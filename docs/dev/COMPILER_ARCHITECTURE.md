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

KR1001 and KR1002 enforce the current dependency/member rows through `src/Kimi/CodeRules.globalconfig`. Layers follow
the longest source-path prefix; `Compiler/*.cs` means direct files only. Every partial declaration contributes its
layer. References use resolved types and member owners, including aliases, inferred receivers and attributes.
The explicit MIR builder allow list names Binding's published types, including BoundArgumentOperation returned by
CallPlan (CR-A3); a new view joins with a reason in its commit. Outside `Mir/Build`, syntax, HIR and plans are forbidden.
`SemanticsKind` lives in Misc and needs no layer exception. Syntax forbids the Binding type until R6 broadens that
restriction to the whole layer. Global forbidden member names cover declarations too; typed member selectors cover
their type family, including overrides and derived Koto slots.

## Closed vocabularies

MIR has 5 statement, 10 rvalue, 6 terminator (plus the `CheckTarget` attribute), 7 projection, 4 operand and 5 callee
kinds; the HIR vocabularies and `IntrinsicId` have fixed limits too (KR1003, configured in CodeRules.globalconfig). Enum
aliases count separately; implicit fields do not. A removed configured enum produces KR9002 for removal from config. A
form not yet implemented has one diagnostic code, the location-only `Unsupported_Kd`, reported by Binding, MIR
construction or the capability check. A new vocabulary entry or a second Unsupported code is a design change that needs
a SPEC basis and owner approval.

## Adding a feature

Once R6 makes MIR the only path, a new form adds one `HirOp` or Plan row, one MIR builder function and, if needed, a
descriptor, operator or intrinsic table row. Analyses and code generation change only for a new vocabulary entry. Until
then the freeze below applies to the old paths.

## Canonical components

