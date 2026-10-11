# Compiler architecture

The compiler reduction plan ([draft](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md))
was adopted on 2026-10-10; it holds the design, rationale and decisions (its §18). This document holds the enforced
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
| P10 | — | Acceptance (SPEC §23.3.3) |
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

`ArchitectureRulesTest` enforces the MIR rows. `MirReadsNoBindingInternals` lets Mir name only the published Binding
types (`PublishedBindingTypes`; a new view joins with a one-line reason in its commit) and no old Analysis/Emission type.
`OnlyTheMirBuilderReadsSyntaxAndHir` keeps syntax, HIR and plans out of `Mir/` outside `Mir/Build` (`SemanticsKind`, a
Type vocabulary in the Parsing namespace, is named through an alias).

## Closed vocabularies

MIR has 5 statement, 9 rvalue, 6 terminator (plus the `CheckTarget` attribute), 7 projection, 4 operand and 5 callee
kinds; the HIR vocabularies and `IntrinsicId` have fixed limits too (`ArchitectureRulesTest.VocabularyDoesNotGrow`). A
form not yet implemented has one diagnostic code, the location-only `Unsupported_Kd`, reported by Binding, MIR
construction or the capability check. A new vocabulary entry or a second Unsupported code is a design change that needs
a SPEC basis and owner approval.

## Adding a feature

Once R6 makes MIR the only path, a new form adds one `HirOp` or Plan row, one MIR builder function and, if needed, a
descriptor, operator or intrinsic table row. Analyses and code generation change only for a new vocabulary entry. Until
then the freeze below applies to the old paths.

## Canonical components

Each concern has one canonical form. Use and extend it; a different form for the same concern replaces the existing one
in the same unit. Rows give the responsibility and entry point; details live in the code and CODEMAP. Paths are under
`src/Kimi/Compiler`.

