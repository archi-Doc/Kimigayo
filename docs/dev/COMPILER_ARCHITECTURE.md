# Compiler architecture

The compiler reduction plan ([draft](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md))
was adopted on 2026-10-10; it holds the design, rationale and decisions (its §18), and [DECISIONS](DECISIONS.md) holds
the design decisions taken since. This document holds the enforced
structure: phases, dependencies, vocabularies, canonical components, structure limits, migration rules and the stage
ledger. SPEC/IMPL define behavior, CODEMAP navigation, STATUS verified support, AGENTS work policy and VERIFICATION
checks. Only completed ledger entries describe the current compiler; the rest is the target.

## Target pipeline

| Phase | Owner | Output |
| --- | --- | --- |
| P1 | Syntax | Koto syntax tree with dense pre-order `SyntaxId`; no phase after parsing writes to it |
| P2–P5s | Binding | Declaration tables, `HirTables` keyed by `SyntaxId`, four Plan kinds (Call, Pattern, Closure, Expansion), HIR lints and startup checks |
| P6 | Mir | One MIR body per definition (desugaring happens here) |
| P7–P9 | Mir | Validator, initialization/Move, drop elaboration, runtime lints, definition-stage capability check, effects, borrow checking per body and Semantics case |
| P10 | — | Acceptance (SPEC §23.2.2) |
| P11–P12 | Backend | Monomorphization, shims, instance-stage capability check, layout/ABI, LLVM text written directly from MIR |
| P13 | Build | Native tools, outside the compiler |

## Dependency rules

| Layer | May read | Must not read |
| --- | --- | --- |
| Syntax | Source | Binding |
| Binding | Syntax tree, its own tables | Mir, Backend |
| MIR builder (`Mir/Build`) | `HirTables`, Plans, declaration tables, pure `TyCtxt` functions, syntax children, `SourceRef` | Binding internals, semantic slots on Koto, parent-walking inference |
| MIR analyses | MIR, declaration tables, pure `TyCtxt` functions | Syntax, HIR |
| Backend | MIR, declaration tables, `TyCtxt` | HIR, borrow-checker state, syntax |
| Hover and LSP | `SemanticQuery` | Private Binding state |

Dependency and member restrictions are review obligations after the analyzer retirement (DECISIONS CR-R1).
Review resolved types and member owners, including aliases, inferred receivers, attributes and every partial
declaration. Mir/Build consumes only published Binding contracts; a new view needs a reason in its commit.
Outside Mir/Build, syntax, HIR and plans are forbidden. Syntax must not read Binding; Hover uses SemanticQuery.
The former exact allow/deny inventory remains in `2e341155^:src/Kimi/CodeRules.globalconfig` as review evidence;
its removal does not authorize new dependencies. Global forbidden child-walk members are VisitChildrenCore,
GetChildNodes, ReplaceChildCore and ReplaceInList; the canonical walk remains ForEachChildSlot.

## Closed vocabularies

MIR has 5 statement, 10 rvalue, 6 terminator (plus the `CheckTarget` attribute), 7 projection, 4 operand and 5 callee
kinds; the HIR vocabularies and `IntrinsicId` have fixed limits too (listed below). Enum
aliases count separately; implicit fields do not. A
form not yet implemented has one diagnostic code, the location-only `Unsupported_Kd`, reported by Binding, MIR
construction or the capability check. A new vocabulary entry or a second Unsupported code is a design change that needs
a SPEC basis and owner approval.

| Vocabulary | Maximum entries |
| --- | ---: |
| `OwnershipOperationKind` | 38 |
| `OwnershipValueKind` | 31 |
| `EmissionOpcode` | 49 |
| `CompilerFunctionKind` | 76 |
| `BindingFailure` | 89 |
| `HirOp` | 40 |
| `HirRes` | 9 |
| `HirAcquire` | 7 |
| `HirAdaptation` | 9 |
| `HirPlaceKind` | 11 |
| `HirCompletion` | 3 |
| `ExpansionKind` | 7 |
| `IntrinsicId` | 34 |
| `MirStatementKind` | 5 |
| `MirRvalueKind` | 10 |
| `MirTerminatorKind` | 6 |
| `MirProjectionKind` | 7 |
| `MirOperandKind` | 4 |
| `MirCalleeKind` | 5 |

