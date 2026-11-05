# R6-check H11: the old-Analysis-only Binding members and R4-M108's list (design)

**Revision 1 (2026-11-05):** H11a after BU's design review (REVISE: B-1, N-1–N-7; R5's rulings §12), R4's C-26 answer (§10) and
H10b's additions (§11), re-based on `63ea33d31` (H10b C1–C3 landed). Changes are marked "(B-1)", "(N-1)" … "(N-7)", "(§10)",
"(§11)", "(rebase)". §9–§12 are kept as records. H11b (§6) is unchanged apart from §10's citation.

Status: revision 1, for R5 (the planning session) and BU's design approval (C1: the owner, 2026-11-05, 「C1C3を採用してください」, PLAN
`50c6e1f50`; R5-M190). Unit row (H0 §4, R5-M182): "H11 | Old-Analysis-only Binding members with R4-M108's H11 list (twins, the
fit-failure gate, non-call fit rows, `CallLocalOrigin`; R4 reviews; answer R4-4); the `src/Kimi` zero-reference row | (now R5) | H10b;
R4-12's owner answer | group G:check-del | ≈ −700". Inputs read: H0-design §3-§5, §8 (R4-4, R4-12); H0c §1 (Binding table), §2, D-c4,
D-c7, D-c8; DECISIONS R4-M108, R4-M111, R4-M113 (Site rule), R4-M152, BU-63, BU-90 (X1, X2), R5-M68, R5-M188, R5-M189, R5-M190; H10a
D4; H10b §0 (D6, D8), §4 (N1); the R5/R6 plan's H11 row (`:506`); ARCH `:232-236`; PLAN's open inputs (`BindingFailure.VirtualEffectBound`,
X2).

Base (rebase): `origin/claude/r5-check-del` **`63ea33d31`**. H10b's C1–C3 (`205f69da5`, `a85a799dc`, `8def58bc6`) are on the branch,
so "after H10b" is the real tree. Every count below is a `git grep -n -w` on it (`notes/refs1.sh` beside this file; homonyms read by
eye). H11a's source and test files are byte-identical to `e48326b9c`, except `DiagnosticRequirement.tinyhand`, whose row moved
686 → 683. So revision 0's line numbers hold. Paths: `C/` = `src/Kimi/Compiler/`, `B/` = `C/Binding/`, `T/` =
`tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`. No build, test or probe was run. Working time (Q12): `design-state.md` beside
this file.

## 0. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **Split H11 at R4-M108's twins** (R5's ruling §9 OQ1). **H11a** deletes every member the old analysis alone read, with its closure, and adds the `src/Kimi` zero-reference row; it is behavior-preserving. **H11b** carries out R4-M108's list (the twins, the fit-failure gate, verdict-independent non-call rows) together with `IsTwinRecord` and MirSide's filter; it changes published records. R4's reading is per position (§10). `CallLocalOrigin`, R4-M108's fourth item, is already gone (2c-5, R4-M152: "Deleted: `CallLocalOrigin` and the Closure's failure"); H11 only records it. | R4-M108's items are not dead code. They move Binding's verdict on a node to A5, which changes published records: the twins disappear, the records the Opaque node hid come back, and the gate's `Unsupported_Kd` becomes A5's verdict. That needs a census, a generated gate and pin moves in 21 test files (44 "H11" sites). H11a needs none of that and unblocks H14's counts. Splitting keeps each unit within Q12's hour and gives each one subject to review. | — |
| D2 | **H11a's list** is §1's "delete" rows: the R4-M152 gate (`Binding.ValueCallGate.cs` whole, `CallPlan`'s two facts and byte, the `dependent` computation in `ValueCallResult`), BU-63's `ContainsOrigin(BoundType, …)`, `OriginProofMetrics.Verdict`/`Verdicts`, `PropertyUpdateStorage`, `CollectWitnesses`, H10b N1's nine, `InstantiateDefaultCall` with `InstantiateForwardedCall` and their tables, `ComparisonPlan(CallPlan)` (D-c8), and `BindingFailure.VirtualEffectBound` with its arm and row (BU-90 X2). **(§11)** Also `BoundProperty.InitializerFunction`, `HasReceiverOnPath`'s `owners` parameter with its `true` branch, and `ReferenceTypes.IsScalarBorrow`. **(N-3)** In C2's second order, the private `ComparisonPlan`'s `operators` parameter, constant `false` at its one caller, goes, together with the cache key's `Operators` component and `BoundComparison.Operators` (unread: `git grep '\.Operators\b' -- src tests` returns 0). Also second order: `ProvePrimitiveInteger` (D6). **(N-2)** `TryGetReceiverOperation` stays (D6). | Each has 0 compiler readers on `63ea33d31` (§1 proves it; BU's review verified each name, §"Verified" 1). Every callee keeps another reader. | §1 |
| D3 | **Tests that lose a second producer (H0c: "retarget tests to F5b's twin, then delete")**, and the oracles that remain (N-4, ruling "N (oracles)").<br>**`InstantiateForwardedCall`** has no compiler reader once `InstantiateDefaultCall` goes. Its six test reads compare `TyCtxt.Compose`/`Witness` against it:<br>• `TyCtxtTest.cs:1390` and `:1518` lose one line each.<br>• **(N-4)** `ComposeProblems` (`:1817`) drops the forwarded-Substitution equality. Its declared-Type loop keeps the two-step oracle for **every** nested call: `Apply(Apply(declared, SubstOf(inner)), SubstOf(outer))` against `Apply(declared, Compose(...))`. Today only requirement calls take that arm, so the corpus check widens rather than narrows. `ComposeIsTheForwardedInstantiation` keeps its corpus and direct counts and its direct assertions, and is renamed by subject (`ComposeIsTheTwoStepInstantiation`).<br>• **(N-4)** `WitnessProblems` (`:1986`) drops the `agrees` arms. It keeps the state-unchanged check, the bound-reference check against `InstantiateStorageType` and the per-kind counts. Witness's targets by kind stay observed by `InstanceCollectorTest`'s K 4.5 rows (`:404`, `:920`, `:1020`, `:1051`), TyCtxtTest's direct Witness assertions (`ADeepSelfHasTheLimitTarget` `:1373-1374`, `ARejectedSelfRunsItsWitness` `:1495`, `ContractDeclaresARequirementAndItsBuiltinTarget` `:1532`, the warm test `:1604-1611`) and the native fixtures. `WitnessIsTheForwardedRequirementCall` is renamed `WitnessResolvesEveryRequirementCallOfTheCorpus`.<br>• `VirtualCallSelectionTest.InstantiationClosesBothTheSlotAndDirectImplementation` (`:80-93`) is rewritten over `c.Types.Compose(SubstOf(inner), SubstOf(outer))` if the Subst carries the slot's declaring and implementing Types; otherwise it is deleted with the test that still observes the fact named (§9 OQ3: never weaker).<br>The comments that named the retired comparison are reworded (D7).<br>**(B-1) `ComparisonCompositionTest.TheViewHasTheLegacyPlansLeaves` keeps its agreement check.** Both resolvers stay live: the private `ComparisonPlan(BindingSymbol, …)` is read through `Binding.Witnesses` (`B/Binding.RequirementInstances.cs:24`), then `B/TyCtxt.Effects.cs:50`, then `C/Mir/Analysis/MirEffects.Solve.cs:304` (R5-M68). So `Twin` (`:562-604`) compares, for a closed Self with `TupleOperator` false, the view's Witness leaves (`Target`, `Subst.Declaring`) in walk order with the list `c.Types.Witnesses(requirement, self, contract, null, null, leaves)` appends for the comparison's own requirement (the live A4 path), instead of `c.Binding.ComparisonPlan(call)`. `Same` becomes that leaf comparison. `Answers` is used only for `TupleOperator` calls (A4 reads the view directly since R5-M166; the `operators: true` legacy plan loses its last reader here) and for Selfs with an abstract part. `TyCtxt.Comparison.cs:12-13`'s invariant stays true, observed by the same test. | The legacy side was the reference while two producers existed (F5b, R5-M61). Once it has no compiler reader, keeping it only for tests would be a test-only mechanism. Where both producers stay live (B-1: the comparison leaves, two resolvers until A-12), the agreement check stays, through the live reader. No member is added. Each TyCtxt fact these tests pin stays pinned. | `T/TyCtxtTest.cs`, `T/VirtualCallSelectionTest.cs`, `T/ComparisonCompositionTest.cs` |
| D4 | **`BindingFailure.VirtualEffectBound` goes in H11a** (`BindingModel.cs:231`), with its arm (`Binding.Check.cs:202`) and its row (`DiagnosticRequirement.tinyhand:683-685`, rebase). X2's reason (the ledger row) ended at H10a. The members after it shift ordinal. Requirements are named by enum name (`DiagnosticRequirements.NameOf`) and the `Kind` ordinal is persisted nowhere (BU's review, Verified 2). `DiagnosticCatalogTest` in C2's checks confirms this. `OwnershipRequirement.VirtualEffectBound` (A4's) is a different enum and stays. | X2: "stay to H11", producer gone since H5's F. | `B/BindingModel.cs`, `B/Binding.Check.cs`, `src/Kimi/Diagnostics/DiagnosticRequirement.tinyhand` |
| D5 | **Kept with a live reader:**<br>• `WithoutRawOrigins` (AdtDef's view, `Binding.Storage.cs:71, 81, 96`). Its comment `Binding.RawOrigins.cs:47-49` drops "the old analysis … removed with it at R6-check" and states the lasting view, ending BU-24's D5 exception text.<br>• `RequirementAt` (8 Binding readers).<br>• `RequirementImplementation` and the private `ComparisonPlan` (R7: R4-12 and BU-90 X1; B-1's live path).<br>• `propertyUpdateStorage` (`Binding.Ranges.cs:137`).<br>• `PerCallSignature` and `HasEnvironmentOrigin` (other readers).<br>• `IsBorrowedReceiver` (`Binding.InPlaceReads.cs:107`) and `ReachesThroughBorrow` (`ElementAccess.cs:13, 22`).<br>• The discharge H0c called "A3's gate" (`Binding.TypeOrigins.cs:197-210`): `OriginDischarge.CallBorrows` is read at `Binding.Obligations.cs:67` and `Binding.OriginClauses.cs:155`.<br>H0c's other "A3 gate" (`Binding.OriginFits.cs:107-110`) is R4-M108's fit-failure gate (H11b). | AGENTS: delete only what is dead. Each of these has a compiler reader. | `B/Binding.RawOrigins.cs` |
| D6 | **A public or internal query left with only test readers:** it goes when its tests keep the fact through a published or program-level observation. Otherwise it stays, with a summary naming its reader as the one observation of a SPEC fact.<br>• `ProvePrimitiveInteger` (public, `Binding.Capabilities.cs:28-34`) goes with `IsGenericWrappingInteger`. `T/WrappingIntegerTest.cs:53` goes if the class already rejects a `T is PrimitiveInteger` use at `Wrapping<T>` through a program (SPEC 8.4.7.3); else the member stays (§9 OQ4).<br>• **(N-2)** `TryGetReceiverOperation` (public, `B/Binding.ArgumentOperations.cs:73-78`) lost its compiler reader with H10b (`OwnershipAnalysis.Properties.cs:20`). Its readers are 9 assertions in `T/InheritedReceiverBindingTest.cs`, which read `BasePath`, `ObjectCompatibility`, `Kind`, `Adaptation`, `Source` and the Origins of the selected receiver operation. The HIR publishes only the adaptation (`Binding.Hir.Adjust.cs:115`, `HirAdjust.Of`), and a member access has no other store. So it **stays**, its summary naming that test as its reader (the receiver operation selected for an inherited member: its base path and object compatibility; `BasePath` is postponed to R7 by R5-M190). Visibility is unchanged. | Same rule as H10b B1 and R5's ruling "N (`TryGetReceiverOperation`)". A test-only query stays only as the one observation of a SPEC fact, never as a weaker retarget. | `B/Binding.Capabilities.cs`, `T/WrappingIntegerTest.cs`, `B/Binding.ArgumentOperations.cs` (summary) |
| D7 | **Comments naming the deleted analysis or a deleted member as current** are reworded in H11a:<br>• Binding: `B/Binding.ClosureReceivers.cs:21`, `B/Binding.LocalRegions.cs:245`, `B/Binding.OriginClasses.cs:17`, `B/Binding.RawOrigins.cs:47-49`, `B/Binding.Structs.cs:114` ("the Loans A5 lacks"), `B/BoundClosure.cs:12` and **(N-1)** `:21`, `B/KimiLibrary.DictionaryStorage.cs:23`, `B/Binding.ValueCalls.cs:10, 623` (the gate clause goes), **(§11)** `B/Binding.Diagnostics.Cases.cs:9` ("the numbering of MirCases' views"; `BuildCases` is gone) and `:12` (the old-Analysis sentence goes).<br>• **(N-1)** TyCtxt: `B/TyCtxt.Substitutions.cs:165` (Compose's meaning stated without `InstantiateForwardedCall`) and `B/TyCtxt.Dispatch.cs:23-24`. The invariant against the live materializer `InstantiateRequirementCall` stays; "(TyCtxtTest's twin)" becomes the observations D3 names.<br>• Tests: `T/TyCtxtTest.cs:1302`, `:1331`, **(N-1)** `:1940`; `T/TypeDagScalingTest.Instances.cs:20` (the `ResultFixedForCall` clause goes); **(§11)** `T/TypeDagScalingTest.cs:191` ("AddType's set per Place" becomes the MIR's Loan).<br>• Other: `C/Backend/Codegen/FunctionCodegen.cs:42` (R5's file; "or the old Analysis/Emission" goes); **(§10)** `C/Mir/MirCases.cs:19`, in R4's wording: "Views are numbered with the last binder advancing fastest, each through its admitted set in SemanticsMask order, so view i is its case bit i."<br>Excluded: `B/Binding.Lints.cs` (H10b D4 / C4), `ControlFlowAnalysis.cs` (H10b-2), and historical comparisons such as `T/TypeDagScalingTest.cs:153, 164, 222` ("the old analysis's bound was 64"), which state a past fact. | A comment states the current system. D-c4's row counts comments. | as listed |
| D8 | **The `src/Kimi` zero-reference row (D-c4, second step)** lands in H11a's C1: one `InlineData` beside `T/ArchitectureRulesTest.cs:76-78`, over H10b's constant `DeletedByH10b` (`:40`; rebase): "Compiler references to the old ownership analysis", `src/Kimi`, `*.cs`, limit 0. On `63ea33d31` the constant matches in `src/Kimi` exactly `Binding.RawOrigins.cs:48` and `Binding.ValueCallGate.cs:8, 27`, and H11a removes all three (BU's review, Verified 3). The exact-site ratchets are unaffected (`TypeOrigins.cs=3` lies outside the deleted overload). | A row lands in the commit that makes it true (H10b D8). | `T/ArchitectureRulesTest.cs` |
| D9 | **Commits.** H11a:<br>• **C1 (bp)**: the compiler-only deletions (gate, `ContainsOrigin`, `Verdict`, `PropertyUpdateStorage`, the three Collect members, the four reference helpers, `IsGenericWrappingInteger` with D6, `HasDestructorOnPath`, `TransferCapture`, **(§11)** `InitializerFunction`, `IsScalarBorrow`, `HasReceiverOnPath`'s parameter), D6's summary, D7's comments and D8's row.<br>• **C2 (bp)**: D3's pairs (`InstantiateDefaultCall`/`InstantiateForwardedCall` with the six test reads; `ComparisonPlan(CallPlan)` with `ComparisonCompositionTest`'s B-1 rewrite and N-3's `operators`), D4, and the DECISIONS entry (D10).<br>• **C3 (doc)**: ARCH and DIAGNOSTICS (§4).<br>H11b gets its own commits after its design (§6). | AGENTS "Separate commits". Each code commit is a deletion whose evidence is the grep in §1 plus the build. C1 touches tests only for D6's line, D7's comments and D8's row. | §1 |
| D10 | **DECISIONS: one R5 entry in C2**, numbered at commit time with the next free R5-M (at least **R5-M191**: R5-M190 is the owner's C1/C3; §12). It records:<br>• the split (D1), superseding R5-M182's H11 row in part;<br>• the temporaries ended: R4-M152's receiver-dependence gate, its four `("R6-check")` tags in `Binding.ValueCallGate.cs` and three in `CallPlan.cs`; BU-63's `ContainsOrigin` copy (`("R6-check")`); BU-90 X2's `VirtualEffectBound`; D-c8's `ComparisonPlan(CallPlan)` and the now-constant `operators` (N-3);<br>• D3's retired oracle (R5-M61's forwarding comparison, the plan's "F5b's twin") and B-1's kept agreement (R5-M68's two resolvers);<br>• D6's two decisions;<br>• R4-M108's `CallLocalOrigin` item as done at 2c-5, and R4-M108's other items and R4-M111's `IsTwinRecord` moved to H11b, where R4's per-position reading (§10) supersedes R4-M108's "Rows go by node" sentence and R4-M111;<br>• BU's consent line.<br>**(N-5)** One-line "Ended by R5-M19x (R6-check H11a)" pointers go on BU-63 (its `ContainsOrigin` copy and the "Stay walks" readers `ResultFixedForCall`/`FixedInBody`) and on BU-90 X2, with BU's consent (given in its review). A pointer on R4-M152's Binding half (the gate) is added only with R4's consent; R5 asks R4 with C2's diff. | AGENTS "Record decisions"; R5-M189's precedent. | `docs/dev/DECISIONS.md` |
| D11 | **Class: H11a Major.** It deletes across Binding, root helpers, the catalog and three test classes, and departs from R5-M182's row by the split. BU approves the design (its independent reviewer, C1: revision 0 REVISE, this revision answers it). R4 reviews the implementation (H0's row) and consents to `MirCases.cs:19` (§10). G:check-del's group review follows. **H11b: Owner class**: R4-M108's contract is owner-approved, reviewed and recorded, and R4 fixed the reading (§10). Its own design (§6) still needs probes before implementation. | WORKFLOW classes; AGENTS Roles. | — |

## 1. Inventory on `63ea33d31` (item 1; rebase)

Readers: compiler (`src`) / tests. "Old" = a reader in the files H10b C2 deleted (cited at `205f69da5`). `ControlFlowAnalysis.cs` (to
H10b-2) reads none of these names (grep over the file: 0).

| Member | Site | Readers on `63ea33d31` | Fate |
| --- | --- | --- | --- |
| R4-M152 gate: `receiverDependentCalls`, `receiverDependentPass`, `ReceiverDependentCalls`, `RecordReceiverDependence`, `ResultFixedForCall`, `FixedInBody`, `FixedInBodyOrigin` | `B/Binding.ValueCallGate.cs` (149 lines, 4 `("R6-check")`) | within the file; `ValueCallResult`'s `dependent` (`B/Binding.ValueCalls.cs:634-636`) and its one write (`:655`). Old: `OwnershipAnalysis.cs:176` | **H11a C1**: the file goes, plus `ValueCalls.cs:634-636, 655`. Callees keep other readers: `PerCallSignature` (`Binding.TypeRelations.cs:71`), `HasEnvironmentOrigin` (`Binding.CallSlots.cs:181`), `MentionsOrigin`, `SummaryOf`, `ProveCopy`, `IsWithin`, `IsLocalRegion` |
| `CallPlan.receiverDependence`, `ReceiverDependentResult`, `ReceiverDependentListed` | `B/CallPlan.cs:58-59, 355-372` (3 `("R6-check")`) | the gate file only | **H11a C1** |
| `ContainsOrigin(BoundType, BoundOrigin)` (BU-63) | `B/Binding.TypeOrigins.cs:11-38` (`("R6-check")`) | itself (`:31`); every other call passes `BoundOrigin`s (`ContainerReferences.cs:75, 82`, `OriginDeclarations.Failures.cs:109-110`, `OriginDeclarations.cs:475, 535`) | **H11a C1**; the `BoundOrigin` overload (`:74`) stays |
| `OriginProofMetrics.Verdict()`, `Verdicts` | `B/Binding.OriginProofMetrics.cs:65-66, 91, 108` | none (`MirDump`/`MirDrops`/Layout/Benchmark hits are homonyms) | **H11a C1** |
| `PropertyUpdateStorage` | `B/Binding.PropertyCalls.cs:18-20` | none | **H11a C1**; the table stays (`Binding.Ranges.cs:137`) |
| `CollectWitnesses` | `B/Binding.Specializations.cs:40-78` | none (H0c: "no MIR reader") | **H11a C1**; `PropertyWitnessFunction` stays |
| `CollectFixedArrayWitnesses`, `CollectIntegerPositionWitness` (N1) | `B/Binding.FixedArrays.cs:12-27`, `B/Binding.IntegerPositions.cs:12-20` | none | **H11a C1**; `FixedArrayWitness`, `IntegerPositionWitness` stay |
| `ExclusiveArrayHandle`, `ElementPlaceReference` (N1) | `B/Binding.ArgumentOperations.cs:83-90` | none | **H11a C1**; `PlaceOrigin` stays |
| `PayloadReadReference` (N1) | `B/Binding.ObjectViews.cs:23-24` | none | **H11a C1** |
| `IsGenericWrappingInteger` (N1, public) | `B/Binding.Capabilities.cs:36-43` | none | **H11a C1** |
| `ProvePrimitiveInteger` (public; second order) | `B/Binding.Capabilities.cs:28-34` | `IsGenericWrappingInteger` / `T/WrappingIntegerTest.cs:53` | **H11a C1** per D6 |
| `AdtDef.HasDestructorOnPath` (N1) | `B/AdtDef.cs:108-124` | none | **H11a C1** |
| `BindingSymbol.TransferCapture` (N1) | `B/BindingModel.cs:370-371` | none | **H11a C1** |
| **(§11)** `BoundProperty.InitializerFunction` | `B/Binding.Properties.Model.cs:34` | none (no write either) | **H11a C1** |
| **(§11)** `HasReceiverOnPath`'s `owners` parameter | `C/ElementAccess.cs:31, 307, 313` | one caller, `ReachesThroughBorrow`, passing `false` | **H11a C1**: the parameter and its `IsBorrowedReceiver` arm go (R5's root-helper file); `IsBorrowedReceiver` stays (`Binding.InPlaceReads.cs:107`) |
| **(§11)** `ReferenceTypes.IsScalarBorrow` | `C/ReferenceTypes.cs:61-63` | none (dead before H10b) | **H11a C1**; `ScalarTypes.Supports` stays |
| **(N-2)** `TryGetReceiverOperation` (public) | `B/Binding.ArgumentOperations.cs:73-78` | — / `T/InheritedReceiverBindingTest.cs` ×9 | **stays** (D6); summary names its reader |
| `InstantiateDefaultCall`, `defaultCalls` (N1) | `B/Binding.Specializations.cs:14-29` | none | **H11a C2** |
| `InstantiateForwardedCall`, `forwardedRequirements` | `B/Binding.Specializations.cs:11-13, 80-190` | `InstantiateDefaultCall` (`:22`) / `T/TyCtxtTest.cs:1390, 1518, 1824, 2009`, `T/VirtualCallSelectionTest.cs:87, 91` | **H11a C2** with D3; `ForwardOrigin`, `InstantiateRequirementCall` (R7), `InstantiateStorageType`, `SubstituteLength`, both scratches stay |
| `ComparisonPlan(CallPlan)` (D-c8) | `B/Binding.ComparisonPlans.cs:21-25` (`("R6")`) | — / `T/ComparisonCompositionTest.cs:575` | **H11a C2** with D3 (B-1) |
| **(N-3)** the private `ComparisonPlan`'s `operators`, the key's `Operators`, `BoundComparison.Operators` | `B/Binding.ComparisonPlans.cs:8, 30, 40, 46, 73`; `B/BoundComparison.cs:7`; the call `B/Binding.RequirementInstances.cs:24` | constant `false` after C2; `.Operators` read nowhere | **H11a C2** (`ComparisonKindOf(…, false)` at its one use) |
| `BindingFailure.VirtualEffectBound` (X2) | `B/BindingModel.cs:231`; arm `B/Binding.Check.cs:202`; row `DiagnosticRequirement.tinyhand:683-685` | no producer since H5's F | **H11a C2** (D4) |
| Comments | D7's list | — | **H11a C1** |
| `IsTwinRecord`, `originTwins` (R4-M111) | `B/Binding.OriginFits.cs:27-29, 41-46, 67, 165`; `B/Binding.OriginClauses.cs:263` | the twin sites / MirSide's filter `X/MirTargetSide.cs:51` (summary `:13`, `:39-40`) | **H11b**, with the twins (H10a D4) |
| The twins (R4-M108) | fit: `RecordOriginChains` `B/Binding.OriginFits.cs:134-169` (callers `:121`, `B/Binding.OriginProof.cs:379`); clause: `JudgeLocalClause` `B/Binding.OriginClauses.cs:246-265` | live: Binding's published records | **H11b** (§6) |
| The fit-failure gate | `B/Binding.OriginFits.cs:107-111` | live | **H11b** |
| Verdict-independent non-call rows, `fncomp` | `RecordOriginFitFailure` `:121-124`; `CheckTypeUseCore` `B/Binding.ValueFits.cs:111-114`; Function-component search stop `B/Binding.OriginFits.cs:214-219` (G216 (a) U2.2's) | live; pins `T/OriginRowsTest.Boundary.cs:60-68`, `T/CallableOriginForwardingTest.Conversions.cs:438-450` | **H11b** |
| `CallLocalOrigin` (R4-M108 M2-(i)) | none (deleted at 2c-5; `T/OriginRowsTest.Boundary.cs:99` says "had") | — | done; recorded (D10) |
| Already gone before H11 | `TryResolveFormattingCallback`, `DestructionCall`, `SelectSpecialization(CallPlan)`, `DictionaryComparison` (BU-90 X1); `IsIndexerCall`, `DesignatedType`, `MatchTypes`, `StaticScalar`, `DeferredExpansionLimit_Kd` (H10b D6, D7); `FallthroughKey` (H5) | — | none |
| Not H11's | `Binding.LintErrors` (H10b C4); `LintErrorRecords` (lasting, H10b D4); `JudgeChains`' `("R6-check")` (`B/Binding.OriginClauses.Chains.cs:51`, PLAN G318); `BoundMatch`/`BoundClosure` names (§9 OQ5: H15's exit records); BU's sweep: `PendingBinding`, `Reanalyze`, `RecursiveDefault` (H10b-2's file), `RequireAbsent` (R8's positional catalog) | — | — |

## 2. Behavior (item 2)

- **H11a changes no published record, verdict or generated code.** Every deleted member is unread. The one compile-path change is the
  removed `dependent` computation. Its `ResultFixedForCall` called `ProveCopy`/`SummaryOf`, and the gate's contract says "no record,
  failure or substitution", so constraint interning simply stops at those calls. **(N-6)** These are memoized capability proofs whose
  growth check reads the consumer chain (`Binding.Capabilities.cs` `RequestCapability`), so the order of first queries shifts. That
  order is not a published fact; the snapshots below are the evidence. Binding also allocates less: two dictionaries per Binding
  (`defaultCalls`, `forwardedRequirements`), the gate list, and the `ProveCopy` constraints per value call. The gate's
  `call.CallStorage ??= new()` (`ValueCallGate.cs:68`) created the plan earlier on a cold bind. The plan is still created on every
  success path, at `Binding.ValueCalls.cs:550`, and nothing between those lines reads a plan or catches an exception (BU's review, N-6:
  safe). Evidence (§12 "Ledger"): the semantic snapshot check equal, and in G:check-del's window the Session with natives and
  `-DiagnosticSnapshot` with 0 differences. The mir ledger ended at H10a (R5-M188).
- **The twin filter: MirSide is unchanged by H11a.** `IsTwinRecord` and MirSide's filter stay to H11b. In H11b the twins themselves stop
  being recorded, so removing the filter changes no MirSide row (Binding's issues without twins, then the MIR's). MirSide changes only
  where H11b changes the MIR (below).
- **H11b changes published records (SPEC 23.1.2; DECISIONS Q13):**
  - (a) Binding's twin records (`UnsatisfiedOriginRelation_Kd` / `UnprovenOriginRelation_Kd` under `Binding.OriginRelation`) stop
    publishing beside A5's record of the same relation. R4-M113's Site rule keeps A5's location at the value Binding's record names.
  - (b) The twin's node is no longer a failed (Opaque) MIR value, so independent Loan conflicts formed inside it publish (G56, R4-M122:
    `slot@follow = x@ref; x = 2`). SPEC 23.1.2 requires these records; under Q13 they are improvements.
  - (c) The gate's `Unsupported_Kd` at an unproven bound-ends fit becomes A5's verdict: a relation record, or an acceptance where A5
    proves the relation, which needs a SPEC clause per program.
  - (d) `fncomp` positions gain rows.
  - Under R4's per-position reading (§10), a mixed fit keeps Binding's fixed-fixed record with its code and location. No twin was
    ever suppressed in published output; only MirSide filtered them.

## 3. The zero-reference rows (item 3)

D-c4's `src/Kimi` step is D8: one row over `DeletedByH10b` (the 44 top-level types of the deleted files, `AnalyzeLegacyOwnership`,
`DeferredExpansionLimit_Kd`) in `src/Kimi`, limit 0, landing in H11a C1. It replaces no row: H10b C2 already replaced the four
`LegacyOwnership` rows with the tests/Benchmark/Playground rows (`:76-78`). The control-flow row stays to H10b-2 (C4). The H11
temporaries' names get no row: they are deleted members, and no reference to a deleted type survives a build.

## 4. Commits, consents, decisions, documents (item 4)

| Commit | Content | Consent | Phase (Q12) |
| --- | --- | --- | --- |
| H11a C1 (bp) | §1's C1 rows, D6, D7, D8 | BU: every `B/` hunk; R4: `C/Mir/MirCases.cs:19` (given, §10). `ElementAccess.cs`, `ReferenceTypes.cs` and `FunctionCodegen.cs` are R5's | H11a-i (≈ 35 min) |
| H11a C2 (bp) | §1's C2 rows, D3's test edits (B-1, N-4), N-3, D4, D10's DECISIONS entry with N-5's pointers | BU: `B/` and TyCtxt/Comparison test hunks, the BU-63/BU-90 pointers (given); R4: the R4-M152 pointer, if added | H11a-i or -ii (≈ 35 min) |
| H11a C3 (doc) | DIAGNOSTICS `:360`'s "`Binding.VirtualEffectBound` has no producer and goes at H11". **(N-7)** ARCH `:232-236`: the `VirtualEffectBound` half of `:232-233` goes, and the `IsTwinRecord` half reads "(H11b, with the twins)". At `:234-236` only the gate's half goes ("the old analysis's receiver-dependence gate, its `CallPlan.ReceiverDependentResult` fact, `ReceiverDependentListed` flag and `Binding.ValueCallGate` (R6-check; DECISIONS R4-M152)"); F1's located `Unsupported_Kd` and `CallAtomsOf`'s `Foreign` row stay. The stage ledger's R6 row names H11a's landing and H11b. CODEMAP: no change (`:65` names `Binding.ComparisonPlans`, which stays) | — (documentation of decided facts) | H11a-ii |
| PLAN (landing, Auto) | position; H11b's row | — | landing |

Documents (Auto): ARCH, DIAGNOSTICS `:360` and DECISIONS as above. DIAGNOSTICS `:164`, STATUS `:24`, `:49` and the `spec-cases.json`
rows `:144`, `:146` belong to H11b. VERIFICATION, HOVER and CODEMAP: none. No SPEC or IMPL change.

## 5. Verification and order (item 5)

- **H11a checks (Unit, each code commit):** incremental `Kimi` and `xUnitTest` builds, then the slnx (Benchmark: 0 readers;
  Playground: 0). Classes:
  - `ArchitectureRulesTest`, `DiagnosticCatalogTest` (D4's row);
  - `TyCtxtTest`, `VirtualCallSelectionTest`, `ComparisonCompositionTest` (B-1);
  - `WrappingIntegerTest`, `InheritedReceiverBindingTest` (D6's summary);
  - `ClosureEnvironmentOriginTest`, `ClosureResultOriginTest`, `MirBorrowsTest` (closures, the value-call result path),
    `GenericCaptureTest`, `OriginRelationDiagnosticTest`;
  - the classes of `ElementAccess`'s readers that `ReachesThroughBorrow` serves (`ElementAccess*`/`InPlaceRead*` tests by grep at C1);
  - the four `*ScalingTest` classes, `HirColumnsTest`, `HirFormsTest`, `MirCensusTest`;
  - `SemanticSnapshotTest` with `KIMI_SEMANTIC_SNAPSHOT=check`, and `-Trait Purpose=Allocation`.

  A cold Bind ceiling that falls is tightened in the same commit, never relaxed. Each commit's evidence carries §1's grep and, as
  H10b B1 did, the name-based list of members left with only their declaration.
- **Review:** BU approves this revision (its reviewer re-checks B-1 and the N items). R4 reviews H11a's implementation. G:check-del's
  group review follows. **G:check-del's window** (§12 "Ledger"): Session with natives and milestones, `-DiagnosticSnapshot` with 0
  differences, and the semantic snapshot.
- **Order with H12 and H14:** H11a can start now: H10b C2 is on the branch, and H10b-2 is not needed. H12's code commits touch no
  H11a file (H12 D13). H11a's `ArchitectureRulesTest` row and H12 c1's edit to that class meet, and so do the docs (DECISIONS, ARCH).
  Rebase rule: the unit that lands second on the group branch rebases and re-runs the touched classes. H14 (after H10b and H11)
  rewrites forwarding-property reads in Parsing, root helpers, Mir and the Backend. H11a removes the gate's `CallStorage` reads and
  touches `ElementAccess.cs` and `ReferenceTypes.cs` (root helpers) for deletions only, so H14 counts on H11a's tree. H11b runs
  after H11a.

## 6. H11b: R4-M108's list (contract and what its design needs)

- **Contract (R4-M108, R4-M111, R4-M113; owner-approved; R4's reading, 2026-11-05, board C-26, §10):** at a non-call fit, as at a
  call (`JudgeCallPosition`, `B/Binding.CallRelations.cs:277`), each Origin position forms its row regardless of Binding's verdict.
  - A body chain is a row and never a Binding record.
  - A fixed-fixed position stays Binding's judgment and record (until R7, R4-M108 D8). A5 forms no row for it.
  - So the twin set, `IsTwinRecord`, MirSide's filter and the gate (`OriginFits.cs:107-111`) disappear.
  - A local clause whose failed relation is a body chain is a row only (`OriginClauses.cs:259-265`).
  - Rows sit at the value Binding's record named (R4-M113).
  - The pending-relation path (`OriginProof.cs:361-379`) follows the same rule.
  - `fncomp`: positions after a differing Function component get rows (`ValueFits.cs:111-114`; `OriginFits.cs:214-219`, staged for
    G216 (a) U2.2).
  - H11b's DECISIONS entry supersedes R4-M108's "Rows go by node…" sentence and R4-M111 in part (R4-M111 ends whole with
    `IsTwinRecord`).
- **Proposed shape (for H11b's design, H):** one per-position judge for call and non-call fits. It takes JudgeCallPosition's
  body-chain/fixed split and is reached from `RecordMismatch`'s Origin part and from `CheckTypeUse` at every structurally matching
  non-call fit. It replaces `RecordOriginFitFailure`'s search-then-record, `RecordOriginChains`' rows branch and the twin set (≈ −80
  src net, H).
- **Before H11b's implementation (probes; R5 runs the census, §9 OQ6):**
  1. A census of twin nodes over the corpus, the milestones and the 21 pinned test files (an `IsTwinRecord` dump): A5's record at
     each, with the same code and Site.
  2. The gate's `Unsupported_Kd` sites and A5's verdict there.
  3. The records the Opaque limit hid (G56, R4-M122).
  4. The `fncomp` rows.
  5. The generated gate (dev vs head), and the pinned timing of rows at every non-call fit (A5's input grows), measured from one folder.

  §10's conditions apply. Each moved record is classified under Q13 with WORKFLOW's blocking rule, and an acceptance change needs its
  SPEC clause. R4 reviews the implementation.
- **Size (H):** ≈ −80 src, ±200 t (44 pin sites move once; the two "UntilH11" tests flip and are renamed).

## 7. Sizes and structure limits (item 6)

| Unit | src | tests | docs |
| --- | --- | --- | --- |
| H11a C1 | ≈ −332: gate file 149, `CallPlan` 20, `ValueCalls` 5, `ContainsOrigin` 29, Collect members 65 (`CollectWitnesses` 40), helpers 11, wrapping 15, `HasDestructorOnPath` 18, `TransferCapture` 3, metrics 6, `PropertyUpdateStorage` 4; **(§11)** `InitializerFunction` 2, `IsScalarBorrow` 4, `HasReceiverOnPath` ±1; comments ±16; D6's summary ±2 | +1 (row), ±4 | — |
| H11a C2 | ≈ −140: `Specializations` 130 (`InstantiateDefaultCall` 16, `InstantiateForwardedCall` 114), `ComparisonPlan` 6, `VirtualEffectBound` 5; **(N-3)** `operators` ±0 (in-line edits) | ≈ −50 (TyCtxt ≈ −30, VirtualCallSelection ≈ −20; **(B-1)** ComparisonComposition ±10, no longer −10) | DECISIONS +14 (entry and pointers) |
| H11a C3 | — | — | ≈ ±10 |
| **H11a** | **≈ −472** | **≈ −50** | +24 |
| H11b (H) | ≈ −80 | ±200 | DIAGNOSTICS, STATUS ±10 |

Recount of H0's ≈ −700: **≈ −552 src** (H11a ≈ −472, H11b ≈ −80). The gap has four parts:
- H10b D6/D7 took `MatchTypes` (88), `StaticScalar` (59), `IsIndexerCall`/`DesignatedType` and `DeferredExpansionLimit_Kd`.
- BU-90 X1 took the formatting and destruction helpers.
- 2c-5 took `CallLocalOrigin`.
- The gate file, H10b N1's nine, `InstantiateForwardedCall` and §11's three are added.

Structure limits: deletions only. `Binding.Specializations.cs` shrinks from 583 lines to ≈ 413, and `Binding.ValueCallGate.cs` is
removed. No file, member, limit or ratchet grows, and no test enumerates `Transitional` tags (BU's review, Verified 6).

## 8. Open questions for R5

| # | Question | State |
| --- | --- | --- |
| OQ1 | The split and H11b's per-position reading | **Answered:** split (§9); per position (R4, §10) |
| OQ2 | BU's approval and consent; R4's consent for `MirCases.cs:19` | R4 consented (§10). BU: revision 0 REVISE (§12); this revision goes to BU's reviewer for a re-check. **Blocking for H11a's implementation** |
| OQ3 | `VirtualCallSelectionTest.InstantiationClosesBothTheSlotAndDirectImplementation` | Ruled (§9): the implementer decides at C2, never weaker |
| OQ4 | `ProvePrimitiveInteger`; **(N-2)** `TryGetReceiverOperation` | D6's rule. `TryGetReceiverOperation` stays (decided here). `ProvePrimitiveInteger` is decided at C1 |
| OQ5 | `BoundMatch`/`BoundClosure` (R2b-D5) | Ruled (§9): H15's exit records |
| OQ6 | H11b's census and its order with G216 (a) U2.2 | Ruled (§9): R5 runs the census; the order is decided in H11b's design |
| OQ7 | **(N-5)** The "Ended by" pointer on R4-M152's Binding half | Non-blocking: added only with R4's consent at C2 |

## 9. R5's rulings (2026-11-05)

| # | Ruling |
| --- | --- |
| OQ1 | **Split accepted.** H11a (behavior-preserving deletions and the `src/Kimi` zero-reference row) goes ahead after H10b; H11b (R4-M108's twins, the fit-failure gate, non-call fit rows, `IsTwinRecord` and MirSide's filter) gets its own design with probes first, since it changes published records. The per-position reading of "as JudgeCallPosition's" is R4's to confirm (R4-M108 is R4's decision): R5 asks R4 on the board; H11b's design follows R4's reading. |
| OQ2 | R5 sends H11a's design to BU for approval (BU's independent reviewer, C1) and consent on every `B/` hunk, and asks R4's consent for `MirCases.cs:19`. |
| OQ3 | The implementer decides at C2 by reading the Subst; a rewrite keeps every direct TyCtxt assertion (never weaker); a deletion names the counterpart that still observes the fact. |
| OQ4 | D6's rule decides at C1 as written. |
| OQ5 | Not H11's: R5 adds `BoundMatch`/`BoundClosure`'s R2b-D5 deferral to H15's exit records (a deferral to R7 needs the owner's approval there). |
| OQ6 | H11b's census: R5 runs it, with BU's dump if needed; whether H11b waits for G216 (a) U2.2 is decided in H11b's design with BU and G. |

## 10. R4's answer, board C-26 (2026-11-05)

- (1) YES to `MirCases.cs:19`, reworded to the order itself: "Views are numbered with the last binder advancing fastest, each through its
  admitted set in SemanticsMask order, so view i is its case bit i." No other `Mir/**` hunk is needed.
- (2) **R4's reading of R4-M108's "as JudgeCallPosition's" is per position** (cite as "R4's reading, 2026-11-05, board C-26"): a mixed
  non-call fit keeps Binding's fixed-fixed record and gets A5's body-chain row; no record is a twin; R4-M108's "rows go by node" and
  R4-M111's node-level twin set were the interim shape dated "until H11". H11b's DECISIONS entry supersedes R4-M108's "Rows go by node,
  not by record…" sentence and R4-M111 in part (R4-M111 ends whole with `IsTwinRecord`), with "Superseded in part" lines on both.
  Conditions for H11b's census: Binding's fixed-fixed record keeps its code and location at a mixed fit; A5 forms no row for a
  fixed-fixed position; each body-chain row's Site follows R4-M113; each moved record is classified under Q13. H11b's implementation
  review: R4.

## 11. Additions from H10b's implementation (2026-11-05; H11a's list, the implementer re-proves each by grep)

`BoundProperty.InitializerFunction` (unread after H10b); `HasReceiverOnPath`'s `owners: true` branch (no caller); `IsScalarBorrow`
(dead before H10b); stale comments `Binding.Diagnostics.Cases.cs:9, 12`, `TypeDagScalingTest.cs:191` (with D7's
`Binding.LocalRegions.cs:245` and `MirCases.cs:19`, R4's wording in §10).

## 12. R5's rulings on BU's design review of H11a (2026-11-05; `C:\Users\bwff1\AppData\Local\Temp\claude\bu\batch\H11a\design-review.md`, REVISE)

| Finding | Ruling |
| --- | --- |
| B-1 | Accepted as BU proposes: D3 keeps `ComparisonCompositionTest`'s closed-Self `Same` check (`:562-604`) by comparing the view's leaves with `Binding.Witnesses` (the live path: RequirementInstances.cs:24 → TyCtxt.Effects.cs:50 → MirEffects.Solve.cs:304; R5-M68, `TyCtxt.Comparison.cs:12`) when `TupleOperator` is false; `Answers` only for tuple operators and abstract Selfs. Never weaker than today. |
| N (comments) | Add to D7: `TyCtxt.Substitutions.cs:165`, `TyCtxt.Dispatch.cs:23-24`, `TyCtxtTest.cs:1940`, `BoundClosure.cs:21`. |
| N (`TryGetReceiverOperation`) | Decided under D6's rule (test-only reader: delete with its test line, or keep as the one observation of a SPEC fact with a summary naming it). |
| N (`operators`) | The private `ComparisonPlan`'s `operators` parameter, always false after C2, goes in C2 (remove what the change makes redundant). |
| N (oracles) | D3 names each remaining oracle. |
| DECISIONS | "Ended by" lines on BU-63 (its `ContainsOrigin` copy) and BU-90's X2; the entry takes the next free R5-M at commit time (at least R5-M191; R5-M190 is the owner's C1/C3); ARCH's rows are now `:232-236`, keeping F1's half. |
| Ledger | Not applicable: H10a removed the mir ledger (R5-M188); G:check-del's window evidence is the Session with natives, the diagnostic snapshot (0) and the semantic snapshot. |

## 13. C-25 answered (BU, 2026-11-05): design approved, Binding consent given (BU's independent re-check ACCEPT)

Conditions the implementation keeps: (1) D3's kept closed-Self check also asserts each `Witnesses` call returns true (the old
`plan is not null` arm), so two empty leaf lists cannot pass; (2) D6's reason names `BaseReferenceKoto.BasePath` (R5-M190's), not
`BoundArgumentOperation.BasePath`; (3) `BoundComparison`'s summary is reworded after `Operators` goes. `TryGetReceiverOperation` stays as
a lasting test-only query, untagged, with its summary. The ledger point is withdrawn. R4-M152's "Ended by" line (OQ7) only if R4's C-28
consent is on the board at C2; otherwise omit it and report.
