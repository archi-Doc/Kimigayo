# R6-check H10b: deleting the old analysis (design)

Status: revision 1. Revision 0 got REVISE from BU's design review (B1, N1–N3, OQ answers;
`C:\Users\bwff1\AppData\Local\Temp\claude\bu\batch\H10b\design-review.md`, member lists beside it). The changes are marked "(B1)",
"(OQ5)" and so on; the rulings are in §7. Unit row (H0 §4, R5-M182): "H10b | Delete `C/Analysis/` (50 files), its readers and the
transitional entry | R5 | H8a–c, H9, H10a | group G:check-del | ≈ −16,300". Inputs: H0-design §3 (D-c1, D-c2, D-c4), §4, §6 (F10,
F13); H0c §1, §2 row 7, §5; H8a §4; H8b D6, D8, D9, D10, D11, §4; H8c §1 (OQ3), §4; H9 §6, D7; H10a Q6; PLAN G428 and G52 on dev
`9e961e612`; the coordinator's chain input (2026-11-05 10:55).

Base: G:check-tests' head `claude/r5-h10a` (revision 0 read `4b5687d01`; `9253ebd42` since, documents and comments only) merged with
H9's `claude/r5-check-del` `4064ae3e5`; the review's composition is the tree `b775ff0f0` (`git merge-tree`; code merges cleanly, the
conflicts are in CODEMAP, ARCH and DECISIONS). `src/Kimi` is h10a's except `C/Core/Compilation.Pipeline.cs` (check-del's), tests are
h10a's, `src/Benchmark` is check-del's. Every count is a `git grep`/`git show | wc -l` on that composition. Paths: `C/` =
`src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`. No build, test or probe was run.

Working time (Q12), logged in `design-state.md` beside this file: revision 0 10:55–11:11; revision 1 11:28–11:31 (2026-11-05).

## 0. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **Split at the control-flow analysis.** H10b deletes the old ownership analysis: 47 of the 48 files (14,446 lines), the transitional entry and every member only they read in R5's files. `C/Analysis/ControlFlowAnalysis.cs` (967), `Compilation.AnalyzeControlFlow`, `Binding.LintErrors` and the control-flow ratchet row stay until **H10b-2**, which deletes them once the three G428 stops are moved (§3; DefersCoverage moves in C1, D11). | The stops read only `AnalyzeControlFlow()` (and one `LintErrors`), never the entry. `ControlFlowAnalysis.cs` is self-contained (its own nested `Flow`, `DefaultCompletion`, `ChildCollector`; none of the other 47 files' 68 types; BU verified) and reads only live members plus `ElementAccess.UpdateOperator`. Waiting with all 48 files holds ≈ 14,450 lines and H11, H12 and H14 behind other sessions' fixes for nothing. The split pins nothing and loses nothing. | §2 |
| D2 | **Start conditions.** H10b: G:check-tests and H9 merged on dev (group reviews passed), BU's consent (given, §7 OQ3), the board hold on `Core/Compilation.Pipeline.cs` (ARCH hold list), G:check-del's window. **No dependency on G52 or G428.** H10b-2: the control-flow ratchet row at 0, i.e. **BU's G52 second shape → R4's G428** landed (G428's fix moves CurrentControlFlow…, NeverSuccess…, AbruptSubject…). (OQ5) | The owner's 「BU の G52 の修正を先にする で決定です」 orders G52's second shape before G428; G428's row names H10b as its deadline, which D1 makes H10b-2. G52's join part no longer gates anything (D11). | — |
| D3 | **Commit order.** C1 (bp): the entry and its own test, `InvalidateOwnership` renamed `InvalidateMir` (N2), DefersCoverage moved (D11), the control-flow limit 4 → 3; C2 (bp): the old ownership analysis, now unreferenced, with every member only it read (D5–D7, B1), the zero-reference rows and the DECISIONS entry; C3 (doc): ARCH, CODEMAP. H10b-2 is one commit C4. Each commit's evidence includes the grep that proves what it deletes is unreferenced; C2's also carries the name-based list (B1). | AGENTS "Separate commits"; brief item 2. The entry is the old analysis's last live reference, so it goes first; then the whole analysis is provably dead. | §2 |
| D4 | **Q6 (H10a Q6, H8b D6): `Binding.LintErrorRecords` stays as a lasting Binding fact** (OQ2, BU agrees), untagged. Its summary states the contract: "Gets the pass's HIR lint records of Error severity, derived ones included, in walk order before `ReportDiagnostics` sorts them; with `Issues`, Binding's Error records before finalization. Readers: tests only (the test MIR side and R5-M32's derived-record test); no compiler path reads it, publication reads the lint list itself." `LintErrors` loses its old reader at C2; its summary, the header line `:14` and its `[Transitional("R6")]` Reason name its one reader, the G428 (1) stop, and its removal at C4 (OQ3). Both `LintErrorRecords` readers stay unchanged (`X/MirTargetSide.cs:57`, `T/DiagnosticPrecisionTest.cs:315`). | Re-pointing to the published lint Errors loses facts: finalization suppresses a derived record whose cause is published (`DiagnosticOwner.Finalize`; `Candidates` is private), so R5-M32's "recorded derived, not nothing" has no other observation, and MirSide's rows are pre-finalization records at exact nodes (H10a D3, D7). Re-deriving dependence in tests would copy Binding's guess rule. The member is the lint half of the raw view `Binding.Issues` and `Compilation.Problems` give; no second representation. | `C/Binding/Binding.Lints.cs` |
| D5 | **H8c OQ3's two old-only readers go in C2.** `ElementAccess.SupportsBorrowRoot`/`SupportsMoveRoot` (`C/ElementAccess.cs:10-15`) and `BoundEnumConstruction.acquisitions`/`Acquisitions`/`SetAcquisition` with the array allocation (`:56`) and the write (`:629`) in `C/Binding/Binding.Enums.cs:32, 40, 56, 63, 629`; `ProveCopy` and the `TransferRequired` branch stay. | They name `OwnershipPlace`/`AcquisitionKind`, so C2 cannot build without them; their only readers are in the deleted files. | `C/ElementAccess.cs`, `C/Binding/Binding.Enums.cs` |
| D6 | **The non-Binding closure goes in C2 (OQ4).** Root-helper members left with no reader by C2: `ElementAccess.ValueSource` (its signature names `OwnershipBody`), `ConversionKind`, `IsIndexerCall`, `DesignatedType`, `IsBorrowedSelection`, `IsFollowedRoot`, `IsRuntimeElementPlace`, `ElementPathBase`, `HasElementRoute`, `KeySyntax`, `PathSelector` (≈ −120 with D5's two; `UpdateOperator` stays for the control-flow analysis to C4), **(B1)** `ReferenceTypes.IsValue` (`C/ReferenceTypes.cs:71`), `FormattingTypes.IsSliceBorrow` (`C/FormattingTypes.cs:14`), `SharedReadTypes.ReadsStoredPointer` (`C/SharedReadTypes.cs:17-22`), and `C/MatchTypes.cs` (88), `C/StaticScalar.cs` (59). Binding's newly unread members go to H11's list (§4, N1). | AGENTS: remove what the change makes redundant in the same unit. These are R5's files; each has only its declaration left (a member a live member calls, such as `ReachesThroughBorrow`, `HasReceiverOnPath`, `FollowedReference`, `BorrowSemantics`, `IsUtf8Slice`, stays). Departs from H0c's rows (PR-1's `IsIndexerCall`/`DesignatedType`, `MatchTypes`/`StaticScalar` at H11), recorded in D9. | `C/ElementAccess.cs`, `C/ReferenceTypes.cs`, `C/FormattingTypes.cs`, `C/SharedReadTypes.cs`, 2 `git rm` |
| D7 | **`DeferredExpansionLimit_Kd` and its requirement go in C2:** the enum member `DiagnosticCode.cs:164` and catalog row `DiagnosticCode.tinyhand:749-751`; **(B1)** `OwnershipRequirement.ExpansionLimit` (`DiagnosticRequirement.cs:21`) and its row `Ownership.ExpansionLimit` (`DiagnosticRequirement.tinyhand:609-610`). `T/DeferredEmissionTest.cs` loses `:158-159` (the `c.ReportAnalysis()` and the code's absence; N3: `c.Accepts` in the loop already implies no published Error) and `:143`'s comment drops the code's name. No ordinal or count is pinned: codes and requirements are catalogued by name, and the requirement table enumerates `Enum.GetValues<OwnershipRequirement>()` against its rows, so member and row go together. | The code's one reporter and the requirement's two producers are in the deleted files (`OwnershipModel.cs:198`, `OwnershipAnalysis.cs:235`); verified: 0 other reporters, 0 docs, 0 spec-cases; `MirBuilder.Defaults.ExpansionLimit` is a private constant, a homonym. Every other code the old analysis reports has another reporter (23 codes). | `src/Kimi/Diagnostics/DiagnosticCode.{cs,tinyhand}`, `DiagnosticRequirement.{cs,tinyhand}`, `T/DeferredEmissionTest.cs` |
| D8 | **Ratchets → zero-reference rows (D-c4, first step).** C2 replaces `LegacyOwnership` and its four limit-0 rows (`T/ArchitectureRulesTest.cs:28-29`, `:54-62`) by three rows over the old ownership names (the deleted files' top-level types, `AnalyzeLegacyOwnership`, `DeferredExpansionLimit_Kd`; a name with a live homonym is left out, a letter in brackets keeps the row from matching itself) in `tests`, `src/Benchmark`, `src/Playground`. The control-flow row stays (limit 3 after C1, its comment naming the three G428 stops); C4 replaces it by H8b D9's pattern in the same three directories plus `src/Kimi`, which by then names none. `src/Kimi`'s ownership-name row is H11's (D-c4: `Binding.RawOrigins.cs:48`, `Binding.ValueCallGate.cs:8, 27`). | A row lands in the commit that makes it true; comments count. | `T/ArchitectureRulesTest.cs` |
| D9 | **One DECISIONS entry in C2** (the next free R5-M, R5-M189 on the base): the split and H10b-2's start condition (D1, D2); the ended temporaries (`AnalyzeLegacyOwnership`, `Compilation.ownership`, `legacyAnalyzed`, `OwnershipAnalysis`'s four `("R6-check")` stops and gate, R4-M152/R4-M159's old halves); D4's lasting `LintErrorRecords` with one line naming BU's consent (2026-11-05, H10b design review OQ2); D6's departure from H0c's H11 rows; D7; D11's move and its Explicit regression; the D5 freeze exceptions whose code ends (H0c §5.2). It supersedes R5-M182's H10b row in part (the split) and names R5-M188 (Q6). | AGENTS "Record decisions"; WORKFLOW: departing from an entry adds one. | `docs/dev/DECISIONS.md` |
| D10 | **Class Major with one design review by BU** (WORKFLOW Design 4: D1, D4 and D6 depart from R5-M182's rows; D4 and D6 move work across BU's H11 boundary; D5 edits BU's files). Then one implementation review by a reviewer who wrote neither (R4), then G:check-del's group review. | WORKFLOW Design 4; AGENTS Roles. | — |
| D11 | **(OQ5) DefersCoverage moves in C1; G52's join part gates nothing.** `ControlFlowAnalysisTest.DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever` reads the published Errors (`CompilationTestHelper.PublishedErrors`, H8b D4): for both forms, the cause (`UnresolvedBinding_Kd` at `abort`) is published and no fallthrough or coverage code is (`FunctionFallthrough_Kd`, `RequireFallthrough_Kd`, `NonExhaustiveMatch_Kd`), the old `Issues` read's fact (kind (a)); form 1's exact set is the cause alone. Its `PendingBinding` reads have the old analysis as their subject and go (H8b D1; counterpart: Binding's guess rule, R5-M32). The helper `Analyze(string)` (`:217`) goes with them. Form 2's exact set goes to a new test, **`ControlFlowAnalysisTest.AJoinPastAnUnresolvedCallPublishesOnlyItsCause`**: `[Fact(Explicit = true)]`, asserting the published Errors are exactly `UnresolvedBinding_Kd` at `abort` for `let x = if true / abort() / else => 1`, its comment naming PLAN G52's join cascade as the condition that makes it a default test (the G264 kept-apart form, `MirBorrowsTest.ArrayDestructions.cs:380-387`: expectation unchanged, Explicit until the row decides it). If a form needs the startup, it gains `public func main() => ()` (H8b D10). | BU's answer: the stop's own subject holds today in both forms; only form 2's exact line needs G52, so it becomes G52's fail-first regression and the wrong `TypeMismatch_Kd '1'` is asserted neither way. No owner decision is needed. | `T/ControlFlowAnalysisTest.cs`, `T/ArchitectureRulesTest.cs` |

## 1. Inventory on the merged base (item 1; F13 recount)

`C/Analysis/`: **48 files, 15,413 lines** (H0c counted 50/16,180 before H5): `OwnershipAnalysis*` 28 files, `OwnershipBody*` 12,
`ControlFlowAnalysis` 967, `OwnershipModel` 537, `OwnershipFlow` 176, `BorrowLiveness` 145, `PackedAnalysisTable` 59,
`OwnershipStorage{,LimitException}` 43, `InterpretationContext` 16. Transitional tags inside: 4 `("R6-check")` (`OwnershipAnalysis.cs:27,
166, 185, 200`). No static extension, `using static`, partial part, reflection or string reference outside the directory (BU).

| Reader outside `C/Analysis` | Site | Commit |
| --- | --- | --- |
| The entry | `Compilation.Pipeline.cs:149-163` (`AnalyzeLegacyOwnership`, `[Transitional("R6-check")]` `:152`), `:29` `legacyAnalyzed`, `:165-170` `InvalidateOwnership` (two old lines; renamed `InvalidateMir`, callers `Binding.cs:108`, `Compilation.cs:340`), header `:10-11`; `Compilation.cs:126` `ownership` | C1 |
| The entry's own test | `T/PipelineSwitchTest.cs:336-351` `TheLegacyOwnershipEntryRunsOncePerVersion` | C1 |
| DefersCoverage stop (G52 join) | `T/ControlFlowAnalysisTest.cs:131-138`, helper `:217` | C1 (D11) |
| Old-only `src/Kimi` readers (H8c OQ3) | `C/ElementAccess.cs:10-15`; `C/Binding/Binding.Enums.cs:32, 40, 56, 63, 629` | C2 (D5) |
| Root-helper closure | `ElementAccess` 11 members; `ReferenceTypes.IsValue`, `FormattingTypes.IsSliceBorrow`, `SharedReadTypes.ReadsStoredPointer` (B1); `C/MatchTypes.cs`, `C/StaticScalar.cs` | C2 (D6) |
| Old limit code and requirement | `DiagnosticCode.cs:164`, `.tinyhand:749-751`; `DiagnosticRequirement.cs:21`, `.tinyhand:609-610` (B1); `T/DeferredEmissionTest.cs:143, 158-159` | C2 (D7) |
| `LintErrors` (old analysis `:104`, `:123`; G428 (1) stop `T/CurrentControlFlowTest.cs:126`) | `C/Binding/Binding.Lints.cs:14, 83-98` | C2 (texts), C4 |
| `LintErrorRecords` | `Binding.Lints.cs:100-104`; `X/MirTargetSide.cs:57`, `T/DiagnosticPrecisionTest.cs:315` | C2 (untag, D4) |
| Ownership ratchet rows | `T/ArchitectureRulesTest.cs:28-29`, `:54-62` (limits 0) | C2 (D8) |
| Control-flow entry and the G428 stops | `Compilation.cs:309-319`; `T/CurrentControlFlowTest.cs:124`, `T/OptionalTryDiscardTest.cs:237`, `T/PatternBindingTest.cs:428`; ratchet `ArchitectureRulesTest.cs:63-69` (limit 3 after C1) | C4 (H10b-2) |
| Comments naming old types | `Binding.RawOrigins.cs:48`, `Binding.ValueCallGate.cs:8, 27` | H11 (D-c4) |
| `IsTwinRecord` | `Binding.OriginFits.cs:46`; `X/MirTargetSide.cs:40, 51` | H11 (H10a D4) |
| `src/Benchmark`, `src/Playground`, scripts | none (H9 §6's grep; scripts 0) | — |
| Docs (Auto) | CODEMAP `:5`, `:21`, `:35`, `:75` (`StaticScalar`); ARCH `:156` (limits' exemption), `:210-244` (freeze exceptions), `:294` ("the old Analysis until H10b"), `:316` (R6 ledger) | C3, C4 |

F13: H0's "50 files, ≈ −16,300" is **48 files; H10b ≈ −14,770, H10b-2 ≈ −1,000** (§5).

## 2. Deletion table (item 2)

| Commit | Path or member | References left before it (proof) | Notes |
| --- | --- | --- | --- |
| C1 (bp) | `Compilation.AnalyzeLegacyOwnership`, `legacyAnalyzed`, `ownership`, `InvalidateOwnership`'s `ownership?.Invalidate()` and `legacyAnalyzed = false` (it keeps `this.mir = null`, renamed `InvalidateMir` at its 3 sites, N2), Pipeline header `:10-11`; `PipelineSwitchTest.TheLegacyOwnershipEntryRunsOncePerVersion`; D11 (DefersCoverage moved, the Explicit regression added, `Analyze(string)` gone, ratchet limit 3) | `git grep AnalyzeLegacyOwnership -- src tests` = the declaration, its test and the `LegacyOwnership` constant with its comment (`ArchitectureRulesTest.cs:29`, `:58`) | Ends the entry's `("R6-check")` tag; the four limit-0 rows still pass |
| C2 (bp) | `git rm` 47 files of `C/Analysis` (all but `ControlFlowAnalysis.cs`); D5; D6 (with B1's three); D7 (with `ExpansionLimit`); `Binding.Lints.cs` texts and `LintErrorRecords` untagged (D4); D8's rows; D9's entry | After C1 no live code names `OwnershipAnalysis`. Evidence: (1) the grep over D8's names returns only the deleted files and D5–D7's sites; after C2, `src/Kimi` 3 comment lines (H11), tests, Benchmark, Playground 0; (2) **(B1) the name-based list:** every member name the 47 files use that, outside them, appears only in its declaration after C2 (BU's method, `dead-candidates.txt`); each hit is deleted here, on H11's list (§4) or in C4's file (`DefaultCompletionPending`, `RecursiveDefault`), or a homonym named in the evidence | Ends 4 `("R6-check")` tags; the D5 freeze exceptions' code (H0c §5.2: PR-1, BU-10, BU-14, BU-19, BU-24's old reads, BU-44's `SlotLoanRequirement`, R4-M126/M152's gate, R4-M159's stop, R4-M106's old part, R5-M159/M160's `AcquireElement`) |
| C3 (doc) | ARCH: the freeze's exception bullets end (their code is gone; the freeze sentence stays for the remaining file until C4), `:294` "until H10b-2", the R6 ledger row ("H10b deleted the old ownership analysis; the control-flow analysis goes at H10b-2"); CODEMAP `:5`, `:21` (one link, `ControlFlowAnalysis.cs`, until H10b-2), `:35` (the old stems go), `:75` (`StaticScalar` goes) | — | Documentation recording decided facts (WORKFLOW: no separate review) |
| C4 (H10b-2) | `ControlFlowAnalysis.cs` (the directory ends), `Compilation.AnalyzeControlFlow`, `Binding.LintErrors` and `Binding.Lints.cs:14`, `ElementAccess.UpdateOperator` if then unread; the control-flow row → zero-reference rows (D8); ARCH `:156`'s "outside the old Analysis path" and the freeze's old-Analysis clause, `:294`; CODEMAP `:5`, `:21`; DECISIONS: one line amending D9's entry | ratchet row at 0 (its count), so `AnalyzeControlFlow` has no reader | Ends `LintErrors`' `("R6")` tag (BU consents, §7) |

Phases (Q12, one hour each): **H10b-i** C1 + C2 (mechanical deletion, the build as the reference check, Unit checks); **H10b-ii** C3
and the landing PLAN commit; **H10b-2** C4 (≈ 20 minutes). The landing PLAN commit (Auto) records the position, rewrites G428's
"before H10b" as "before H10b-2", adds to G52's row the Explicit test of D11 as its fail-first regression (BU records it; R5's landing
names it), and closes the rows H0c §5.3 gives to H10b that are still open (G47, G75, G100, G159 (4), G162 (2), G205, G315; each closing
line names its reason, as the row allows).

## 3. The stopped readers and the start condition (item 3)

| Method | Reads | Cause and owner | Successor (H8b §3) | H10b | H10b-2 needs |
| --- | --- | --- | --- | --- | --- |
| `CurrentControlFlowTest.StatementCompletionAndNestedResultsBind` | `AnalyzeControlFlow()` `:124`, `LintErrors` `:126` | PLAN G428 (1): the validator's `while` Type finding, `InternalInvariant_Kd` (R4); after G52's second shape (BU), the owner's order | V, H; `LintErrors` → P | unchanged | G428's fix moved it |
| `OptionalTryDiscardTest.NeverSuccessStillChecksFailureAndHasNoDiscardWarning` | `AnalyzeControlFlow()` `:237` | G428 (2): A1's `UninitializedPlace_Kd` at `$try.value` (R4) | V | unchanged | G428's fix |
| `PatternBindingTest.AbruptSubjectDoesNotPropagateGuardTransfers` | `AnalyzeControlFlow()` `:428` | G428 (3): A1's `UninitializedPlace_Kd` at `result` (R4) | H, V | unchanged | G428's fix |
| `ControlFlowAnalysisTest.DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever` | its `Analyze(string)` helper `:217` | PLAN G52, the join cascade (BU): `TypeMismatch_Kd` at `1` resting on the failed call | P (D11) | **moved in C1**; form 2's exact set → `AJoinPastAnUnresolvedCallPublishesOnlyItsCause` (Explicit, G52) | — |

Options weighed for the G428 stops: **(A)** all of H10b after the chain: pins nothing, but holds 47 files no stop reads, and H11, H12
and H14 with them. **(B)** H10b first, deciding the pins (G428's row allows it): each stop keeps the facts that hold now (H rows,
Binding-level P), and its failing V line becomes G428's fail-first regression; B pins no wrong verdict but takes the V facts out of the
default run until the fix and makes two sessions edit the same methods twice. **(C, recommended) D1's split:** the G428 stops keep
every read and only H10b-2 waits for G52's second shape → G428. DefersCoverage, whose subject holds today, takes B's form at once
(D11): that costs one Explicit line and removes the G52 join part from the chain.

## 4. H10b vs H11 and H12 (item 4)

- **H11 (BU)** keeps: H0c §1's Binding table and R4-M108's list (the twins, the fit-failure gate, non-call fit rows,
  `CallLocalOrigin`), `IsTwinRecord` (H10a D4), `OriginProofMetrics.Verdict` (H8c D5), the R4-M152 gate's 7 Binding declarations and
  `ContainsOrigin(BoundType, …)` (their `("R6-check")` tags), `PropertyUpdateStorage`, `CollectWitnesses`, `WithoutRawOrigins`'
  comment, `ComparisonPlan(CallPlan)`/`DictionaryComparison` (D-c8), and the `src/Kimi` zero-reference row for the ownership names
  (D-c4). **(N1)** It gains the nine Binding members C2 leaves with only their declaration: `CollectFixedArrayWitnesses`,
  `CollectIntegerPositionWitness`, `ElementPlaceReference`, `ExclusiveArrayHandle`, `InstantiateDefaultCall`, `PayloadReadReference`,
  `IsGenericWrappingInteger` (public), `AdtDef.HasDestructorOnPath`, `BindingSymbol.TransferCapture` (`BindingModel.cs:371`). It loses
  (to H10b, D6): `IsIndexerCall`, `DesignatedType`, `MatchTypes`, `StaticScalar`. H11 needs H10b, not H10b-2: no H11 member is read by
  `ControlFlowAnalysis.cs` alone.
- **H12 (R5)** is unchanged: the pipeline switch, `[mir]`, `-Pipeline`, after H10a and H10b; `ControlFlowAnalysis.cs` reads no switch.
- **H14** after H10b and H11 is unchanged; **H15** needs H10b-2 (exit items 2 and 3: the zero-reference rows and no `("R6")` tag).

## 5. Verification, performance, documents, sizes (items 5, 6)

- **Checks (Unit, each code commit):** incremental build of `Kimi` and `xUnitTest`, then the slnx build (Benchmark and Playground
  compile); classes `ArchitectureRulesTest`, `PipelineSwitchTest`, `DeferredEmissionTest`, `DiagnosticCatalogTest`, `EnumBindingTest`,
  `EnumOwnershipTest`, `DiagnosticPrecisionTest`, the stop classes (`CurrentControlFlowTest`, `OptionalTryDiscardTest`,
  `PatternBindingTest`, `ControlFlowAnalysisTest`; C1 runs `AJoinPastAnUnresolvedCallPublishesOnlyItsCause` explicitly once and
  records that it fails on `TypeMismatch_Kd '1'`, the fail-first evidence), `MirTargetSide`'s heaviest callers
  (`ClosureEnvironmentOriginTest`, `OriginRelationDiagnosticTest`), **(evidence) `HirColumnsTest`, `HirFormsTest`, `MirCensusTest`**,
  and `-Trait Purpose=Allocation`; **(evidence) the semantic snapshot**, `SemanticSnapshotTest` with `KIMI_SEMANTIC_SNAPSHOT=check`
  (D5 changes `BoundEnumConstruction`). C2's evidence carries §2's greps and the name-based list (B1). **G:check-del's window:** Session
  with natives (a family sweep), milestones, CLI, and `-DiagnosticSnapshot` with **0** differences (the old analysis publishes nothing
  since H5). H10b-2: Unit of the three G428 stop classes and `ArchitectureRulesTest`, plus a Session in its window.
- **Performance:** no compile path changes (`Analyze` runs no old analysis since H5); Binding stops allocating one `AcquisitionKind[]`
  per enum construction with payload. Measure base vs head from one folder, pinned and interleaved with `DOTNET_TieredCompilation=0`:
  `--compiler-pipeline` on the O2 corpus inputs (expected equal within noise) and the cold Bind allocation of an enum-payload workload
  through the binding harness (expected lower by those arrays); a cold ceiling that falls is tightened in C2; warm zero-allocation
  tests unchanged. Record in `artifacts/benchmarks/`; failed evidence kept.
- **Documents (Auto):** ARCH and CODEMAP (§2, C3/C4); DECISIONS (D9); PLAN at landing (§2); STATUS none (no boundary changes; G428's
  known-defect row stays); VERIFICATION none (no command changes); DIAGNOSTICS `:163-174` stay H15's (H0c §6).
- **Sizes:** C1 ≈ −20 src, t ≈ −16 +12 (D11); C2 ≈ −14,735 src (14,446 + ≈ 120 + ≈ 12 (B1's three) + 147 + ≈ 10 Binding/catalogs),
  t ≈ +10 (rows) −5; C3 ≈ ±30 doc; **H10b ≈ −14,770**; H10b-2 (C4) ≈ −1,000 (967 + ≈ 30), t ±5. **Structure limits:** deletions
  only; `ElementAccess.cs` 453 → ≈ 335; no member or file grows; the old Analysis exemption ends at C4.

## 6. Open questions for R5

| # | Question | State |
| --- | --- | --- |
| OQ1 | Stops: (A) wait whole, (B) H10b first with pins, (C) the split | Recommended (C); BU: sound (§2 of the review). R5 decides |
| OQ2 | Q6: `LintErrorRecords` a lasting Binding fact | Answered: yes, with BU's contract (§7) |
| OQ3 | BU's consent for the Binding hunks | Answered: consent to every hunk (§7) |
| OQ4 | D6 at H10b rather than H11 | Recommended yes; BU: sound, extended by B1. R5 decides |
| OQ5 | The fallback if a fix slips | Answered: DefersCoverage moves now (D11); the owner need not move anything (§7) |
| OQ6 | The design review by BU | Done (REVISE, then this revision); BU re-checks |

## 7. Rulings on BU's design review (revision 1, 2026-11-05)

- **B1 → adopted.** C2 also deletes `OwnershipRequirement.ExpansionLimit` with its catalog row (D7) and `ReferenceTypes.IsValue`,
  `FormattingTypes.IsSliceBorrow`, `SharedReadTypes.ReadsStoredPointer` (D6); each verified on `b775ff0f0` with only its declaration
  left. C2's evidence carries the name-based list of declaration-only members (§2).
- **N1 → adopted.** The nine Binding members join H11's list (§4).
- **N2 → adopted.** `InvalidateOwnership` becomes `InvalidateMir` in C1 (declaration and its two callers; the one-token hunk in
  `Binding.cs:108` is BU's, for the re-check).
- **N3 → no replacement assertion.** `c.Accepts` in the loop already implies no published Error, and `Assert.Empty(c.Problems)` keeps
  the comment's "no record" of the MIR (D7).
- **Evidence → adopted.** The semantic snapshot check and `HirColumnsTest`, `HirFormsTest`, `MirCensusTest` in C2's Unit (§5).
- **OQ3 (BU's answer):** consent to every Binding hunk (`Binding.Enums.cs` D5; `Binding.Lints.cs` D4's untag and summaries, C4's
  deletion). Also adopted: `LintErrors`' `[Transitional("R6")]` Reason names the G428 (1) stop and C4 (D4).
- **OQ2 (BU's answer):** `Binding.LintErrorRecords` stays as a lasting Binding fact; its summary says derived records are included,
  tests are its only readers and no compiler path reads it (D4); H10b's DECISIONS entry has one line naming BU's consent (D9).
- **OQ5 (BU's answer):** the owner need not move anything. DefersCoverage moves in C1: for both forms the cause is published and no
  fallthrough or coverage code is, plus form 1's exact set; form 2's exact set goes to G52 as its fail-first regression, the Explicit
  test **`ControlFlowAnalysisTest.AJoinPastAnUnresolvedCallPublishesOnlyItsCause`** (D11), for BU to record in PLAN G52. The wrong
  record is pinned neither way. The stops drop to three (the G428 cases), and H10b-2 waits only for G52's second shape → G428 (D2).

### BU's re-check 1: ACCEPT (2026-11-05)

- BU consents to the one-token `Binding.cs:108` rename (`InvalidateOwnership` → `InvalidateMir`). N3's decline stands.
- For the implementation: the moved DefersCoverage method calls `c.Analyze()` (PublishedErrors asserts that the MIR exists). The
  form-2 Explicit test is recorded in PLAN G52 (dev 7ea655ac4); the ratchet goes 4 → 3.