| Concern | Canonical form | Source |
| --- | --- | --- |
| Child walk | `ForEachChildSlot`, shared by visiting, collecting and replacing | `Parsing/Koto/Common/ChildSlots.cs` |
| Syntax identity | Dense pre-order `SyntaxId` per module; `SourceRef(Module, SyntaxId)`, the only way MIR and its records name syntax | `Parsing/Koto/Common/SyntaxTable.cs`, `Core/SourceRef.cs` |
| Phases after Binding | The `Compilation` entry points (`Analyze`, `Analysis`, `Problems`, `ReportAnalysis`, `WriteIr`, `Validate`, `SyntaxOf`) and the pipeline switch | `Core/Compilation.Pipeline.cs` |
| Analysis records | `Problem(Code, SourceRef)`, merged per code and location | `Core/Compilation.Pipeline.cs` |
| Binding facts for MIR | `HirTables` columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and `PlanRow`s keyed by `SyntaxId`; `HirAdjust.Of`, the one conversion from Binding's acquisition vocabularies; `Binding.BareAcquire`, the one bare-acquisition rule | `Binding/HirTables.cs`, `Binding/HirVocabulary.cs`, `Binding/Binding.Hir.cs` |
| Binding facts for Hover, LSP and tests | `SemanticQuery` (`Compilation.Semantics`) | `Binding/SemanticQuery.cs` |
| Calls | `CallPlan`, reached only through `CallPlan.Of(node, ordinal)`; an omitted default is its `DefaultDecl` | `Binding/CallPlan.cs` |
| Desugared forms | `ExpansionPlan.Of`; the Pattern plans `BoundMatch.Of`, closures `BoundClosure.Of` and enum Case construction `BoundEnumConstruction.Of` | `Binding/ExpansionPlan.cs` |
| Stored shape of a struct or enum | `AdtDef` | `Binding/AdtDef.cs` |
| Types and substitutions | `TyCtxt` (`Compilation.Types`): interning, `Subst` (`SubstOf`, `Apply`, `Compose`, `Signature`), implicit `Layer`s, `CaseType`, `Name` for display | `Binding/TyCtxt.cs` |
| Semantics cases | `PairBinder` (a body's binders, `CaseProduct`); `MirCases`, per-case views of one body that is never copied | `Binding/BindingModel.cs`, `Mir/MirCases.cs` |
| Library operations | `IntrinsicDisposition.Of`: a Kimigayo body, an `IntrinsicId` or a shim for each `CompilerFunctionKind` | `Binding/KimiLibraryKinds.cs` |
| MIR storage | `MirBody` struct arrays with interned projection chains, `ProjectedType` the one projection Type rule; `MirProgram`, bodies by `MirDefKey`, pooled for warm rebuilds | `Mir/MirVocabulary.cs`, `Mir/MirBody.cs`, `Mir/MirProgram.cs` |
| Operands, accesses and effects | `MirDescriptors.Of` rows; `MirUses.ForEach` and `MirUses.Successors`, the one enumeration the validator and the analyses share | `Mir/MirDescriptors.cs` |
| Type facts in MIR | `MirTypes`: `MayNeedDrop`, `HoldsRaw`, `IsExclusive`, `Agree` (Type agreement modulo Origins) | `Mir/MirTypes.cs` |
| MIR construction | `MirBuilder` (`Compilation.BuildMir`): one body per definition and Closure, built by Op, Adjust and PlaceShape; `Ref`, the one borrow rule; one drop tree per transfer target; one template per default; `Unsupported(node)`, the one Unsupported record with an Opaque value | `Mir/Build/MirBuilder*.cs` |
| MIR validation | `MirValidator`, the one structural and Type verifier; a finding is `InternalInvariant_Kd` | `Mir/MirValidator.cs` |
| MIR text | `MirDump.Write`, compared by the dump goldens and the MIR snapshot | `Mir/MirDump.cs` |

## Structure limits

Members and files outside the old Analysis/Emission paths stay within these limits, which the owner decided on
2026-10-11 for the [Code Rules Analyzer](../../draft/Proposals/2026-10-11%20Code%20Rules%20Analyzer.md) (its §9).

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
violations are fixed, except syntax-class decisions in the MIR builder, which reach 0 by the end of R6. The analyzer
(units A0–A8, before R4) turns these limits into build errors over a shrinking baseline; until then review applies them.

## Rules during the migration

- **Freeze (D5).** From R0 until R6 completes, the old Analysis and Emission paths receive no new accepted forms or
  generation features. Allowed: deletion, miscompile fixes mirrored on the new path, and the mechanical follow-up that a
  stage of the plan names.
- **Sizes.** `CompilerSizeBudgetTest` holds the size caps and `ArchitectureRulesTest` the no-growth ratchets. A stage
  closes only within its exit ceiling (plan §12.2); otherwise work stops and the owner decides, given the measured size,
  the remaining deletion candidates and their cost. When a reduction stage exits, its caps fall to the measured size plus
  2%. A cap rises only on owner instruction or to preserve the native O2 and allocation guarantees (plan §18 P5), with the
  reason recorded. In R2b–R5 the total cap is the stage's cumulative ceiling: R4's is 142,000, with Mir 11,500, Binding
  500 lines over its R3-exit size and Core 1,830.
- **Forecasts.** Each unit states its line forecast; an overrun above 20% is consolidated before commit or reported with
  its reason.
- **Missing facts (R3–R5).** Binding publishes a fact the MIR side lacks as a small view, at most 30 Binding lines per
  unit; Mir never re-derives it.
- **Difference ledger (R4–R6).** Each difference between the paths is one row in `artifacts/verify/mir-diff`, classified
  as (a) an old-path error, (b) a new-path error, fixed, (c) a SPEC ambiguity, clarified, (d) an intended change (D4 and
  the like) with its clause and plan Appendix B item, or (e) a form not ported under D2, recorded in STATUS and PLAN. R4
  exits only with no unclassified and no (b) row. Each R4 and R5 session ends by reporting the number of unclassified
  differences; if it fails to fall twice in a row, the owner decides whether to stop (plan §18 P9).
- **Temporary structures**, with the stage that removes them: the Koto semantic forwarding properties and the
  `KIMI_PIPELINE` switch (R6); the HIR publisher walk, replaced by inline writes (R7); the temporary intrinsics
  `Capacity`, `FormatBuffer`, `FormatHint` and `FormatFinish` that the owner admitted on 2026-10-11 (`IntrinsicId` limit
  34, back to 30 in R8).
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
| R4 | Next | MIR analyses A1–A6 and the difference ledger (shadow) |
| R5 | Pending | Monomorphization, shims, layout/ABI, code generation, capability table (shadow) |
| R6 | Pending | Switch the default to MIR, then delete the old Analysis/Emission paths |
| R7 | Pending | Binding consolidation: resolver, inference context, Contract solver, CallPlan pipeline, Problem records |
| R8 | Pending | Library algorithms in Kimigayo, about 28 intrinsics |
| R9 | Pending | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation |

## Baseline

At `419e8e79` the compiler has 458 C# files and 134,591 lines (Binding 60,703; Emission 20,859, of which the toolchain
1,317; Analysis 20,613; Parsing 17,712; Documentation 4,338; Lexing 3,968; Helper 3,472; Core 1,584; root 1,342)
plus 2,335 lines of `.ll.in`. The target is about 84,350 lines from the like-for-like base of 131,271 (relocated
Documentation and toolchain excluded), with a final total cap of 85,850. Line counts include comments and blank
lines; moving code out of the compiler, changing line endings or deleting explanatory comments is not reduction.