These are the unchanged pre-retirement limits; IntrinsicId returns to 30 at R8.

## Adding a feature

Once R6 makes MIR the only path, a new form adds one `HirOp` or Plan row, one MIR builder function and, if needed, a
descriptor, operator or intrinsic table row. Analyses and code generation change only for a new vocabulary entry. Until
then the freeze below applies to the old paths.

## Canonical components

Each concern has one canonical form. Use and extend it; a different form for the same concern replaces the existing one
in the same unit. Rows give the responsibility and entry point; details live in the code and CODEMAP.
Each entry file begins with the component's contract (AGENTS). Paths are under `src/Kimi/Compiler`.

| Concern | Canonical form | Source |
| --- | --- | --- |
| Child walk | `ForEachChildSlot`, shared by visiting, collecting and replacing | `Parsing/Koto/Common/ChildSlots.cs` |
| Syntax identity | Dense pre-order `SyntaxId` per module; `SourceRef(Module, SyntaxId)`, the only way MIR and its records name syntax | `Parsing/Koto/Common/SyntaxTable.cs`, `Core/SourceRef.cs` |
| Phases after Binding | The `Compilation` entry points (`Analyze`, `Analysis`, `Problems`, `ReportAnalysis`, `WriteIr`, `Validate`, `SyntaxOf`) and the pipeline switch | `Core/Compilation.Pipeline.cs` |
| Analysis records | `Problem(Code, SourceRef)`, merged per code and location; until R6 the MIR analyses' problems stay unpublished on `MirProgram.Problems` (DECISIONS R4-M1) | `Core/Compilation.Pipeline.cs` |
| Binding facts for MIR | `HirTables` columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and `PlanRow`s keyed by `SyntaxId`; `HirAdjust.Of`, the one conversion from Binding's acquisition vocabularies; `Binding.BareAcquire`, the one bare-acquisition rule; a definition's parts are published only when Binding resolved the definition | `Binding/HirTables.cs`, `Binding/HirVocabulary.cs`, `Binding/Binding.Hir.cs`, `Binding/Binding.Hir.Adjust.cs` |
| Binding facts for Hover, LSP and tests | `SemanticQuery` (`Compilation.Semantics`) | `Binding/SemanticQuery.cs` |
| Calls | `CallPlan`, reached only through `CallPlan.Of(node, ordinal)`; an omitted default is its `DefaultDecl` | `Binding/CallPlan.cs` |
| Desugared forms | `ExpansionPlan.Of`; the Pattern plans `BoundMatch.Of`, closures `BoundClosure.Of` and enum Case construction `BoundEnumConstruction.Of` | `Binding/ExpansionPlan.cs` |
| Stored shape of a struct or enum | `AdtDef` | `Binding/AdtDef.cs` |
| Types and substitutions | `TyCtxt` (`Compilation.Types`): interning, `Subst` (`SubstOf`, `Apply`, `Compose`, `Signature`), implicit `Layer`s, `CaseType`, `Adaptation` (Binding's selection of an explicit adaptation), `Name` for display | `Binding/TyCtxt.cs` |
| Effect facts | `TyCtxt.EffectObligations` (who owes `confined`: `EffectObligation` rows), `DispatchBounds` and `CallableBounds` (the bounds a requirement or virtual call and a call through an abstract Callable input may use under a definition's premises; `ItemContract` a requirement item's Contract reference), `Bodyless` (what a function without a body does), `Implementation` (the bodies a call may run), `Published` (an operation's published row) and `Witnesses` (what a requirement runs on a Type that is not abstract), `Copy`, `Sealed`, all under one premise scope per definition, `Binding.EffectPremises` (DECISIONS R4-M23, R4-M28) | `Binding/TyCtxt.Effects.cs`, `Binding/Binding.EffectObligations.cs` |
| Semantics cases | `PairBinder` (a body's binders, `CaseProduct`, `CaseBound`); `Binding.CaseLimits`, the one decision of the bound per definition before its parts are bound; `MirCases`, per-case views of one body that is never copied, under which an `Adapt` is the operation its case selects, and `Views`, the views of a callee a call's substitution selects | `Binding/BindingModel.cs`, `Binding/Binding.CaseLimits.cs`, `Mir/MirCases.cs` |
| Library operations | `IntrinsicDisposition.Of`: a Kimigayo body, an `IntrinsicId` or a shim for each `CompilerFunctionKind`; `IntrinsicDisposition.Effect`, its effect class (SPEC 8.11.2); `KimiLibraryCatalog.Row`, an operation's published row `PublishedRow(Runs, On, Destroys)` (SPEC 4.9.6, 12.4.3), which `TyCtxt.Published` gives MIR | `Binding/KimiLibraryKinds.cs`, `Binding/KimiLibraryCatalog.cs` |
| MIR storage | `MirBody` struct arrays with interned projection chains, `ProjectedType` the one projection Type rule; `MirProgram`, bodies by `MirDefKey` and `Ordinal`, the body a callee runs (`Callee`), pooled for warm rebuilds | `Mir/MirVocabulary.cs`, `Mir/MirBody.cs`, `Mir/MirProgram.cs` |
| Operands, accesses and effects | `MirDescriptors.Of` rows; `MirUses.ForEach` (of a body, or under a case view with each `Adapt` resolved) and `MirUses.Successors`, the one enumeration the validator and the analyses share | `Mir/MirDescriptors.cs` |
| Reachability | `MirReach`, the one depth-first walk over a body's blocks along runtime edges and, when asked, CheckTarget edges (the validator, A3, the dataflow's reverse postorder); reached blocks and their `Postorder` accumulate until `Clear` | `Mir/MirReach.cs` |
| Type facts in MIR | `MirTypes`: `Destruction` (the one walk of what destroying a value does), `MayNeedDrop` (over it), `HoldsRaw`, `IsExclusive`, `Agree` (Type agreement modulo Origins), `RefKind` (the mode of a borrow), `PointerCast` | `Mir/MirTypes.cs` |
| MIR construction | `MirBuilder` (`Compilation.BuildMir`): one body per definition and Closure, built by Op, Adjust and PlaceShape; `Ref`, the one borrow rule; `Set` (a node's own value, assigned where the node completes) and `Make` (a part made where it is built, before later parts that may not complete), the two assignments; one `Adapt` per case adaptation; one drop tree per transfer target; one template per default; `Unsupported(node)`, the one Unsupported record with an Opaque value | `Mir/Build/MirBuilder*.cs` |
| MIR validation | `MirValidator`, the one structural and Type verifier; a finding is `InternalInvariant_Kd` | `Mir/MirValidator.cs`, its Type tier `Mir/MirValidator.Types.cs` |
| MIR analyses | `MirAnalysis` (`Compilation.BuildMir`): the analyses of one program version, once per version after build and validation, in the pass order its contract fixes (DECISIONS R4-M2a) | `Mir/Analysis/MirAnalysis.cs` |
| Runtime lints (A3) | `MirLints`: a require failure body whose end a runtime path from its entry reaches (`MirBody.MustNotComplete`) is `RequireFallthrough_Kd` (DECISIONS R4-M7a) | `Mir/Analysis/MirLints.cs` |
| MIR dataflow | `MirDataflow`: forward bit-set states stored at flow heads (chains of blocks), union join, P1 over runtime edges and P2 over the heads only the checks reach with P1 frozen, `Replay` for the checks and later readers; the analyses' one arena Resource limit (DECISIONS R4-M20, R4-M15a) | `Mir/Analysis/MirDataflow.cs` |
| Move paths | `MirMoveData`: a body's move paths keyed by (parent, step kind, argument) and numbered in pre-order, so a subtree is a range; `Find` gives a Place's path (DECISIONS R4-M21) | `Mir/Analysis/MirMoveData.cs` |
| Initialization and moves | `MirInit` (A1): MaybeUninit, MaybeMoved and EverInit, and for A2 MaybeInit and MaybePartial, per move path and Semantics case view (DECISIONS R4-M21, R4-M6a) | `Mir/Analysis/MirInit.cs` |
| Drop elaboration (A2) | `MirDrops`: per Drop the runtime edges reach and view class a verdict (Keep, Elide, Flag, Open), the drop flags and their set and clear points in `MirProgram.Drops` (`MirDropTable`, a Drop's group by `Group`), read by R5, A4, A5 and the dump; an incomplete layer with its own `drop` is `MovedPlace_Kd` (DECISIONS R4-M6a) | `Mir/Analysis/MirDrops.cs` |
| Effect summaries | `MirEffects` (A4): per body view the classes and atoms of the effect summary, composed through each call's substitution, one worklist to the least fixed point, and the `confined` obligations Binding publishes judged per view (DECISIONS R4-M22) | `Mir/Analysis/MirEffects.cs`, `Mir/Analysis/MirEffects.Solve.cs` |
| MIR analysis problems | `MirReport`: one `MirProblem(Code, Subject, Views)` per code and subject with its views merged; the one home of the derived-problem rules (DECISIONS R4-M3a, R4-M10) | `Mir/Analysis/MirReport.cs` |
| MIR text | `MirDump.Write`, with A2's verdicts and flags on an analyzed program, compared by the dump goldens and the MIR snapshot | `Mir/MirDump.cs` |

## Structure limits

Members and files outside the old Analysis/Emission paths stay within these limits, which the owner decided on
2026-10-11 for the [Code Rules Analyzer](../../draft/Changes/2026-10-11%20Code%20Rules%20Analyzer.md) (its §9).

| Measure | Limit |
| --- | --- |
| Parameters per member | 6, of which at most 2 optional, 1 `out` and 1 `bool` |
| `bool` literal arguments | Passed by name |
| Failure text returned through `out string` | None; record a typed Problem instead |
| Statements per member | 60 |
| Control-statement nesting | 4 |
| Operands in one chain of the same logical or pattern operator | 4 |
| Tokens per file | 5,000 |
| Line length | 160 characters |

A member or file already over a limit may not get worse; split it by responsibility before it grows. An exception to
the statement or nesting limit states its reason and bound in the code. Mir and the Backend take no baseline: their
violations are fixed, except syntax-class decisions in the MIR builder, which reach 0 by the end of R6. Review checks these limits against the existing source and recorded exceptions;
the analyzer and its baseline have been retired (DECISIONS CR-R1).

## Code rules

The owner retired the analyzer workflow on 2026-10-15 after its producer/configuration were deleted in
`2e341155` (DECISIONS CR-R1). The requirements below remain review obligations; they are not claimed as
automated build diagnostics. `ArchitectureRulesTest` separately retains its existing no-growth assertions.

| Concern | Review contract |
| --- | --- |
| Dependencies and vocabulary | Follow the published layers, member restrictions and closed vocabulary limits above. |
| Syntax identity | No Koto-keyed dictionary, set or weak table outside Syntax; migrate existing Binding uses by R7 exit. |
| Capability gates | No boolean `Supports*` method or property. |
| MIR dispatch | No Koto type test or KotoKind dispatch in Mir/Build after R6; remove existing uses by that stage. |
| Transitional declarations | Keep a removal stage and reason; the stage ledger governs when removal is due. |
| Catch boundaries | No unfiltered catch-all outside CLI, LSP and test-runner boundaries. |
| Member and file structure | Follow the parameter, statement, nesting, logical-chain and token limits above. |
| Density | At most 160 UTF-16 units per physical line, excluding whole multiline-string lines; no nested conditional expression. |
| Deconstruction | Computed tuple literals do not initialize all-new-local deconstructions; existing-variable assignments remain allowed. |
| Unused code | Remove unused/unread private members and unused parameters while preserving behavior. |

Existing excess cannot grow. Only statement and nesting limits admit `CodeRuleException`, with an explicit
bound and reason. `Transitional` records a removal obligation; neither attribute is an analyzer certificate.
New/increased exceptions and later removal stages need owner approval recorded in the commit. Reduced bounds
and earlier stages do not. Mechanical evasions fail review. At a stage exit, record measurements and review
exceptions and transitional declarations with the owner; the deleted Report command is not a gate.

At R6, Syntax's restriction extends to the whole Binding layer. At R7, UnwrapParentheses is restricted to
Parsing/Binding. At R5, add Backend and forbid System.Linq.Enumerable in Mir analyses and Backend, rewriting
existing uses first; the owner decides whether to extend these rules to tests. IntrinsicId returns to 30 at R8.

## Rules during the migration

- **Freeze (D5).** From R0 until R6 completes, the old Analysis and Emission paths receive no new accepted forms or
  generation features. Allowed: deletion, miscompile fixes mirrored on the new path, the mechanical follow-up that a
  stage of the plan names, and the owner's exception for the Place result removal (DECISIONS PR-1).
- **Size.** Code size has no caps, stage ceilings or forecasts (DECISIONS Q5). Each compiler commit states its net size
  change per area, and stage exits record area sizes (AGENTS). `ArchitectureRulesTest` retains its no-growth ratchets.
- **Missing facts (R3–R5).** Binding publishes a fact the MIR side lacks as a small view; Mir never re-derives it.
- **Difference ledger (R4–R6).** Each difference between the paths is one row in `artifacts/verify/mir-diff`, classified
  as (a) an old-path error, (b) a new-path error, fixed, (c) a SPEC ambiguity, clarified, (d) an intended change (D4 and
  the like) with its clause and plan Appendix B item, or (e) a form not ported under D2, recorded in STATUS and PLAN. R4
  exits only with no unclassified and no (b) row. Each R4 and R5 session ends by reporting the number of unclassified
  differences; if it fails to fall twice in a row, the owner decides whether to stop (plan §18 P9).
- **Temporary structures**, with the stage that removes them: the Koto semantic forwarding properties and the
  `KIMI_PIPELINE` switch (R6); the HIR publisher walk, replaced by inline writes (R7, DECISIONS R2b-D2); the temporary
  intrinsics `Capacity`, `FormatBuffer`, `FormatHint` and `FormatFinish` that the owner admitted on 2026-10-11
  (`IntrinsicId` limit 34, back to 30 in R8; DECISIONS R3-9, R3-10); the `Compilation.Analyzed` event, the difference
  ledger's observation point, and `Binding.IsEffectRecord`, the ledger's old-side effect subset (R6, DECISIONS R4-M17, R4-M23).
  Transitional attributes identify these declarations (CR-A7). The lasting HIR publication/version contract is unmarked;
  only its publisher walk and private state disappear at R7. LegacyRuntimeHelpers has no declaration yet; its future
  producer must record the R8 obligation when introducing it.
- **Performance and allocation.** Generated native code may not regress in O2 run time or allocation counts (plan §18
  P1, P2); O0 run time and compiler processing time may, when recorded (P1, P3). A timing regression exceeds the
  variation of repeated matched runs (P4). Compiler allocations follow the owner's decision of 2026-10-11 (AGENTS,
  `8071549f`), which supersedes plan §18 P11 and §14-8: an allocation is allowed when it makes equivalent work faster or
  reduces compiler code with a reasonable increase in memory, with time, allocations and retained memory measured and
  recorded. Existing compiler allocation regressions hold until a measured change updates them.

## Stage ledger

| Stage | State | Scope |
| --- | --- | --- |
| R0 | Done (timing baselines carried to before R5) | Baselines, size and rule ratchets, disposition ledger, narrow-implementation and Advice/Note inventories, freeze |
| R1 | Done 2026-10-10 (closed by user instruction at 129,482 lines, above its 127,000 ceiling) | Delete-first: one Unsupported code, Advice/Note reduction, library validation into tests, dead code, control-flow type flow, OCC-X waiting machinery, independent narrow implementations |
| R2a | Done 2026-10-11 (closed at 126,611 lines, above its 125,000 ceiling, by the stage decision to close at the measured size) | `SyntaxId`, `HirTables` behind forwarding properties, `AdtDef`, unified CallPlan type, pure fits, child-walk and operator-class consolidation, Hover move |
| R2b | Done 2026-10-11 (closed at 127,815 lines, within its 129,000 ceiling; the unused 1,185 lines pass to R3) | `TyCtxt` and `Subst`; the published HIR columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and their publisher; `CallPlan.Of`, `ExpansionPlan`, the Pattern plans and closure definitions as views; `DefaultDecl` and `PreparedParam`; the pair-binder view; the intrinsic table. The five read-only declaration-table views (`ImplTable`, `VTableDef`, `UniversalRelations`, `EffectBoundTable`, `AvailableBounds`) are deferred to their first readers in R4/R5 |
| R3 | Done 2026-10-11 (closed at 133,004 lines, within its 135,500 ceiling: Mir 4,765, Binding 56,569, Core 1,805, the other areas at their R2b-exit sizes; +5,189 against a forecast of +5,750) | MIR model, descriptor table, validator with its Type tier, dump and builder (shadow): every body of the library and the 43 milestones builds without validator findings, with Opaque values only at Binding-rejected nodes of Milestones 33, 35 and 38; 30 dump goldens. The work paused after R3 by owner instruction |
| R4 | Active; A1–A4 landed, the remaining scope is being replanned (DECISIONS R4-M33) | MIR analyses A1–A6 and the difference ledger (shadow). Every analysis unit extends `Mir/Analysis/MirAnalysis.cs`; its three-consecutive-units restructuring is deferred to the R4 exit (DECISIONS R4-M2a). `Mir/MirDump.cs` changed in three consecutive units (U0c, U4, U5): its next change restructures it first |
| R5 | Pending | Monomorphization, shims, layout/ABI, code generation, capability table (shadow); MIR holds the bodies of overrides no instance applies, which generation reads applicability to skip (DECISIONS R4-M28) |
| R6 | Pending | Switch the default to MIR, then delete the old Analysis/Emission paths. Unit a publishes the MIR problems and folds the MIR construction records, which keep their own merge in `MirProgram` until then, into `MirReport` (DECISIONS R4-M3a). TransferRequired moves to Binding/HIR (R4-M4), which makes A1's StaticMovePathRequired at the bare acquisitions it rejects derived. Deleting the old effect checks ends `Binding.EffectObligations`' reading of their violation tables (`effectViolations`, `virtualEffectViolations`): an obligation then follows the conformance's and the function's own validity (DECISIONS R4-M23); the old twins of A4's rules go with them: `EffectSummary.Function`'s foreign-function branch (beside `TyCtxt.Bodyless`), its formatting-callback and composite-comparison branches and the Dictionary selection and literal comparison of the old walk (beside the published rows and `TyCtxt.Witnesses`), with the `Formatting` effect class only they read, and `CallableEffectFailure`'s decision of a Closure's owner acquisition (beside `AddCallableObligation`'s `Owner`); the Array `append`/`insert`/`pop`/`remove` rows join the catalog (DECISIONS R4-M28) |
| R7 | Pending | Binding consolidation: resolver, inference context, Contract solver, CallPlan pipeline, Problem records |
| R8 | Pending | Library algorithms in Kimigayo, about 28 intrinsics |
| R9 | Pending | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation |
