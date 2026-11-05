# R6-check H10a: deleting the MIR difference ledger harness (design)

Status: revision 1, uncommitted. Revision 0 got REVISE (`C:\Users\bwff1\AppData\Local\Temp\claude\r5\h10a\design-review.md`, B1–B3,
N1–N10). R5's rulings are in §9; changes are marked "(B1)" etc. Unit row: H0 §4 "H10a | Ledger harness, `Analyzed`, `KIMI_MIR_DIFF`,
`IsTwinRecord`; MIR helpers into `CompilationTestHelper` | R5 | H5's post-merge sweep (N1) | group G:check-tests | ≈ −1,000/+80 t"
(R5-M182; H0c §2 row 1).

Base: dev `4fb6fcdb1` (H5 `70fd9deb9`; W-14, the sweep and C-13 closed). Paths: `C/` = `src/Kimi/Compiler/`, `T/` =
`tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`. Counts are greps on the base. No build, test or run was done.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h10a\design-state.md`: revision 0 04:35–04:53;
revision 1 05:10–05:16; 24 minutes of the hour in all.

## 1. Inventory (item 1)

| Part | Where | Readers on the base |
| --- | --- | --- |
| Observer, compared set, matcher, aggregation | `X/MirDifferenceLedger{,.ComparedSet,.Aggregation}.cs` (158 + 164 + 145) | `Sides`: `X/MirTargetSide.cs` (2), `T/ClosureEnvironmentOriginTest.cs:295, 396, 856`, `T/ClosureResultOriginTest.cs:179`, `T/ClosureResultOriginTest.Makers.cs:390`, `T/MirOriginsTest.Proofs.cs:20`, `T/PairFollowTest.cs:385`; `BindingComplete`, `MirAnalysisOf`: `T/MirCensusTest.cs:61, 85`; `Hash`: none |
| Classification table | `X/TestData/mir-diff/classification.tsv` (71 rows) | `Table` |
| Ledger tests | `T/MirDifferenceTest{,.ComparedSet}.cs` (178 + 170) | — |
| Reverse read (N1) | `T/VerifiedSpecExampleTest.cs:19-21` (`Landed`, "the difference ledger's summary reads it until H10a") | `Aggregation.cs:104` only, so D1 leaves it dead |
| Observation point | `C/Core/Compilation.Pipeline.cs:9`, `:31-35` (`Analyzed`, Transitional R6), `:85`, `:98` | the ledger; `T/PipelineSwitchTest.cs:337-372` |
| Switch contract (N1) | `C/Core/Compilation.Switch.cs:5-6` ("selects only the difference ledger's observation"), `:67` (`CompilerPipeline.Mir`'s summary) | becomes false at H10a (R5-M7 shared pair with `Compilation.Pipeline.cs`) |
| Twin subset | `C/Binding/Binding.OriginFits.cs:29`, `:41-46` (`IsTwinRecord`, R6, "H11 deletes it with the twins"), `:67`, `:147-166`; `Binding.OriginClauses.cs:263` | `ComparedSet.cs:155` only |
| Old records | `X/LegacyRecords.cs` (56): `Of`, `Location`, `Library` | `Of`: `Sides`, `T/SemanticSnapshotTest.cs:254`, `T/AbstractCallReachTest.cs:376`, `T/MirOriginsTest.cs:264`, `T/NeverContinuationTest.cs:446, 464, 484`, `T/OriginRelationDiagnosticTest.cs:328`; `Location`: 13 reads in 6 files plus Aggregation's `Library` (11 in 5 files after D1) |
| Variables | `KIMI_MIR_DIFF`, `KIMI_MIR_LEDGER` | `X/FreshProcess.cs:12`, `T/FreshProcessTest.cs:9, 25`; `scripts/verify.ps1:25-26` (comment only; verify.ps1 has no ledger step) |
| Stale ledger text in comments (N1) | `C/Mir/Analysis/MirFact.cs:5-6`, `MirProblem.cs:7-8` (R4's); `C/Binding/Binding.Lints.cs:100-102` (`LintErrorRecords`, BU's); `X/DiagnosticCorpus.cs:122` (`SpecDifference`); `T/ClosureResultOriginTest.Arguments.cs:11` | consent list, §10 |
| Documents | VERIFICATION `:94`, `:98`, `:151`, `:160-161`; CODEMAP `:56`; ARCH `:248-253`, `:258-259`, `:294`; DIAGNOSTICS `:299`; DECISIONS (history) | — |
| PLAN rows (N1; R5's landing PLAN commit, not the unit) | `:24`, `:29` ("`KIMI_PIPELINE` selects only the ledger and the mark"), `:28` (R4's holdings "and the difference ledger"), G106, G396's X-1a, G355 (`b-identity-places`), G419 ("a full mir ledger before review"), G318 | — |

F13 recount: H0c's 13 `Sides` reader files are 7 on the base. `MirTargetSide.Check` has 47 calls in 33 files, and `At` 32 more.
`src/Benchmark` and `src/Playground` read no harness member.

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | Delete the observer, the compared set, the matcher, the aggregation, the table, the ledger tests and `VerifiedSpecExampleTest.Landed` (N1). Before that, the tests in D12 are re-pointed | Nothing classifies against the ledger after H5 (D-c2). W-14 was its last run | `X/MirDifferenceLedger*.cs`, `X/TestData/mir-diff/`, `T/MirDifferenceTest*.cs`, `T/VerifiedSpecExampleTest.cs` |
| D2 | Delete `Compilation.Analyzed`, its call, the header line `:9` and `AnalyzeNotifiesTheObserverOnce`. Reword `Compilation.Switch.cs:5-6` and `:67`: the pipeline selects only the identity mark and the manifest entry (N1) | Its one subscriber goes. It is Transitional R6, due at H10a | `C/Core/Compilation.Pipeline.cs`, `C/Core/Compilation.Switch.cs` (R5-M7 hold in PLAN §3), `T/PipelineSwitchTest.cs` |
| D3 (B1) | **The MIR side** is `MirTargetSide.MirSide(Compilation)`, giving `(DiagnosticRequirement? Requirement, DiagnosticCode Code, Koto Node)` rows. First come Binding's issues and HIR lint Error records, in their order, without the twins. Then come `Compilation.Problems`' rows as the publisher has them, with no requirement and the subject through `SyntaxOf`. Those rows are every `MirReport.Merged` problem (both record classes, derived problems, merged across bodies), then each validator finding as `InternalInvariant_Kd`. The publisher locates findings under ControlFlow, and MirSide names no requirement for them. MirSide never runs the old analysis | One owner for the verdict's rows: the publisher's list (`Compilation.Pipeline.cs:58-82`, `Compilation.Publication.cs:143-160`), so no second copy of its selection. `Sides`' exclusions (construction records only, derived problems out, per-body listing) were the ledger's comparison rules. H0c row 1's "published MIR records". Derived rows: §9 Q7 | `X/MirTargetSide.cs` |
| D4 (N2) | **The twin filter**, exactly `Sides`' filter: a Binding row whose requirement is `Binding(OriginRelation)`, whose code is `UnsatisfiedOriginRelation_Kd` or `UnprovenOriginRelation_Kd`, and whose node `IsTwinRecord` is left out. A twin node that failed with OriginContract keeps its `UnprovenOriginContract_Kd`, which `!IsTwinRecord(node)` alone would drop. `IsTwinRecord` stays to H11, its tag's stage; H11 removes it together with MirSide's filter, its one reader. The summary and Reason are reworded (consent, §10) | Binding's twins publish beside A5's since H5 (T50), and the published pins move once, at H11 (review N2). Dropping the filter now would move every twin-node MirSide expectation twice | `X/MirTargetSide.cs`, `C/Binding/Binding.OriginFits.cs:41-45` (comment) |
| D5 | `LegacyRecords` keeps `Of` until H10b, and its doc drops the ledger. `Location`, `Document` and `Library` move to `CompilationTestHelper` | `Of`'s remaining readers assert the old analysis for its own subject (§3). `Location` has 5 reader files across subjects, so it belongs in the shared helper, unlike D6 and D7's single-owner items | `X/LegacyRecords.cs`, `X/CompilationTestHelper.cs`, 5 files |
| D6 (N4) | The census table moves to `MirCensusTest`, with `BindingComplete`: 21 distinct (MIR requirement, code, analysis) pairs. The 24 MIR-column rows repeat 3 Binding rows. With them move the consistency check (`MirProblemsNameRequirementsOfTheComparedSet`, PLAN10 §7), the defect half of `MirProblemsTakeTheirPairsAnalysisAndADefectIsARow` (an out-of-table pair and the disagreement record get no analysis) and the uniqueness half of `TheComparedSetCoversEachOwnershipCode` (each pair names one analysis). The image half (checked against `OwnershipIssue.Code`) is old-subject and goes | Their one reader is the census (AGENTS: reuse the owning class) | `T/MirCensusTest.cs` |
| D7 (N4, N10) | `MirTargetSide` keeps its name, `Check` and `At`, and owns `MirSide`. Its contract states D3 and D4: Binding's records except the twins (until H11), and the publisher's MIR rows | It is the group's one record pin (group review A), with 79 call sites; a rename would be churn | `X/MirTargetSide.cs` |
| D8 (N5) | `SemanticSnapshotTest.Print` prints `MirSide` in place of `LegacyRecords.Of`. The `? binding` lines keep their format, and a MIR row prints as `? mir {code} {location}`. `Print`'s comment (`:236`) changes. The semantic baselines are re-made. This lands in its own commit | H0c row 1 (no departure). The snapshot ran the old analysis for every program (group review B R2) | `T/SemanticSnapshotTest.cs` |
| D9 | `FreshProcess`' doc drops `KIMI_MIR_DIFF`. `FreshProcessTest` keeps the row as "another KIMI_ variable", and its comment names the remaining evidence variables | The child filter still guards evidence | `X/FreshProcess.cs`, `T/FreshProcessTest.cs` |
| D10 (N6) | **Restored coverage** (T-04 N3, group review B R3). In `OwnershipStorageLimitTest`, add a theory over MirAnalysisTest's A5-arena program (2,000 reference locals across branches), with and without an independent `MovedPlace_Kd`, published through `ReportAnalysis` and `Diagnostics.Finalize`. It asserts one `OwnershipStorageLimit_Kd` at `func f`, its published arguments (`requiredBytes` > `limitBytes` = 64 MiB, `table` "Loans"), the independent record beside it, `Accepts` false and no generation. The old-limit test keeps T-35's invalidations, and its comment drops the observer | No test publishes the MIR's limit end to end. It passes on the base too (H5 publishes it), so it is not a regression test | `T/OwnershipStorageLimitTest.cs` |
| D11 | One R5 DECISIONS entry records the departures from R5-M182: D4 (D-c2's `IsTwinRecord` at H10a moves to H11, and MirSide's filter becomes H11's reader, N2); D5 (`LegacyRecords.cs` stays to H10b); D3, D6 and D7 (the H0 row's "MIR helpers into `CompilationTestHelper`": MirSide lives in `MirTargetSide` and the census table in `MirCensusTest`, N4); and `MirTargetSide` staying (H0c row 1's "else goes"). Each names the reason that no longer holds | AGENTS: a superseding entry | `docs/dev/DECISIONS.md` |
| D12 (B2) | Re-point, with their rows unchanged, the tests whose subjects survive. `OnlyTwinsOfBindingsOriginRecordsAreCompared` (5 forms, including the swapped mixed fit and the call-position pass-through) moves to `OriginRelationDiagnosticTest`: "compared" means absent from MirSide, and "passed" means present. `ADerivedProblemLeavesTheMirSide` becomes the positive form in `MirPublicationTest`: the derived `ComparisonLoanConflict_Kd` is a MirSide row (D3), and the published set holds its cause's `UnsatisfiedOriginRelation_Kd` and no `ComparisonLoanConflict_Kd` (suppressed beside its cause). It is kept because `ADerivedProblemNamesItsMirCausesKey` pins only an injected problem. D6's three checks move as stated there | AGENTS: never weaken. Each kept exclusion (the twins) keeps its test | `T/OriginRelationDiagnosticTest.cs`, `T/MirPublicationTest.cs`, `T/MirCensusTest.cs` |
| D13 (B3) | `NeverContinuationTest:464` (`MovedPlace_Kd` after `x@move`) and `:484` (the Loan conflict) read MirSide's codes in place of `LegacyRecords.Of` | Their subject is the SPEC verdict (14.9.3, 14.10.3, 13.5.6), and the MIR has decided it since H5. Before D1, the implementer reads W-14's ledger rows for these programs; a difference there is the named class | `T/NeverContinuationTest.cs` |

## 3. Split against H8a and H10b (item 4)

A reader whose subject is the MIR verdict moves to MirSide at H10a. A reader whose subject is the old analysis keeps `LegacyRecords.Of`
or the entry until H10b, or until H8a for the `Bodies`/`Issues` residue. An old-side assertion that was read through `Sides` is
re-pointed to `LegacyRecords.Of` (`common + old + passed` is exactly `Of`'s rows), never deleted.

| Test | H10a | Then |
| --- | --- | --- |
| `ClosureEnvironmentOriginTest:295` (both sides), `:856` (`OnlyTheOldAnalysisRejects...`) | MIR side → MirSide; old side → `Of` | H10b deletes the old halves |
| `ClosureEnvironmentOriginTest:396`, `ClosureResultOriginTest.cs:179`, `MirOriginsTest.Proofs:20`, `PairFollowTest:385` | → MirSide | — |
| `ClosureResultOriginTest.Makers.AssertSides` (`:379-410`; its 4 `Arguments.cs` callers) | MIR side → MirSide; D1/D2 → `Of`; the stale "published on both pipelines" text goes | H10b deletes D1/D2 |
| `NeverContinuationTest:464`, `:484` (B3) | → MirSide (D13) | — |
| `NeverContinuationTest:446` (the old limit's `Unsupported_Kd`), `OriginRelationDiagnosticTest:328`, `MirOriginsTest:264`, `AbstractCallReachTest` `OldPathGaps`/`Records().Old` | unchanged | H10b (H11 for `:264`'s Binding twins) |
| `ChildMoveActivationTest` (4), `CallReservationTest.DiagnosticsIdentifyPreparationOrActivation`, `BlockEscapeLoanTest.WarmEscapeChecksAllocateNothing`, the old storage limit | unchanged (they read `AnalyzeLegacyOwnership().Issues`) | H8a / H10b |

## 4. Consumers outside R5 (item 2): notices before the merge

| Consumer | After H10a | Notice to |
| --- | --- | --- |
| The windows' "mir ledger" lane (board §1; R7 plan §9 "until H10") | Ends. An A5-verdict or library unit runs RP14's successor: the generated gate (843 programs), the A5 class set and `MirCensusTest` (RP14's "after H10" means H10a) | M, F, G |
| The board column "A5 verdict change (ledger or gate run)" | gate run only | M |
| `shared-base/<sha>/mir-diff/` | No new one. The last full ledger is `shared-base/1415d6b42/mir-diff/` (W-14) | M, F |
| `shared-base/<sha>/semantic/` (N5) | Bases taken before H10a are not comparable after D8. The next base is re-made at H10a's merge | M, F, G |
| BU's gates (`bin/worktrees/bu-gate-*`) | Their ledger runs end, and the generated gate stays. A script that still sets `KIMI_MIR_DIFF` only loses the observer | F |
| G216 (a)'s oracle (`artifacts/verify/g216a-oracle/one.sh`) | Unaffected (`KIMI_PIPELINE` on the CLI, moot since H5, gone at H12) | G |
| Units in flight that add a `classification.tsv` row or read `Sides`/`Entry`; tests pinning `MirTargetSide.Check` rows that §8 moves | They rebase onto MirSide and drop the row; the moved rows are listed in commit 2 | all |
| PLAN rows (§1) | R5's landing PLAN commit closes G106 and X-1a, restates G355 and G419, and fixes `:24`, `:28` and `:29` | M (G106, G396, G419), F (G355, G318) |

## 5. The exit gate (item 3)

H0 settled it. Plan §5.5 item 1 classifies the diagnostic snapshot's differences (a)–(e). H3b's table did that with each row's category
(H0b-14, N4), and H5 landed it. The (e) list is STATUS's limits with their PLAN rows. H15 reads STATUS, PLAN and the snapshot, and RP14
and the R7 plan's acceptance row switch to the gate after H10. So nothing after H10a reads the ledger's categories, though the
categories themselves stay defined (N8, §7).

W-14's 101 open blocking rows (A1 2, A5 99) are X-1's verdicted rows (N9). X-1 gave verdicts to 105 rows at R4's exit (73 restated,
17 (a), 15 (d); `draft/Sketches/A5 Step 2/R4 exit/x1-half*.tsv`), and 101 remained after the flip in M's W-13 reading. So no old
rejection the MIR lacks leaves without a verdict, and X-1a closes with nothing to replace it. The evidence to keep is W-14's ledger,
which H10a's commit names. A run at H10a's base would repeat W-14, since no input-changing commit has been pushed since.

## 6. Files and sizes (item 7)

| File | Change | Lines (H) |
| --- | --- | --- |
| `X/MirDifferenceLedger*.cs`; `T/MirDifferenceTest*.cs`; `classification.tsv` | deleted (the D6 and D12 checks move first) | −467; −348; −71 data |
| `C/Core/Compilation.Pipeline.cs`, `Compilation.Switch.cs` | D2 | −8/+3 |
| `C/Binding/*.cs`, `C/Mir/Analysis/Mir{Fact,Problem}.cs` | comments (consent list) | ±10 |
| `X/MirTargetSide.cs` (66 → ≈ 110) | MirSide with the twin filter | +45 |
| `T/MirCensusTest.cs` (105 → ≈ 170), `X/CompilationTestHelper.cs` (+12), `X/LegacyRecords.cs` (−12) | D5, D6 | +80/−12 |
| `T/OriginRelationDiagnosticTest.cs`, `T/MirPublicationTest.cs` | D12 | +45 |
| 10 reader files, `T/NeverContinuationTest.cs`, `T/SemanticSnapshotTest.cs`, `T/VerifiedSpecExampleTest.cs`, `FreshProcess*` | re-points, D8, D13, `Landed` | ±50, −3 |
| commit 2's moved expectations (§8) | about 15–35 rows | ±40 |
| `T/PipelineSwitchTest.cs`; `T/OwnershipStorageLimitTest.cs` | D2; D10 | −36; +30 |

Net ≈ −960/+300 (−660): tests ≈ −585, test data −71, compiler −5. No file passes a structure limit. `MirCensusTest` and `MirTargetSide` stay
far under 5,000 tokens, and the large test files shrink.

## 7. Verification (item 6)

- **Class: Major, with one design review** (WORKFLOW Design 4: a departure from R5-M182, D11). This review was it. The group review of
  G:check-tests covers the implementation.
- **Commits**, in order:
  1. Behavior-preserving move: MirSide with `Sides`' exact rows (common + mir, `Sides`' order) in `MirTargetSide`; D5, D6, D7; the
     re-points; and D12 with its rows unchanged (the derived test still in its exclusion form). The ledger remains. The rows are equal:
     `common` differs only by Binding's `VirtualEffectBound` records, which have no producer, and the old analysis's run cannot change
     `common` (review N3).
  2. Behavior: MirSide takes D3's rows. It names each moved expectation per test with its Q13 class (collection records, derived
     problems by their code, merged duplicates). It settles group review tests-b N1: each `RawAddressTest` `valid` row names P42's
     collection record and is checked against the (e)/P42 list. A row not on that list stops the unit and is reported as an unnamed
     rejection. D12's derived test takes its positive form, and D13 lands here.
  3. D8: the semantic snapshot, its `?`-line difference counted.
  4. Deletion: D1, D2, D9, D11 and the documents.
  5. D10, as restored coverage.
- **Unit checks:** the changed classes (`ClosureEnvironmentOriginTest`, `ClosureResultOriginTest`, `MirOriginsTest`, `PairFollowTest`,
  `MirCensusTest`, `MirPublicationTest`, `OriginRelationDiagnosticTest`, `NeverContinuationTest`, `SemanticSnapshotTest`,
  `PipelineSwitchTest`, `FreshProcessTest`, `OwnershipStorageLimitTest`, `MinimalEmissionTest`, `VerifiedSpecExampleTest` (N7),
  `ArchitectureRulesTest`) and the 33 classes that call `MirTargetSide.Check` (commit 2 moves rows in them). Also build `-warnaserror`
  and the slnx. The tooling row needs nothing: verify.ps1 changes only in its comment.
- **The window without the ledger:** the default Session with every native fixture (`'*.ll'` O0/O2), the 40 milestones, the five CLI
  harnesses, `KimiLibraryShapeTest` and `Purpose=Allocation`. The diagnostic snapshot against `shared-base/<H10a base>/` shows
  0 differences, since no producer changes. The semantic snapshot is re-made (D8). `ArchitectureRulesTest`'s ratchets are unchanged,
  since no `AnalyzeLegacyOwnership().Issues/ControlFlow` read moves (N7). There is no `-Pipeline mir` run.
- **Documents (Auto):**
  - VERIFICATION: `:94` (mir adds only the mark); `:98` deleted; `:151` and `:160-161` drop the ledger.
  - CODEMAP `:56`: drops the ledger links and the `Analyzed` sentence, and names `MirTargetSide.MirSide`.
  - ARCH (N8): `:248-253` keeps the categories, as "Difference categories (a) old-path error, (b) new-path error, (c) SPEC ambiguity,
    (d) intended change with clause and Appendix B item, (e) a form not ported under D2 (STATUS, PLAN), used by exit-gate item 1,
    spec-cases' `Category` and the (e) list; the difference ledger that recorded them ended at R6-check H10a (last ledger
    `shared-base/1415d6b42/mir-diff/`)". `:258-259`: "`Binding.IsTwinRecord`, the test MIR side's twin filter (H11)". `:294` drops
    `MirDifferenceLedger.cs`.
  - DIAGNOSTICS `:299`: "leaves the test MIR side" becomes "publishes as `PrerequisiteUnavailable_Kd` or beside its cause".
  - The `scripts/verify.ps1:25-26` comment.
  - DECISIONS: D11 only. PLAN: R5's landing commit (§4).

## 8. Moved `MirTargetSide.Check` expectations under D3 (estimate)

| Source | Rows (H) | Q13 class | Named |
| --- | --- | --- | --- |
| P42's `StaticStorage` collection record (`InstanceCollector.Capabilities.cs:65`) in programs with a settable static: `EffectBoundImplementationTest`'s 12 `Sink` rows (`Metrics.puts`, through `AssertBound`; its comment `:251-252` says the record is excluded), `EffectBoundReportTest`'s `Metrics` rows, `RawAddressTest`'s `valid` rows (tests-b N1) | 14–18 | (e), P42 (STATUS "Static members"); a valid row gains its rejection, an invalid row gains a record | P42 per row (or stop) |
| Merged duplicates: the 9 calls that expect `(code, loop)` twice (`BlockEscapeLoan`, `BorrowingIteration`, `FixedArrayEntry` ×2, `IntegerRange`, `OwnedLendingIteration`, `SliceOperations`, `UserIteration` ×2) collapse to one where the two are one (code, subject, Reason) in two bodies | 0–9 | 3 (record merged) | — |
| Derived problems now on the side under their code (rule W) | 0–5 | 4 (counted, dependent) | — |
| The collector's G128 `Unsupported_Kd` beside `OwnershipDepthLimit_Kd` (`TypeDagScalingTest:175`, `DeepNest(60)`) | 0–1 | (e), G128 (H0a-11) | G128 |

Total ≈ 15–33 rows, under the ~40 threshold. The other MirSide readers (§3) move at most a few rows (the Makers' MIR lines only where
a program has a collection record). Commit 2's first run fixes the exact count.

## 9. R5's rulings on the review (2026-11-05)

- **B1 → option (a).** The MIR side is Binding's records without the twins until H11 (exactly `Sides`' filter, N2), plus
  `Compilation.Problems` as the publisher has them: collection records in, derived problems, merging across bodies, and findings
  (ControlFlow).
  - Reason: this removes the second copy of the publisher's selection (AGENTS: share by meaning, assert at the lowest public layer),
    and it matches H0c row 1, so D8 needs no departure.
  - It lands as its own behavior commit after the move. That commit names each moved expectation per test with its Q13 class and
    settles tests-b N1 against the (e)/P42 list; a row not on that list stops the unit.
- **B2.** Re-point the named tests with their rows unchanged. A test whose exclusion is gone (`ADerivedProblemLeavesTheMirSide`)
  becomes the positive form, or is deleted only if a publication test already pins exactly that, named. Every kept exclusion (the
  twins) keeps its test.
- **B3.** Move `NeverContinuationTest:464` and `:484` to the MIR side at H10a.
- **Adopted non-blocking:**
  - N1: the inventory additions; `Landed` is removed. Other owners' comment edits go in a consent list. The PLAN rows go to R5's
    landing PLAN commit.
  - N2: the filter's exact form, with H11's reader named.
  - N4: MirSide lives in `MirTargetSide`; the census table and `BindingComplete` live in `MirCensusTest`; D11 records this.
  - N5: D8 gets its own commit, and the semantic-base notice is in §4.
  - N6: D10 is restored coverage. N7: §7 is fixed, with `VerifiedSpecExampleTest` added. N8: ARCH keeps the categories. N9: in §5.
    N10: follows from B1.
- An estimate of the moved `MirTargetSide.Check` expectations is §8; it is below 40.

## 10. Open questions and the consent list

| # | Question | Recommendation |
| --- | --- | --- |
| Q7 | My reading of B1's "derived problems under their published code": MirSide lists a derived row under the code `Compilation.Problems` gives it, the merged problem's own code (the rows the verdict counts). The publisher then writes `PrerequisiteUnavailable_Kd` when the row publishes alone and suppresses it beside its cause (`Compilation.Publication.cs:18, 40`). Reproducing that in MirSide would be the second copy B1 forbids. D12's positive form pins both layers | Confirm this reading. It blocks commit 2 only |
| Q3 | Rename `MirTargetSide`, now that one side remains? | No (non-blocking) |
| Q6 | `LintErrorRecords` after H10b, MirSide's direct read: keep it as Binding's fact with a new reason, or read the published lint Errors? | H10b's design (non-blocking) |

Consent list (comment edits in other owners' files; R5 asks before commit 4):
- **R4 (M):** `C/Mir/Analysis/MirFact.cs:5-6` and `MirProblem.cs:7-8`. Their Outputs say "the difference ledger reads", which becomes
  "the publisher and the test MIR side read".
- **BU (F):**
  - `C/Binding/Binding.OriginFits.cs:41-45`: `IsTwinRecord`'s summary and Reason say "the test MIR side leaves these out, A5's twin
    stands for each; H11 deletes it with the twins and MirSide's filter".
  - `C/Binding/Binding.Lints.cs:100-102`: `LintErrorRecords`' summary and Reason name MirSide and `LegacyRecords` as readers, H10b.
- Without consent, the comment stays stale, and the commit names the stale lines with their removal unit (H11 or H10b).
- **Q7 (revision 1): confirmed.** MirSide lists each derived problem under the code `Compilation.Problems` gives it, the merged problem's own
  code, which is the row the verdict counts (the MIR layer, AGENTS: assert a rule at the lowest public layer that observes it). The
  publisher's re-coding (`PrerequisiteUnavailable_Kd` alone, suppressed beside its cause) is the publication layer's rule, pinned by D12's
  positive form in MirPublicationTest and by the diagnostic snapshot; MirSide does not copy it. B1's "published code" meant "as
  `Compilation.Problems` has it".

### R5's fixes after design review 2 (REVISE: C1, C2; WORKFLOW Review table: the planning session fixes the design)

- **C1:** `MirSide` calls `Analyze()` on the compilation before it reads `Compilation.Problems` (a no-op on an analyzed current version),
  so its MIR half is never empty for lack of analysis. The string overload's summary says it analyzes; the `MirBuildTest.Compile` readers
  (`ClosureEnvironmentOriginTest:396` and D12's two moved tests) and the 8 string-overload calls (`ContentUpdateTest:119`,
  `DefaultAccessTest:47`, `DictionaryStoredReferenceTest:31, 253, 276`, `LocalRegionCompletionTest:44`, `LocalRegionCompositionTest:67`,
  `StoredReferenceLoanTest:140`) are named in §3 and in commit 2; a row that then gains a record is a moved expectation of commit 2.
- **C2:** D12's twin test: "compared" = present among Binding's issues (code, node) **and** absent from `MirSide`; "passed" = present in
  both.
- **Review 2's N1–N4 adopted:** the 5 classes reaching `MirTargetSide.Check` through `EffectBoundImplementationTest.AssertBound`
  (EffectBoundReport, ForeignBorrowImport, RequirementIdentity, UserArithmeticOperator, Utf8ReserveEffect) join the unit checks; each moved
  row is named "already published on the base" (checked against the base's `Compilation.Problems` or the published diagnostics; the
  diagnostic snapshot stays 0), the P42 rows checked against STATUS's (e), and a row outside it stops the unit; from commit 1 to commit 3
  the MIR column exists twice (the ledger's `Compared` and MirCensusTest's table), commit 4 removes the copy; commit 4's evidence against
  commit 3 is the diagnostic snapshot 0 and the semantic snapshot unchanged against commit 3's baseline; Q7's citation is
  `src/Kimi/Diagnostics/DiagnosticOwner.cs:162` (finalization suppresses beside a cause; the publisher writes `PrerequisiteUnavailable_Kd`),
  DIAGNOSTICS `:299` worded to match; R4 is told that the compared set's MIR column (R4-M67) and its checks move into MirCensusTest.
- **The estimate past 40** (34–52): accepted in one behavior commit, since no published record changes (snapshot 0) and each row is named.
  The implementation runs in two phases under Q12, each within its hour: **H10a-i** = commits 1–2 (the move, the behavior), **H10a-ii** =
  commits 3–5 (D8, the deletion, D10). One implementation review after H10a-ii by design review 2's reviewer, which first checks that C1
  and C2 are resolved (WORKFLOW Repeated reviews 1); then H8a continues the group G:check-tests.
