# R6-check H14: the forwarding-property reads outside Binding (design)

**Revision 1** (2026-11-05): the independent design review (`design-review.md`, REVISE) applied under R5's rulings (§9) and §8; changes
are marked "(B1)", "(N3)" and so on; revision 0 is `H14-design.rev0.md` beside this file.

Status: revision 1, for R5's re-check. Unit row (H0 §4, R5-M182): "H14 | Forwarding-property reads (Parsing, root helpers, Mir,
Backend); 37 retags to R7 (R5-M9) | R5 (BU reviews) | H10b, H11 | group G:check-del | ≈ −150". Inputs: H0c §4.4, D-c6, §8 row 3;
DECISIONS R5-M9 (`DECISIONS.md:9391`), R5-M10, CR-A7 (`:494`), R5-M182 (`:14078`); ARCH `:26`/`:36` (Syntax must not read Binding),
`:28` (Mir/Build must not read semantic slots on Koto), `:30` (Backend must not read syntax), `:35`, `:107`, `:108`, `:163`, `:168`,
`:189`, `:201`, `:205`, `:254-257` (N2); H10b-design rev. 1 (D5, D6); R7 plan RP4, T2, Y4, X1 and its session table (`:187`, `:243`,
`:250`); PLAN G431 (N8).

Base (revision 1): `claude/r5-check-del` `e48326b9c` (dev merged at `aadc14bc8`), with H10b's C2 deletions applied on paper; its lines
are dev's (`MirBuilder.cs:119`, `:141`, `:237` now). `C/` = `src/Kimi/Compiler/`, `P/` = `C/Parsing/Koto/`. Counts are `git grep`
occurrences, each site read by eye (homonyms dropped: `PlanRow.Mode`/`.Formatting`, `HirAdjust.Adaptation`, loan `Mode`,
`MirBuilder.Iteration(…)`, `Needs.Formatting`, `LegacyWriter.Formatting`, `BindingResult.Mode`, static `BoundType.X`), and re-counted
with D7's regex (§1). No build, test or probe was run. Working time (Q12): `design-state.md` beside this file.

## 0. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **Scope = reads**, by area: Mir 8, Backend 2, root helpers 23 (+1 dead overload), Parsing 4 (BasePath included, B2), Documentation 3, Testing 1, Benchmark 1 (§1; §8 OQ4). Out of H14's count: what H10b deletes (13 reads, §1.6), Binding's reads (R5-M9), the old Analysis (H10b/H10b-2), and the **write paths** of the properties (setter calls and the two getter-to-mutate reads in invalidations and synthetic views, §1.7; §8 OQ1). | R5-M9 moves "the properties themselves" and Binding's reads to R7 and names "Parsing and root-helper reads" for H14; D-c6 adds Mir and the Backend, and its rule (every read outside Binding ends at R6-check) the other three areas. A setter call is the property's own write path: the synthetic Kotos are R7 by name (R5-M9, R7-P11), the syntax-edit invalidation is R7's inline writes (R2b-D2). | — |
| D2 | **Mir reads a published view.** `FunctionKoto.ClosureDefinition` (7 sites) → **`BoundClosure.Of(FunctionKoto literal)`**, the literal route beside the Type route `BoundClosure.Of(BoundType?)`: `HirTables.PlanOf(literal).Plan is BoundClosure { Signature: not null } plan ? plan : null`. The forwarding property becomes `=> BoundClosure.Of(this)` and the Type route calls the literal route, so the `Signature` rule has one copy. `Formatting.Acquisition!.BoundCall!` (1) → `CallPlan.Of(acquisition)!`. **(N5)** The three test reads of the slot take the same route (`ClosureReceiverClassificationTest.cs:457`, `HirColumnsTest.cs:195`, `MirTypesTest.cs:213`), so D7's extended name list leaves the test row at 13. | Same meaning: the literal route is `FunctionKoto.cs:251` + `:261` moved to the owner of the closure definition (BoundClosure's "Access" contract already names "R5-M63 (1)'s HIR publisher and MIR builder" as literal readers; unbound: `PlanOf` → `NoPlan` → null); the 20 existing `Of` callers pass BoundType-typed arguments, so the overload is unambiguous. `CallPlan.Of` on an `InvocationKoto` is `Resolved ? CallStorage : null` (`CallPlan.cs:154-181`); after a finished pass `HasCurrentBinding` ≡ `Resolved` (`HirTables.cs:63-64`), and the writer acquisition is a named call (`Binding.Interpolation.cs:155`, never `CalleeKind.Value`), so it equals `BoundCall`. ARCH `:28` (N2): Mir/Build reads Plans, never Koto slots; the new route's reason goes in the commit (ARCH `:35`). | `C/Binding/BoundClosure.cs`, `P/Declarations/FunctionKoto.cs:251`, 6 `C/Mir/Build` files, 3 test files |
| D3 | **The Backend reads a declaration-table view.** `function.Scope.Owner.BoundSymbol` (2) → **`VTableDef.Owner(BindingSymbol function)`**, the struct that declares a function (F 4.2's consumer rule); `VTableDef.Slot` and `SlotOf` (`VTableDef.cs:46`, `:60`) use it too. **(N4)** It is VTableDef's dispatch rule only, not the general "owner of a symbol's scope": Binding's 43 `Scope.Owner.BoundSymbol` lines stay Binding's reads, which R7-Y4 routes (R5-M9); D9 says so. | ARCH `:30` (N2): the Backend reads declaration tables, never syntax (`BindingScope.Owner` is a Koto). VTableDef already computes the same expression twice; one owner, three readers. | `C/Binding/VTableDef.cs`, `C/Backend/Instances/InstanceCollector.Objects.cs` |
| D4 | **Column reads: root helpers and the three other areas** (H0c §4.4, approved through R5-M182). `ElementAccess`'s 23 reads that survive H10b: **(N1)** `x.BoundType` (17) → **`HirTables.TypeOf(x)`**, a new typed read beside `Meaning`/`Symbol` (`Meaning(node) as BoundType`); `x.BoundSymbol`/`{ BoundSymbol…: }` (5: `:46`, `:168`, `:346`, `:409`, `:411`) → `HirTables.Symbol(x)` (patterns become `is T t && HirTables.Symbol(t) is {…}`); `ConversionBinding` (1, `FollowedReference` `:256`) → **(N3)** `HirTables.PlanOf(x).Conversion`, a typed read `PlanRow.Conversion => (ConversionBinding)this.Mode` that replaces the three existing decodes (`BinaryKoto.cs:235`'s getter, `SemanticQuery.cs:52`, `MirBuilder.Operators.cs:150` `row.Conversion`), four spellings in all. **(N3)** `HirTables.TypeOf` replaces every spelled-out typed read in the same commit: src `Koto.cs:472`, `SemanticQuery.cs:27`, `MirBuilder.cs:73` (keeps `?? BoundType.Unit`), `MirBuilder.Places.cs:192`, `:273`, `MirBuilder.Values.cs:51` (`is BoundType item` → `HirTables.TypeOf(node) is { } item`); tests `BlockClosureInferenceTest.cs:105`, `HirColumnsTest.cs:319`, `MirBuildTest.Values.cs:216`, `NeverContinuationTest.cs:139`, `:219`, `:269`, `:430`, `OriginRowsTest.Classes.cs:175`, `TypeIdentityTest.cs:119` (`(BoundType)HirTables.Meaning(x)!` → `HirTables.TypeOf(x)!`); `HirFormsTest.cs:745` compares the untyped slot and stays. `ScalarTypes.TryFloatLiteral(Koto, out long)` (`ScalarTypes.cs:57-62`) has no reader after H10b (its one, `OwnershipAnalysis.Values.cs:17`, goes): deleted. **(N6)** `DocumentationMarkdown.cs:42`, `:78`, `:132` and `TestCatalog.cs:252` → `HirTables.Symbol(x)`, `DiagnosticMeasurements.cs:244` → `HirTables.State(x)`: one route, unbound-safe, no `SemanticQuery` created, the benchmark's evidence unmoved. | The column reads are the properties' bodies (`Koto.cs:472`, `:488`, `:464`, `BinaryKoto.cs:235`), so the meaning is identical by construction. ElementAccess stays a root helper until R7-T2 moves it into `TyCtxt`/`AdtDef`; moving it into Binding now would postpone root-helper reads to R7, which needs the owner (§7). **(N9)** No chain grows: `IsSyntax` becomes `source is MemberAccessKoto member && HirTables.Symbol(member) is { Property.IsStored: true } && AdtDef.IsStruct(HirTables.TypeOf(member.Left))` (3 operands; `IsStruct` takes `BoundType?`, so the dropped `{ } type` binding changes nothing); `:20-21` (5-operand `\|\|`, over 160 units) and `:299-300` (6-operand `&&`) are existing excess whose replacements substitute inside operands, with line splits for length. | `C/ElementAccess.cs`, `C/ScalarTypes.cs`, `C/Binding/HirTables.cs` (`TypeOf`, `PlanRow.Conversion`), `C/Binding/SemanticQuery.cs`, `P/Koto.cs`, `P/Expressions/BinaryKoto.cs`, `C/Mir/Build/MirBuilder{,.Places,.Values,.Operators}.cs`, `C/Documentation/DocumentationMarkdown.cs`, `src/Kimi/Testing/TestCatalog.cs`, `src/Benchmark/Benchmarks/DiagnosticMeasurements.cs`, 6 test files |
| D5 | **Parsing's reads leave Parsing with the rule they serve, into Binding** (departs from H0c §4.4's "rewritten to HirTables" for Parsing). (a) Value Context (SPEC 14.2.2, 14.5.2, 14.7): `KotoHelper.IsValueContext`, `DiscardsFunctionBody`, `IsResultRequiringSelection`, `TransferTarget` and the private `IsUnitType` move from `P/Common/ControlFlowContext.cs` to a new `C/Binding/Binding.ValueContext.cs` (static members of `Binding`, with the contract header); **(B1)** `ResolveTransferTarget`, `IsSelectionBody`, `IsBodyExpression` and `IsParameterDefault` stay (syntax; `IsParameterDefault` reads only `DefaultValue` and `ResolveTransferTarget` `:28` calls it), `IsParameterDefault` becoming `internal` for the moved `IsValueContext`. Callers: 20 calls in 12 Binding files; **(N1)** 20 test references in 5 files (19 calls and the method group `ControlFlowAnalysisTest.cs:177`). **(N9)** The comments naming the moved members follow them (`Binding.BareAcquisition.cs:11`, `Binding.ControlFlow.cs:100`, `Binding.ResultSurvey.cs:52`, `StructuralCompletion.cs:74`, `HirFormsTest.Errors.cs:149`), and `ControlFlowContext.cs:7`'s summary drops the moved scope ("syntactic transfer targets and contexts"); §8 OQ7: R5's implementer renames `Binding.Diagnostics.cs:142`'s repair-text `TransferTarget(Koto)` (BU consents). (b) Argument mapping: `FunctionKoto.AllowsPositionalArgument` and `TryMapArgument` (`FunctionKoto.cs:94-95`, `:131-163`) move to `C/Binding/Binding.ArgumentMapping.cs` as statics over the `FunctionKoto` (callers `Binding.ArgumentMapping.cs:34`, `Binding.ParameterShapes.cs:266, 305, 318`); `FindParameter` and `NameBoundaryIndex` stay. **(N9)** `TryMapArgument` reaches 6 parameters (the function first; 1 `out`, 1 `bool` with `ref bool named`), the limit, and gains none. (c) **(B2)** `BaseReferenceKoto.BasePath` (`BaseReferenceKoto.cs:20-24`) becomes a plain accessor over its field (`get => this.basePath;`, the explicit form it has today, not an auto-property); its gate moves to its one reader, `Binding.BaseCalls.cs:82`: `… \|\| !reference.HasCurrentBinding \|\| reference.BasePath is not { } path \|\| …` (4 operands, the limit; a Binding read R5-M9 leaves to R7). Writers `:14`, `:49` unchanged. | ARCH `:26`, `:36`: Syntax must not read Binding, and a direct `HirTables` read from Parsing is still one, so H0c's wording cannot hold there. Each rule decides by Binding facts (a resolved Unit result or Unit alias, PLAN G431's anonymous result included; `BindingSymbol.ReceiverIndex`; the current pass's base path) and every reader is Binding's (the old Analysis' go at H10b; `ControlFlowAnalysis.cs` reads only `ResolveTransferTarget` `:753` and `ElementAccess.UpdateOperator` `:530`). After the move the reads are Binding's (R5-M9: R7). A responsibility moved between phases: DECISIONS (D9). For (c), equal by construction: the reader sees null exactly when the old getter returned null. | `P/Common/ControlFlowContext.cs`, `P/Declarations/FunctionKoto.cs`, `P/Expressions/Names/BaseReferenceKoto.cs`, `C/Binding/Binding.ValueContext.cs` (new), `Binding.ArgumentMapping.cs`, `Binding.ParameterShapes.cs`, `Binding.BaseCalls.cs`, `Binding.Diagnostics.cs`, the 12 caller files, 5 test files |
| D6 | **The 37 retags.** Every `[Transitional("R6", …)]` in `P/` (§1.1) becomes `[Transitional("R7", Reason = "…; R7 removes syntax-owned semantic access (DECISIONS R5-M9).")]`, keeping each Reason's first clause; the commit cites R5-M9 as the owner's approval. `FoldedConstant` is retagged with the rest (§9 OQ5: it is the write path of a conversion's `PlanRow.Folded`, `BinaryKoto.cs:263-267`, written only at `Binding.NumericConversions.cs:18`). **(B2)** A 38th tag, `BaseReferenceKoto.BasePath` (a semantic slot on syntax CR-A7 should have marked): `[Transitional("R7", Reason = "Base-path slot on syntax; R7 moves Binding's facts off syntax (DECISIONS R5-M9, H14's entry).")]`, **only with the owner's approval of this 38th member of R5-M9's postponement** (R5 asks; C1–C3 do not wait). Without it C4 stops at the 37 and R5 reports. | ARCH `:201`: a later removal stage needs owner approval in the commit; R5-M9 is it for the 37, and the owner's answer for the 38th. H15 row 3 then finds no R6 tag in `P/`, and the 38th is identified (ARCH `:254`). | 8 files of §1.1; `BaseReferenceKoto.cs` |
| D7 | **Zero-reference rows** in `ArchitectureRulesTest`, **(N5)** over one name list: `SemanticSlots` (`:32`) gains the 7 of the 37 it lacks (`ClosureDefinition`, `ClosureAsCreated`, `ArithmeticStorage`, `ComparisonStorage`, `ComparisonActive`, `CreationStorage`, `AdaptationStorage`), split over lines (`:32` is already over 160 units), and one constant `SemanticSlotReads = (?<!Compiler)\.(?:SemanticSlots)\b\|[{,]\s*(?:SemanticSlots)(?:\.\w+)*\s*:` serves the new rows. The four names with live homonyms stay out, as `SemanticSlots` already leaves them (`Formatting`, `Adaptation`, `Mode`, `Iteration`), and so does **`BasePath`** (exact reason: `CallPlan.BasePath`, `BoundFunctionWitness.BasePath`, `BoundArgumentOperation.BasePath` and the property witnesses' share the name, 15 test reads and Binding's, so a name row would count them; its tag (D6) and the `P/` site row identify it). Rows: `C/Mir` and `C/Backend` (C1), **(N5)** each root-helper file by its file pattern under `C/` (`ComparisonTypes.cs`, `DefaultParameters.cs`, `ElementAccess.cs`, `FormattingTypes.cs`, `ReferenceTypes.cs`, `ScalarTypes.cs`, `SharedReadTypes.cs`, `SlotTypes.cs`, `StrictBestCandidate.cs`; `SourceFiles` recurses, so a root row names its file) and `C/Documentation`, `src/Kimi/Testing`, `src/Benchmark` (C2), all limit 0; `P/` as a `SourcePatternStaysAtItsSites` row listing the declaration files and the R7 write paths of §1.7 with the counts measured at C3 (C3). The test row keeps its own regex and limit 13 (D2 moves the three reads the extension would add). | H0c §4.4 ("each area with a zero-reference row"); a row lands in the commit that makes it true (H10b D8). Re-counted on the base with `SemanticSlotReads`: Mir 8, Backend 2, Documentation 3, Testing 1, Benchmark 1, ElementAccess 33 (23 after H10b; `Adaptation`'s two reads are H10b's), ScalarTypes 1, StaticScalar 1 (H10b), the other root files, Playground, Checking, Lsp and Core 0. The `(?:\.\w+)*\s*:` form keeps `new(…, BoundType.ISize)` out. | `tests/xUnitTest/Tests/ArchitectureRulesTest.cs` |
| D8 | **Start: after H10b (C2), not H11; not H10b-2.** H14 shares no member with H11 (H10b D6 took `IsIndexerCall`, `DesignatedType`, `StaticScalar`, `MatchTypes` from H11; D-c4's `src/Kimi` row is about the old ownership names). Implement on `claude/r5-check-del` once H10b's C2 is there; **(N8)** dev is merged (`aadc14bc8`), so D5's files are dev's (`TransferTarget`); H11 and BU's G431 (`DiscardsFunctionBody`, PLAN `:294`) rebase over H14 or H14 over them, whichever lands second (§3); R5 tells BU when C3 starts. | Waiting for H11 holds a unit that needs nothing from it; departs from R5-M182's order (D9 amends it). | — |
| D9 | **One DECISIONS entry** (the next free R5-M at C4): D1's read/write line; D2's literal route and D3's `VTableDef.Owner` (published views; **(N4)** the dispatch rule only, Binding's 43 sites R7-Y4's); D4's `HirTables.TypeOf` and `PlanRow.Conversion`, each replacing every spelling; D5's three moves Syntax → Binding (responsibility moved between phases, reason ARCH `:36`; supersedes H0c §4.4's "rewritten to HirTables" for Parsing); D6 citing R5-M9, and **(B2)** the 38th member with the owner's verbatim answer (or its absence); D7's extended list and BasePath's exclusion. **(N10)** An amendment line in R5-M182 ("Amended (R6-check H14, …)"): H14 starts after H10b's C2, not after H11 (D8), and its size (§5). | AGENTS "Record decisions"; R5-M182's earlier departures were recorded as its amendment lines (`DECISIONS.md:14100` ff.). | `docs/dev/DECISIONS.md` |
| D10 | **Class Major, with a design review** (WORKFLOW Design 4: departs from H0c §4.4 and R5-M182's order; the sites span Parsing, Binding, root helpers, Mir and the Backend). **(N10)** Not Full: WORKFLOW's Full is "a change to a shared representation or across phases", i.e. one that changes what a phase decides or publishes; H14 moves two Binding-only rules' code from Syntax's files into Binding's with every reader, result and record unchanged, adds read views only, and changes no representation, contract, IR or vocabulary; every commit is behavior-preserving. Reviewers per §8 D10: the design review by an independent reviewer R5 dispatched (done: `design-review.md`); the implementation review by R4 or an R5-dispatched reviewer; R4 consents to the `Mir/**`, `HirTables` and `Documentation` hunks; BU consents to D5 and to the Binding, Parsing and root-helper hunks after the review. | WORKFLOW Design 4; AGENTS Roles; §8 D10. | — |

## 1. Inventory

### 1.1 The 37 declarations (all `[Transitional("R6")]` today; fate D6)

| File | Members |
| --- | --- |
| `P/Koto.cs:461-524` (10) | `BindingState`, `BoundType`, `BoundOrigin`, `BoundSymbol`, `HasCurrentBinding`, `ErasedFunctionType`, `FormattingStorage`, `Formatting`, `BindingFailure`, `BoundMeaning` |
| `P/Expressions/BinaryKoto.cs:102-311` (14) | `ArithmeticStorage`, `ArithmeticCall`, `ComparisonStorage`, `ComparisonActive`, `ComparisonCall`, `IsDirectStorage`, `ConversionBinding`, `CreationStorage`, `CreationCall`, `AdaptationStorage`, `Adaptation`, `FoldedConstant`, `BoundConstraint`, `BoundRuntimeTest` |
| `P/Declarations/FunctionKoto.cs:250-262` (3) | `ClosureDefinition` (body → D2), `ClosureAsCreated`, `ClosureStorage` |
| `P/Expressions/ControlFlow/ForKoto.cs:32-52` (3) | `Mode`, `EntryCall`, `Iteration` |
| `P/Expressions/InvocationKoto.cs:19-37` (3) | `BoundCall`, `BoundValueCall`, `CallStorage` |
| `P/Expressions/UnaryKoto.cs:65-73` (2) | `ArithmeticStorage`, `ArithmeticCall` |
| `P/Expressions/CollectionLiteralKoto.cs:20` (1) | `FillCount` |
| `P/Expressions/ControlFlow/TryKoto.cs:46` (1) | `SemanticsIndexed` |
| **(B2)** `P/Expressions/Names/BaseReferenceKoto.cs:20` (the 38th, owner's approval) | `BasePath` |

Their reads of each other inside these bodies (e.g. `ClosureAsCreated` → `BindingState`, `CreationCall` → `ConversionBinding`) stay
with them. After H14 and H10b-2 every one is read only by Binding, tests (the 13-limit test row) and its sibling declarations;
`FoldedConstant` is written by Binding and read by none (its column is read directly).

### 1.2 Mir (8; fate D2; R4 consents)

| Site | Read | Replacement |
| --- | --- | --- |
| `Mir/Build/MirBuilder.cs:119` | `decl.Function.ClosureDefinition is null` | `BoundClosure.Of(decl.Function) is null` |
| `MirBuilder.cs:141` | `function.ClosureDefinition is not null` | `BoundClosure.Of(function) is not null` |
| `MirBuilder.cs:237` | `((FunctionKoto)declaration).ClosureDefinition is { EnvironmentType: … }` | `BoundClosure.Of((FunctionKoto)declaration) is { … }` |
| `Mir/Build/MirBuilder.Defaults.cs:183` | `node is not FunctionKoto { ClosureDefinition: not null } function` | `node is not FunctionKoto function \|\| BoundClosure.Of(function) is null` |
| `Mir/Build/MirBuilder.Opaque.Roots.cs:133` | `function.ClosureDefinition?.Captures` | `BoundClosure.Of(function)?.Captures` |
| `Mir/Build/MirBuilder.Places.cs:132` | `((FunctionKoto)this.declaration).ClosureDefinition?.Captures` | same route |
| `Mir/Build/MirBuilder.Values.cs:273` | `closure.ClosureDefinition!.Captures` | same route |
| `Mir/Build/MirBuilder.Formatting.cs:52` | `…Formatting!.Acquisition!.BoundCall!` | `CallPlan.Of(…Formatting!.Acquisition!)!` |

`Mir/Analysis` and the rest of `Mir/`: no read (homonyms only). D4's `TypeOf`/`Conversion` spellings in `Mir/Build` are not reads of
the properties (they already read columns).

### 1.3 Backend (2; fate D3; R5's area)

| Site | Read | Replacement |
| --- | --- | --- |
| `Backend/Instances/InstanceCollector.Objects.cs:64` | `VTableDef.Of(function.Scope.Owner.BoundSymbol!)` | `VTableDef.Of(VTableDef.Owner(function)!)` |
| `InstanceCollector.Objects.cs:70` | `function.Scope.Owner.BoundSymbol` | `VTableDef.Owner(function)` |

### 1.4 Root helpers (23 + 1 dead; fate D4; BU consents)

`C/ElementAccess.cs` after H10b's C2 (lines are today's): `IsSharedElement` `:20` ×3, `:21`; `IsExclusiveArrayElement` `:30`;
`IsSyntax` `:46` ×2 (`BoundSymbol.Property…`, `Left.BoundType`); `IsSlicing` `:72` (`{ BoundType.Kind: … }`); `StaticSelector` `:87`;
`WritableRoot` `:168` (`{ BoundSymbol: … }`); `IsRawPlace` `:183`, `:189`; `IsPointerPath` `:208`, `:209`; `AccessType` `:231`, `:238`;
`IsSynthesizedAccess` `:248`; `FollowedReference` `:256` (`ConversionBinding`); `IsRuntimeFixedElement` `:300`; `OwnedPathRoot` `:346`
(`{ BoundSymbol.Kind: … }`); `TryType` `:403`, `:409` (`{ BoundSymbol.Property…: }`), `:411`. **(N1)** Total 23: `BoundType` 17,
`BoundSymbol` 5, `ConversionBinding` 1. Every reader of these members is Binding's after H10b. `C/ScalarTypes.cs:61`
(`source.BoundType` in the dead `TryFloatLiteral(Koto, out long)`): deleted (D4). The other root files read none (D7's rows guard them).

### 1.5 Parsing (4; fate D5; BU consents)

| Site | Read | Fate |
| --- | --- | --- |
| `P/Common/ControlFlowContext.cs:104` | `f.BoundSymbol?.Type` (`DiscardsFunctionBody`) | D5 (a) |
| `ControlFlowContext.cs:225` | `type?.BoundType` (`IsUnitType`) | D5 (a) |
| `P/Declarations/FunctionKoto.cs:95` | `this.BoundSymbol?.ReceiverIndex` (`AllowsPositionalArgument`) | D5 (b) |
| `P/Expressions/Names/BaseReferenceKoto.cs:22` | `this.HasCurrentBinding` in `BasePath`'s getter, a Binding fact stored on syntax (writers `Binding.BaseCalls.cs:14`, `:49`, its one reader `:82`) | **(B2)** D5 (c); tag D6 |

### 1.6 Reads another unit deletes (not counted)

| Reads | Unit |
| --- | --- |
| `ElementAccess` `DesignatedType` `:61` ×2, `ValueSource` `:112`, `:115`, `:117`, `:125`, `ConversionKind` `:143`, `:145`, `IsFollowedRoot` `:260`, `HasElementRoute` `:290` ×2, `PathSelector` `:376` (12) | H10b C2 (D6) |
| `C/StaticScalar.cs:43` (the file) | H10b C2 (D6) |
| `C/Analysis/**` (incl. `ControlFlowAnalysis.cs`: `BindingState` 3, `BoundSymbol`, `BoundType`, `ClosureAsCreated` 2) | H10b C2, H10b-2 C4 |
| H11 | none outside Binding |

### 1.7 Write paths (not reads; stay with the properties to R7, §8 OQ1)

`P/Koto.Ownership.cs:194-197` (a replaced child resets its owner's base row); `P/Expressions/ControlFlow/ForKoto.cs:156-158` (a
replaced slot drops the iteration plan; `:156` reads `Iteration` only to reset its decomposition); `P/Declarations/FunctionKoto.cs:426-427`,
`:437`, `:480-481`, `:496-497` (the property-witness, static-initializer and accessor views Binding builds; `:480` reads `BoundSymbol` only
to write its Type); `P/Expressions/SyntheticKoto.cs:25-29`, `P/Expressions/FormattingKoto.cs:22-23` (synthetic Kotos, R5-M9, R7-P11).

### 1.8 Other areas (5; fate D4, §8 OQ4, N6)

`C/Documentation/DocumentationMarkdown.cs:42`, `:78`, `:132` (`BoundSymbol`; R4 consents); `src/Kimi/Testing/TestCatalog.cs:252`
(`BoundSymbol`; R5's); `src/Benchmark/Benchmarks/DiagnosticMeasurements.cs:244` (`BindingState`; R5's). Hover and LSP read
`SemanticQuery` (none).

## 2. Commits and phases (Q12)

| Commit | Content | Kind |
| --- | --- | --- |
| C1 | D2 (with the three test reads, N5), D3; `SemanticSlots` extended and `SemanticSlotReads`; the Mir and Backend rows (D7) | bp |
| C2 | D4: `HirTables.TypeOf` and `PlanRow.Conversion` with every old spelling (N3), ElementAccess's 23, the dead overload, the five reads of §1.8 (N6); the nine root-file rows and the Documentation, Testing and Benchmark rows | bp |
| C3 | D5 (a) (B1's corrected lists, N9's comments, the repair-text rename), (b), (c) (B2); the Parsing row | bp |
| C4 | D6's 37 retags, and the 38th only with the owner's approval (B2); DECISIONS (D9, with R5-M182's amendment line, N10); ARCH `:108` (closures read through `BoundClosure.Of(FunctionKoto)`), `:255` (Parsing, root-helper, Mir and Backend reads ended at H14; N2); BoundClosure's "Access" header; CODEMAP's §14 row gains `Binding.ValueContext` | attr + doc |

Phases: **H14-i** C1 + C2 (mechanical, ≈ 60 sites with N3's spellings); **H14-ii** C3 (≈ 95 lines moved, ≈ 45 call sites and comments);
**H14-iii** C4 and the landing PLAN commit (R5's position; no G row closes). Each commit's evidence carries its `git grep` (the area at
0 under `SemanticSlotReads`, the moved names' callers and references, method groups included).

## 3. H11, G431, consents, review

- **Shared files with H11 (BU):** none by member. Textual overlap possible in `ArchitectureRulesTest.cs` (both add rows) and in Binding
  files where D5 renames a call (`Binding.Lints.cs:482`, `:501`; others as H11's design shows); trivial to rebase either way. H14 can be
  implemented on `claude/r5-check-del` before H11 (D8).
- **(N8) G431 (BU, PLAN `:294`):** an open defect in `DiscardsFunctionBody`, the member D5 (a) moves. Whichever lands second rebases (the
  move is a relocation, the fix a change of its body); R5 tells BU when C3 starts.
- **Consents (§8 D10):** BU for D5 and for every hunk in `C/Binding/**`, `P/**` and the root helpers, after the design review; R4 for
  `C/Mir/**`, `C/Binding/HirTables.cs` (`TypeOf`, `PlanRow.Conversion`; RP17) and `C/Documentation/**`; board holds for
  `ArchitectureRulesTest.cs` and `Binding/HirTables.cs`. Board note: the R7 plan lists H14 in the front's chain (`:243`, `:250`), H0
  (R5-M182) gives it to R5; R5 keeps it (§8 OQ6). **(B2)** The owner's approval of the 38th tag: R5 asks.
- **Review:** design review done (an R5-dispatched reviewer, REVISE → this revision); one implementation review by R4 or an
  R5-dispatched reviewer; BU's consent is per hunk, not a review; then G:check-del's group review.

## 4. Verification, documents, measurements

- **Unit (each code commit):** incremental `Kimi` + `xUnitTest`; the slnx build after C2 and after C3 (tests and Benchmark compile).
  Classes: always `ArchitectureRulesTest`, `HirColumnsTest`, `HirFormsTest`, `MirCensusTest`, `-Trait Purpose=Allocation`. C1:
  `ClosureEnvironmentOriginTest`, `ClosureResultOriginTest`, `ClosureReceiverClassificationTest`, `ConcreteClosureTest`,
  `ContextualClosureTest`, `MirTypesTest`, `FunctionCodegenTest`, `MirBorrowsTest`, `Utf8InterpolationTest`, `Utf8FormatTest`,
  `VTableDefTest`, `VirtualDispatchTest`, `VirtualGenericDispatchTest`, `InheritedObjectCallTest`. C2: `RawStorageTest`,
  `RawPlaceAccessTest`, `SharedElementReadTest`, `DynamicArrayElementTest`, `ElementComparisonTest`, `KeyedIndexingTest`,
  `PublishedPlaceProjectionTest`, `MirBuildTest`, **(N3)** `BlockClosureInferenceTest`, `NeverContinuationTest`, `OriginRowsTest`,
  `TypeIdentityTest`, `PlanRow.Conversion`'s readers through `ConversionOf` (`ConversionReadTest`, `ConversionEmissionTest`,
  `NumericConversionEmissionTest`, `WrapConversionTest`, `BitConversionTest`, `GenericAdaptationTest`, `ObjectCreationAdaptationTest`), **(N7)** `DocumentationMarkdownTest`,
  `DocumentationMarkdownIntegrationTest`, `DocumentationMarkdownReviewTest`, `TestExecutionAnalysisTest` (`FunctionCodegenTest.Testing`
  is in `FunctionCodegenTest`). C3: `ControlFlowRevisionParseTest`, `ControlFlowAnalysisTest`, `ControlFlowConformanceTest`,
  `CurrentControlFlowTest`, `DefaultBindingTest`, `CallArgumentMappingTest`, `NamedArgumentBoundaryTest`, `ParameterNameDefaultTest`,
  `BaseCallBindingTest`, `ImplicitBaseCallTest`, **(B2)** `InheritedReceiverBindingTest`. C3's end: `SemanticSnapshotTest` with
  `KIMI_SEMANTIC_SNAPSHOT=check` (equal). No new test: no behavior changes; the rows are the regression guard.
- **G:check-del's window:** Session with natives, **(N7)** the family sweep naming `'Closure*.ll'`, `'Utf8*.ll'`, `'Object*.ll'` and
  `'Virtual*.ll'` (C1's two views and C2's typed reads), and `-DiagnosticSnapshot` with **0** differences. compare-fixtures is not
  needed (every replacement returns the same object or column value; review N7).
- **Performance:** property reads become static calls of the same bodies. One pinned, interleaved `--compiler-pipeline` A/B on the O2
  corpus inputs from one folder (`DOTNET_TieredCompilation=0`), expected equal; warm zero-allocation tests unchanged;
  `DiagnosticMeasurements`' evidence unmoved (N6); recorded in `artifacts/benchmarks/`.
- **Documents (Auto):** DECISIONS (D9, with R5-M182's amendment line); ARCH `:108`, `:255` and the R6 ledger row's R6-check sentence
  (`:316`, C4); CODEMAP §14 row; PLAN at landing. STATUS, VERIFICATION, LIBRARY: none.

## 5. Sizes and structure limits

C1 ≈ +6 src (views), t ≈ +6 (two rows, the list split and `SemanticSlotReads`) ±3 test reads; C2 ≈ +4 src (`TypeOf` +2,
`PlanRow.Conversion` +2, line splits ≈ +6, the dead overload −6), t ≈ +14 (twelve rows, a comment) ±9 test spellings; C3 ≈ +9 src (the
new file's header, `IsParameterDefault`'s visibility, the gate at its reader) with ≈ 95 lines moved, t ≈ +5 (the Parsing row) ±20
references; C4 ±0 src (+1 attribute line if the 38th is approved), docs ≈ +18. **H14 ≈ +20 src, +25 t** (H0's ≈ −150 counted
`StaticScalar.cs`, `IsIndexerCall` and `DesignatedType`, which H10b D6 now deletes). Limits: `ElementAccess.cs` ≈ 335 → ≈ 341 lines
(tokens well under 5,000); **(B1)** `ControlFlowContext.cs` 239 → ≈ 172; `FunctionKoto.cs` 759 → ≈ 722; `Binding.ArgumentMapping.cs` 65
→ ≈ 102; `Binding.ValueContext.cs` ≈ 80; `Binding.BaseCalls.cs` +0. **(N9)** At-limit members: `TryMapArgument` 6 parameters (1 `out`,
1 `bool`), `BaseMember`'s chain 4 operands; `IsSyntax` 3 operands (D4); the existing excess at `ElementAccess.cs:20-21` and `:299-300`
does not grow (substitutions inside operands). No exception added; no member exceeds its statements or nesting.

## 6. Open questions for R5

Answered in §8 and §9 (revision 0's OQ1–OQ7). Open:

| # | Question | Recommendation | Blocking |
| --- | --- | --- | --- |
| OQ8 | **(B2)** The owner's approval of the 38th R7 tag (`BasePath`) | R5 asks with D6's text; C4 lands the 37 if the answer has not come | for C4's 38th tag only |
| OQ9 | **(N5)** `BasePath` cannot join `SemanticSlots` by name (homonyms, D7); §9 N5's ruling asked for it | The tag and the `P/` site row identify it instead | no |

## 7. Rejected options

- **Parsing reads rewritten to `HirTables` in place** (H0c §4.4's wording): Syntax would still read Binding (ARCH `:36`).
- **The fact passed in by Binding's callers** (`IsValueContext(expression, resultIsUnit)`): `IsValueContext` recurses to the enclosing
  boundary (`ControlFlowContext.cs:122`, `:124`), so its 20 call sites would compute the semantic arm again, a second copy of the rule.
  **(N11)** For (b) it would work, since its four callers already hold the receiver index (`Binding.ParameterShapes.cs:263-264`,
  `Binding.ArgumentMapping.cs:19`), but the rule (which argument may be positional, the receiver exempt) would then be split: the
  receiver exemption decided by Binding's argument, the label boundary by Syntax, one rule with two owners. It is argument mapping,
  Binding's component (`Binding.ArgumentMapping`, CODEMAP `:48`), beside its one caller; moving it is the single-owner change at the
  same parameter count (6).
- **BasePath left with a PLAN row** (§8 OQ2, withdrawn in §9 B2): postpones a read the owner scheduled for H14.
- **ElementAccess moved into Binding now:** its 23 reads would become Binding's, i.e. postponed to R7 against R5-M9's split, which
  needs the owner; R7-T2 moves the helper anyway, carrying D4's column reads.
- **Waiting for H11:** nothing in H14 reads or deletes what H11 changes (D8).

## 8. R5's rulings (2026-11-05)

| # | Ruling | Reason |
| --- | --- | --- |
| OQ1 | Yes: the setters and the two getter-to-mutate reads (§1.7) are the properties' write paths and stay with them to R7. | R5-M9 moves the properties themselves to R7; H14 ends the reads outside Binding. |
| OQ2 | No new tag. `BaseReferenceKoto.BasePath` becomes an open PLAN row for the front (R7's semantic-slot removal), filed in H14's landing PLAN commit. | A new `[Transitional("R7")]` is a postponed removal that needs the owner's approval, and `BasePath` is outside R5-M9's 37; a finding outside the unit is an open issue (AGENTS). |
| OQ3 | `HirTables.TypeOf`, replacing the five existing spellings in the same commit; R4 consents (HirTables, RP17). | One canonical typed read. |
| OQ4 | Yes, in H14 (D-c6: every read outside Binding ends at H14). `TestCatalog` and the Benchmark are R5's; R4 consents to `Documentation`. | The same rule for every area; no residue for H15's grep. |
| OQ5 | Delete `FoldedConstant` in C4 if its one write is a pure duplicate of what `PlanRow.Folded` already carries (a deletion, BU consents); otherwise retag it with the rest. The design review checks which. | A write-only member H10b leaves redundant is removed in the unit that sees it (AGENTS: remove what the change makes redundant). |
| OQ6 | R5 (R5-M182; the owner's C1 of 2026-11-05 moved R6-check's Binding units to R5 as well). The board notes it. | — |
| OQ7 | The moved SPEC 14.5.2 rule keeps `TransferTarget`; **R5's implementer** renames the repair-text helper (`Binding.Diagnostics`) in C3, with BU's consent. | C1: BU approves and consents but does not do R5's edits. |
| D8 | Accepted: H14 starts after H10b's C2, independent of H11 and H10b-2; D9 records the departure from R5-M182's prerequisite. | Nothing in H14 reads or deletes what H11 changes. |
| D10 | Design review by an **independent reviewer R5 dispatches**, not R4, so R4's lane stays on G428 (H10b-2's critical path). R4 consents to the `Mir/**`, `HirTables` and `Documentation` hunks; BU consents to D5 and to the Binding, Parsing and root-helper hunks after the review. Implementation review: R4 or an R5-dispatched reviewer. | Relieves BU (C1) without taking R4 off the critical path. |

## 9. R5's rulings on the design review (2026-11-05; `design-review.md`, REVISE)

| Finding | Ruling |
| --- | --- |
| B1 | Accepted: `IsParameterDefault` reads syntax only and stays in Parsing with `ResolveTransferTarget`; correct D5's moved and staying lists and §5's sizes. |
| B2 | Accepted; **§8 OQ2's ruling is withdrawn** (R5's error: the read is inside H14's own scope, R5-M9). H14 ends the Parsing read in C3: `BasePath` becomes a plain accessor over its field, and the `HasCurrentBinding` gate moves to Binding's reader(s) (`Binding.BaseCalls.cs`, a Binding read R5-M9 postpones to R7 with the rest). The member itself is a semantic slot on syntax that CR-A7 should have marked: C4 tags it `[Transitional("R7")]` beside the 37 and adds it to `SemanticSlots`, **provided the owner approves the 38th member of R5-M9's postponement** (R5 asks; non-blocking for C1-C3). Without that approval, C4 stops at the 37 and R5 reports. |
| OQ5 | Per the review: retag `FoldedConstant` with the rest (it is the write path of a conversion's `PlanRow.Folded`). |
| N1 | Correct the counts (`BoundType` 17, `BoundSymbol` 5; 20 test callers). |
| N2 | Correct the ARCH line citations. |
| N3 | Accepted: `HirTables.TypeOf` replaces every spelled-out typed read, including `MirBuilder.Values.cs:51` and the tests' spellings; the `ConversionBinding` decode gets one helper too if it has three or more spellings (otherwise say why not). R4 consents to the Mir and HirTables hunks. |
| N4 | `VTableDef.Owner` is VTableDef's dispatch rule only (it already computes the expression twice); D9 says so, and that R7-Y4 routes Binding's 43 `Scope.Owner.BoundSymbol` sites (R5-M9). |
| N5 | (a), (b) accepted: zero-reference rows for every root-helper file and for `C/Documentation`, `src/Kimi/Testing` and `src/Benchmark`'s sites; (c) one name list: extend `SemanticSlots` instead of a second `KotoForwarding` list, or state the exact reason they must differ. |
| N6 | One route for one fact: `HirTables.Symbol` for `TestCatalog`, `DocumentationMarkdown` and the Benchmark (unbound-safe, no `SemanticQuery` allocated, the benchmark's evidence unmoved). |
| N7 | Add the named classes and name the native families for the window's sweep. |
| N8 | Name G431 (BU's open defect in `DiscardsFunctionBody`) in §3: whichever lands second rebases; R5 tells BU when C3 starts. Dev is merged into the branch (`aadc14bc8`). |
| N9 | Fix the stale comments in C3; keep the at-limit members within their limits (say how). |
| N10 | D9 adds an amendment line to R5-M182 for D8's order change; D10 states why Major, not Full. |
| N11 | §7 states why D5 (b) moves although its callers hold the index. |

## 10. Re-check ACCEPT (2026-11-05) and notes for the implementer

The same reviewer re-checked revision 1: ACCEPT, no blocking finding (B1, B2 resolved as §9 rules; OQ9's exclusion holds). Carry into
implementation: `PlanRow.Conversion` is `readonly`; D9 names the three readers that now read `HirTables` (TestCatalog,
DocumentationMarkdown, the Benchmark); the `P/` row cannot identify `BasePath`, only C4's tag can (the owner's approval of the 38th).
Consents asked on the board: C-23 (BU: D5 and the Binding, Parsing and root-helper hunks, OQ7's rename), C-24 (R4: the Mir hunks,
`HirTables.TypeOf`, `PlanRow.Conversion`, Documentation).
- **C-23 YES (BU, 2026-11-05)** for D5 (a), (b), (c), D4 with `TryFloatLiteral`'s deletion and OQ7's rename, with conditions the
  implementation keeps: each commit bp with VERIFICATION's triple (diagnostic snapshot 0, semantic snapshot identical, compare-fixtures
  identical: this supersedes §4's "compare-fixtures not required"; use N7's families); `Binding.ValueContext.cs` begins with its contract
  (responsibility, inputs, outputs, invariants); the moved `TransferTarget` keeps BU-101's contract text and names BU-101; BU's
  branches touching the moved callers (Q-45 G417 U2b, G422 `claude/bu149-g422`, G423 `claude/bu148-g423`, later G431) follow
  "whichever lands second rebases". A consent only, not a review (C1).
- **C-24 YES (R4, 2026-11-05)** for D2, D4's `HirTables.TypeOf` and `PlanRow.Conversion` (readonly) and Documentation's reads, checked
  against dev `4862b9fc7`: the C1 commit states both premises of `MirBuilder.Formatting.cs:52` (after a finished pass `CallPlan.Of`
  equals `BoundCall`; the writer acquisition is never `CalleeKind.Value`, which `CallPlan.Of` does not filter); `MirBuilder.cs:73` keeps
  `?? BoundType.Unit`. Overlaps, whichever lands second rebases: R4's G428 (MirBuilder Control, Blocks, Transfers, Match, Acquisitions,
  Values, Expansions) and G427; BU's G422 holds `MirBuilder.Values.cs` l.189/194 (H-5), which H14's `:51` and `:273` hunks respect.
  No further R4 look at these hunks if the implementation matches the design.