Each concern has one canonical form. Use and extend it; a different form for the same concern replaces the existing one
in the same unit. Rows give the responsibility and entry point; details live in the code and CODEMAP. From Code Rules
Analyzer A1, each entry file begins with the component's contract (AGENTS). Paths are under `src/Kimi/Compiler`.

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
| Types and substitutions | `TyCtxt` (`Compilation.Types`): interning, `Subst` (`SubstOf`, `Apply`, `Compose`, `Signature`), implicit `Layer`s, `CaseType`, `Adaptation` (Binding's selection of an explicit adaptation), `Relate` (a value fit: its structural part and its Origin relations, SPEC 15.3.5, 15.6.1), `ComponentVariance` and `InvariantTarget` (SPEC 15.3.5's one component rule), `Clauses` (a callee's declared relations under a call's substitution), `Callable` (a callable value's signature), `Name` for display | `Binding/TyCtxt.cs`, `Binding/TyCtxt.Relations.cs` |
| Effect facts | `TyCtxt.EffectObligations` (who owes `confined`: `EffectObligation` rows), `DispatchBounds` and `CallableBounds` (the bounds a requirement or virtual call and a call through an abstract Callable input may use under a definition's premises; `ItemContract` a requirement item's Contract reference), `Bodyless` (what a function without a body does), `Implementation` (the bodies a call may run), `Published` (an operation's published row) and `Witnesses` (what a requirement runs on a Type that is not abstract), `Copy`, `Sealed`, all under one premise scope per definition, `Binding.EffectPremises` (DECISIONS R4-M23, R4-M28) | `Binding/TyCtxt.Effects.cs`, `Binding/Binding.EffectObligations.cs` |
| Semantics cases | `PairBinder` (a body's binders, `CaseProduct`, `CaseBound`); `Binding.CaseLimits`, the one decision of the bound per definition before its parts are bound; `MirCases`, per-case views of one body that is never copied, under which an `Adapt` is the operation its case selects, `Follows` whether a step follows a reference, and `Views`, the views of a callee a call's substitution selects | `Binding/BindingModel.cs`, `Binding/Binding.CaseLimits.cs`, `Mir/MirCases.cs` |
| Library operations | `IntrinsicDisposition.Of`: a Kimigayo body, an `IntrinsicId` or a shim for each `CompilerFunctionKind`; `IntrinsicDisposition.Effect`, its effect class (SPEC 8.4.10.2); `KimiLibraryCatalog.Row`, an operation's published row `PublishedRow(Runs, On, Destroys)` (SPEC 4.7.5, 12.3.4), which `TyCtxt.Published` gives MIR | `Binding/KimiLibraryKinds.cs`, `Binding/KimiLibraryCatalog.cs` |
| MIR storage | `MirBody` struct arrays with interned projection chains, `ProjectedType` the one projection Type rule; `MirProgram`, bodies by `MirDefKey` and `Ordinal`, the body a callee runs (`Callee`), pooled for warm rebuilds | `Mir/MirVocabulary.cs`, `Mir/MirBody.cs`, `Mir/MirProgram.cs` |
| Operands, accesses and effects | `MirDescriptors.Of` rows; `MirUses.ForEach` (of a body, or under a case view with each `Adapt` resolved) and `MirUses.Successors`, the one enumeration the validator and the analyses share | `Mir/MirDescriptors.cs` |
| Reachability | `MirReach`, the one depth-first walk over a body's blocks along runtime edges and, when asked, CheckTarget edges (the validator, A3, the dataflow's reverse postorder); reached blocks and their `Postorder` accumulate until `Clear` | `Mir/MirReach.cs` |
| Type facts in MIR | `MirTypes`: `Destruction` (the one walk of what destroying a value does), `MayNeedDrop` and `Observes` (a destruction that may observe an Origin, SPEC 15.6.6) over it, `HoldsRaw`, `IsExclusive`, `Agree` (Type agreement modulo Origins, `TyCtxt.Relate`), `RefKind` (the mode of a borrow), `PointerCast`, `Parameter` (the Types a call passes and gives, the validator's and the analyses' one rule), `Component` (the Type an aggregate's operand takes) | `Mir/MirTypes.cs` |
| MIR construction | `MirBuilder` (`Compilation.BuildMir`): one body per definition and Closure, built by Op, Adjust and PlaceShape; `Ref`, the one borrow rule; `Set` (a node's own value, assigned where the node completes) and `Make` (a part made where it is built, before later parts that may not complete), the two assignments; one `Adapt` per case adaptation; one drop tree per transfer target; one template per default; `Unsupported(node)`, the one Unsupported record with an Opaque value | `Mir/Build/MirBuilder*.cs` |
| MIR validation | `MirValidator`, the one structural and Type verifier; a finding is `InternalInvariant_Kd` | `Mir/MirValidator.cs`, its Type tier `Mir/MirValidator.Types.cs` |
| MIR analyses | `MirAnalysis` (`Compilation.BuildMir`): the analyses of one program version, once per version after build and validation, in the pass order its contract fixes (DECISIONS R4-M2a) | `Mir/Analysis/MirAnalysis.cs` |
| Runtime lints (A3) | `MirLints`: a require failure body whose end a runtime path from its entry reaches (`MirBody.MustNotComplete`) is `RequireFallthrough_Kd` (DECISIONS R4-M7a) | `Mir/Analysis/MirLints.cs` |
| MIR dataflow | `MirDataflow`: forward bit-set states stored at flow heads (chains of blocks), union join, P1 over runtime edges and P2 over the heads only the checks reach with P1 frozen, `Replay` for the checks and later readers; `Backward` (liveness: a chain P1 reached joins its runtime successors, any other every successor), states stored at head entries (`AtEntry`); `Index`, the analyses' one table builder; the analyses' one arena Resource limit, forward and backward together (DECISIONS R4-M20, R4-M15a, R4-M24) | `Mir/Analysis/MirDataflow.cs` |
| Move paths | `MirMoveData`: a body's move paths keyed by (parent, step kind, argument) and numbered in pre-order, so a subtree is a range; `Find` gives a Place's path (DECISIONS R4-M21) | `Mir/Analysis/MirMoveData.cs` |
| Initialization and moves | `MirInit` (A1): MaybeUninit, MaybeMoved and EverInit, and for A2 MaybeInit and MaybePartial, per move path and Semantics case view (DECISIONS R4-M21, R4-M6a) | `Mir/Analysis/MirInit.cs` |
| Drop elaboration (A2) | `MirDrops`: per Drop the runtime edges reach and view class a verdict (Keep, Elide, Flag, Open), the drop flags and their set and clear points in `MirProgram.Drops` (`MirDropTable`, a Drop's group by `Group`, each row with its move path), read by R5, A4, A5 and the dump; an incomplete layer with its own `drop` is `MovedPlace_Kd` (DECISIONS R4-M6a) | `Mir/Analysis/MirDrops.cs` |
| Effect summaries | `MirEffects` (A4): per body view the classes and atoms of the effect summary, composed through each call's substitution, one worklist to the least fixed point, and the `confined` obligations Binding publishes judged per view (DECISIONS R4-M22) | `Mir/Analysis/MirEffects.cs`, `Mir/Analysis/MirEffects.Solve.cs` |
| Regions and Loans (A5-1) | `MirBorrows`: per body and Semantics case view, holders (definition, cell) on the move paths, the flow graph of actual value flows (`TyCtxt.Relate`, `TyCtxt.Clauses`, `MirTypes.Parameter`), liveness backward and the active Loans forward by exact counts; `Replay` gives each location the Loans active before it (DECISIONS R4-M24) | `Mir/Analysis/MirBorrows.cs`, `.Regions.cs`, `.Liveness.cs`, `.Flows.cs` |
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
violations are fixed, except syntax-class decisions in the MIR builder, which reach 0 by the end of R6. The analyzer
(units A0–A8, before R4) enforces these limits as build errors over a shrinking baseline.

## Code rules

The owner authorized intake and implementation of the Code Rules Analyzer on 2026-10-12 (DECISIONS CR-A0).
The following rules are enforced by A2–A7; A8 enforces commit approval comparison.
Kimi.Analyzers owns C# rules for `src/Kimi`; compiler behavior, tests, Benchmark and Playground are outside that scope.
Generated code is excluded. Old Analysis/Emission files receive KR1 and the built-in unused-code rules only.

`CodeRulesArchitecture` owns resolved dependency/member/vocabulary policy and structural checks through the shared
analyzer pipeline. Its .Structure concern implements KR1004–1006 and KR1008 (CR-A4): collection declarations use
declared symbols, creations use their resolved type, and generic constraints have one cycle-safe expansion.
Pattern tests use semantic matched/compared/input types; wrapper patterns and casts add no tests, and a recursive
pattern without a type adds no Koto type test (CR-A9a). Catch anchors exclude the body. Configuration lists use commas: Roslyn treats semicolons as inline comments (CR-A3).
`CodeRulesDensity` owns KR3001–3003 in the same syntax walk (CR-A5). A physical line is excluded only when wholly
inside a multiline literal/text token; interpolation holes remain checked. The outermost conditional before a
callable boundary owns the observation. Parentheses are transparent when classifying new-local tuple values.
`CodeRulesAccretion` owns KR2001–2010 (CR-A6). The one file walker carries declaration measurements as stack values:
local functions are separate; lambdas belong to their member and restart control depth; accessors share their member's
statement count. Parameter/call semantics are resolved only after syntax leaves a possible violation.
`CodeRuleAnnotation` carries exception bounds and transitional removal stages through the same observation merge.
The stage order is R0, R1, R2a, R2b, R3–R9; only a current stage later than removal is overdue. Type/member identities
survive partial-file moves. Report includes every applicable annotation, its reason and bound or stage, even at zero.
IDE0051, IDE0052 and IDE0060 are build errors, configured only in CodeRules.globalconfig.

| Rules | Contract |
| --- | --- |
| KR1001–1003 | Enforce configured dependency/member restrictions and closed vocabularies; A3 preserves existing checks, with no baseline |
| KR1004 | No Koto-keyed dictionary, set or weak table outside Syntax; Binding's baseline reaches zero at R7 exit |
| KR1005 | No boolean `Supports*` method or property |
| KR1006 | No Koto type test or KotoKind comparison/pattern in Mir/Build; baseline reaches zero at R6 exit |
| KR1007 | Transitional declarations name their removal stage and reason; overdue declarations fail |
| KR1008 | No unfiltered catch-all outside CLI, LSP and test-runner boundaries |
| KR2001–2005, KR2010 | Parameter limits above; no failure-text out parameter; source-declared call boolean literals are named |
| KR2006–2009 | Statement, nesting, logical-chain and file-token limits above |
| KR3001–3002 | At most 160 UTF-16 units per physical line (whole multiline-string lines excluded); no nested conditional expression |
| KR3003 | Computed tuple literals do not initialize an all-new-local deconstruction, `var (...)` or `(var a, T b)`; existing-variable assignments are outside this rule |
| IDE0051, IDE0052, IDE0060 | Remove unused/unread private members and unused parameters, preserving behavior; no baseline |

One baseline row identifies a rule, its innermost named Type (file for file-wide records), and a member, local-function,
node or line anchor. Member/file measurements cannot rise above the greater of the ordinary limit and their recorded
value; node/line counts cannot rise. Partial-file moves preserve Type-scoped keys. Obsolete entries must be pruned.
Ordinary Report measurements are over-limit only. A complete Report with no such occurrence makes its baseline row
redundant and Prune deletes it (CR-A2). Annotations are reported even at zero; partial declarations share the maximum
measurement and their single annotation. Unannotated partial measurements at or below the limit stay out of Report.
KR1001–1003 and KR1007 never have baselines; extend their scope only after the newly covered violations reach zero.
KR1002's UnwrapParentheses prohibition expands outside Parsing/Binding at R7 exit.
Syntax's prohibition on the Binding type expands to the whole Binding layer at R6 exit.
At R5, add the Backend layer and forbid System.Linq.Enumerable in Mir analyses and Backend, rewriting existing
Mir LINQ uses first. From R5, the owner decides whether to extend these rules to tests/xUnitTest. R8 restores
IntrinsicId's cap to 30. At every stage exit, obtain a fresh Report for owner review of baseline, exceptions and
transitional declarations; their inventory does not by itself authorize changes.

Only statement and nesting limits admit CodeRuleException, with an explicit bound and nonempty reason; an exception
and baseline cannot cover the same member/rule. Invalid targets/rules, out-of-scope exceptions, duplicates, empty reasons,
needless/exceeded bounds and unknown removal stages are KR9005. Conditional attribute uses are absent from product
metadata. All KR descriptors are NotConfigurable. Enforce reports stale rows as
warnings, Verify as errors, and Report publishes complete observations for scripts; verification checks suppression.
Verify and Report end with the KR9000 completion marker, which verification requires. Effective kimi_code_rules
options must equal CodeRules.globalconfig, and Kimi does not discover other .globalconfig files (CR-A9b).
The analyzer reads each file through the compilation's shared semantic model; KR1 resolves only names that can denote
a forbidden type or member (CR-A9).
Configuration changes, new/increased baseline rows, new/increased exceptions and later removal stages require owner
approval recorded by `Code-Rules-Approved`; pruning and reduced exception bounds/removal stages do not. The adopted
A2–A7 configuration and initial baselines are authorized by the proposal and this intake. Mechanical evasions fail review.

## Rules during the migration

- **Freeze (D5).** From R0 until R6 completes, the old Analysis and Emission paths receive no new accepted forms or
  generation features. Allowed: deletion, miscompile fixes mirrored on the new path, and the mechanical follow-up that a
  stage of the plan names.
- **Sizes.** `CompilerSizeBudgetTest` holds the size caps and `ArchitectureRulesTest` the no-growth ratchets. A stage
  closes only within its exit ceiling (plan §12.2); otherwise work stops and the owner decides, given the measured size,
  the remaining deletion candidates and their cost. When a reduction stage exits, its caps fall to the measured size plus
  2%. A cap rises only on owner instruction or to preserve the native O2 and allocation guarantees (plan §18 P5), with the
  reason recorded. In R2b–R5 the total cap is the stage's cumulative ceiling; A1's converted caps are below.
- **Logical size** (owner, 2026-10-11, DECISIONS Q1). Code Rules Analyzer A1, before R4, switches the caps to
  non-trivia C# tokens, so formatting, comments and blank lines no longer count. Each area's cap becomes its line cap
  times the area's measured tokens per line; the remaining stage ceilings and the final target convert at the
  compiler-wide ratio after `.ll.in` and generated data are taken out; `.ll.in` keeps a line cap; generated files
  (`<auto-generated>` header comments) are excluded. A1 implements this with one scan for all caps.
- **Forecasts.** Each unit states its forecast in non-trivia C# tokens per area (lines for reference); an overrun above 20% is
  consolidated before commit or reported with its reason.
- **Missing facts (R3–R5).** Binding publishes a fact the MIR side lacks as a small view, at most 200 non-trivia Binding
  tokens per unit (DECISIONS R4-M30); Mir never re-derives it.
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
| R4 | Active | MIR analyses A1–A6 and the difference ledger (shadow). Every analysis unit extends `Mir/Analysis/MirAnalysis.cs`; its three-consecutive-units restructuring is deferred to the R4 exit (DECISIONS R4-M2a). `Mir/MirDump.cs` changed in three consecutive units (U0c, U4, U5): its next change restructures it first |
| R5 | Pending | Monomorphization, shims, layout/ABI, code generation, capability table (shadow); MIR holds the bodies of overrides no instance applies, which generation reads applicability to skip (DECISIONS R4-M28) |
| R6 | Pending | Switch the default to MIR, then delete the old Analysis/Emission paths. Unit a publishes the MIR problems and folds the MIR construction records, which keep their own merge in `MirProgram` until then, into `MirReport` (DECISIONS R4-M3a). TransferRequired moves to Binding/HIR (R4-M4), which makes A1's StaticMovePathRequired at the bare acquisitions it rejects derived. Deleting the old effect checks ends `Binding.EffectObligations`' reading of their violation tables (`effectViolations`, `virtualEffectViolations`): an obligation then follows the conformance's and the function's own validity (DECISIONS R4-M23); the old twins of A4's rules go with them: `EffectSummary.Function`'s foreign-function branch (beside `TyCtxt.Bodyless`), its formatting-callback and composite-comparison branches and the Dictionary selection and literal comparison of the old walk (beside the published rows and `TyCtxt.Witnesses`), with the `Formatting` effect class only they read, and `CallableEffectFailure`'s decision of a Closure's owner acquisition (beside `AddCallableObligation`'s `Owner`); the Array `append`/`insert`/`pop`/`remove` rows join the catalog (DECISIONS R4-M28) |
| R7 | Pending | Binding consolidation: resolver, inference context, Contract solver, CallPlan pipeline, Problem records; Binding's variance and callable-signature spellings converge on `TyCtxt.Relate`, `ComponentVariance` and `Callable` (DECISIONS R4-M9b) |
| R8 | Pending | Library algorithms in Kimigayo, about 28 intrinsics |
| R9 | Pending | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation |

## Baseline

A1 (2026-10-12, DECISIONS CR-A1) fixes the conversion at 835,421 tokens / 128,885 non-generated C# lines.
Each area cap is rounded upward from its former line cap times its measured ratio; Helper first subtracts the
1,687 generated lines. Total/stage conversions subtract 2,335 template and 1,687 generated lines before applying the
whole-compiler ratio. No area's pass/fail state changes. EOF, missing tokens and trivia do not count.

| Area | A1 tokens | Token cap |
| --- | ---: | ---: |
| Analysis | 121,620 | 124,047 |
| Binding | 371,765 | 432,000 |
| Core | 8,055 | 15,000 |
| Documentation | 24,624 | 25,687 |
| Emission | 160,848 | 164,057 |
| Helper | 7,766 | 8,068 |
| Lexing | 19,065 | 19,445 |
| Mir | 38,804 | 155,000 |
| Parsing | 75,668 | 77,163 |
| Root | 7,206 | 7,345 |

The owner approved the R4 completion and conditional coexistence envelope (DECISIONS R4-M31, superseding R4-M25's
numeric caps and 90,000 Mir planning trigger for this scope). Reforecast at each selected design and landing; exceeding
an area or stage envelope requires consolidation or renewed owner approval. The per-unit 20% overrun rule and R4-M30
Binding exceptions remain. R5 and later ceilings are unchanged; reforecast R5 before its implementation.

LLVM templates remain at 2,335 lines against their unchanged cap of 2,381, outside the C# total.

| Remaining stage | Total C# token ceiling |
| --- | ---: |
| R4 | 1,020,000 |
| R5 | 942,976 |
| R6 | 654,531 |
| R7 | 589,712 |
| R8 | 567,025 |
| R9 | 530,402 |
| Final target (formerly 84,350 lines) | 520,679 |

The historical line baseline and target below are retained as the inputs of the adopted reduction plan.

At `419e8e79` the compiler has 458 C# files and 134,591 lines (Binding 60,703; Emission 20,859, of which the toolchain
1,317; Analysis 20,613; Parsing 17,712; Documentation 4,338; Lexing 3,968; Helper 3,472; Core 1,584; root 1,342)
plus 2,335 lines of `.ll.in`. The target is about 84,350 lines from the like-for-like base of 131,271 (relocated
Documentation and toolchain excluded), with a former final total cap of 85,850. These historical line counts include
comments and blank lines; moving code out of the compiler, changing line endings or deleting explanatory comments is
not reduction.
