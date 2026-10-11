# Compiler architecture

The compiler reduction plan ([draft](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md))
was adopted on 2026-10-10. This document holds the enforced rules and the stage ledger; the plan holds the design,
rationale and decisions (its §18). Required behavior remains in SPEC/IMPL, navigation in CODEMAP, verified support in
STATUS, and execution/verification rules in AGENTS and VERIFICATION. The current compiler does not yet have the target
structure below; only completed ledger entries describe enforced changes.

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

## Closed vocabularies

MIR has 5 statement kinds, 9 rvalue kinds, 6 terminator kinds (plus the `CheckTarget` terminator attribute), 7 projection
kinds, 4 operand kinds and 5 callee kinds (`ArchitectureRulesTest.VocabularyDoesNotGrow`). Not-yet-implemented forms have one diagnostic code, `Unsupported_Kd`, carrying only a
location, reported by Binding, MIR construction or the capability check. Adding a vocabulary entry or a second
Unsupported code is a design change that needs a SPEC basis and owner approval.

## Rules during the migration

- From R0 until R6 completes, the old Analysis and Emission paths receive no new accepted forms or generation features
  (decision D5). Allowed: deletion, miscompile fixes mirrored on the new path, and the mechanical follow-up that a stage
  of the plan names.
- A form that no general rule handles reports the located Unsupported. Do not add `Supports*`-style gates, shape
  allowlists, Advice or downstream re-verification.
- `CompilerSizeBudgetTest` and `ArchitectureRulesTest` hold the size caps and the no-growth ratchets below; caps fall
  when a reduction stage exits. A cap rises only on owner instruction, or to keep O2 performance and zero allocation
  (plan §18 P5), with the reason recorded. In the parallel stages R2b–R5 the total cap is the stage's cumulative ceiling
  (plan §10.4, §12.2): R3's is 135,500, with a Mir cap of 6,600, Binding 350 lines over its R2b-exit size and Core
  1,820. `ArchitectureRulesTest.MirReadsNoBindingInternals` enforces the MIR builder row above over a frozen list of
  published Binding types, which R3 U1 extended by the builder's gaps (`BuiltinForm`, `CompilerFunctionKind`) and only the
  deferred declaration views join, and keeps Mir off the old Analysis/Emission types; `OnlyTheMirBuilderReadsSyntaxAndHir`
  enforces the MIR analyses row for everything under `Mir/` outside `Mir/Build` (`SemanticsKind`, a Type vocabulary that lives in
  the Parsing namespace, is named there through an alias).
- O2 run time and zero allocation (compiler warm paths and native allocation counts) may not regress; O0 run time and
  compiler processing time may, and are recorded. A regression is a change beyond the variation of repeated runs under
  identical conditions.

## Canonical components

Components the reduction has created, under `src/Kimi/Compiler`. Extend one of these before adding a parallel structure.

| Component | Source | Responsibility |
| --- | --- | --- |
| `ChildSlots` | `Parsing/Koto/Common/ChildSlots.cs` | The one child walk: a node lists its slots once in `ForEachChildSlot`, which visiting, collecting and replacing share |
| `SyntaxTable` | `Parsing/Koto/Common/SyntaxTable.cs` | Per-module dense pre-order `SyntaxId`; a synthetic node takes a later id at its first semantic write; `Module`, the module's ordinal (the source modules in order, the library last) |
| `SourceRef` | `Core/SourceRef.cs` | A syntax node as a value, `(Module, SyntaxId)`, the only way MIR and its records name syntax; `SourceInfo` adds a MIR scope and the statement-start mark |
| Pipeline switch and entry points | `Core/Compilation.Pipeline.cs` | `Compilation.Pipeline` (`CompilerPipeline` Legacy or Mir; the default read once per process from `KIMI_PIPELINE`, R3–R5) and the one way to run and read the phases after Binding and startup: `Analyze`, `Analysis` (`AnalysisResult`), `Problems` (`Problem(Code, SourceRef Subject)`), `ReportAnalysis`, `WriteIr`, `Validate`, `SyntaxOf`. Until R6 they delegate to the old path; under mir `Analyze` builds and validates the MIR first, and its records in the source modules (construction `Unsupported_Kd`, validator `InternalInvariant_Kd`) are analysis Errors, reported in the control-flow partition, that block generation; the mir pipeline marks the check identity (` [mir]`) and the test artifact manifest |
| `HirTables` | `Binding/HirTables.cs` | Semantic columns and `PlanRow`s keyed by `SyntaxId`, behind the Koto forwarding properties until R6; the published `Op`, `Res`, `Completion`, `Adjust` and PlaceShape (`Places`) columns (`HirOp`, `HirRes`, `HirCompletion`, `HirAdjust` = `HirAcquire` + `HirAdaptation`, `HirPlace` = `HirPlaceKind` + implicit layers, in `Binding/HirVocabulary.cs`), empty until `HirTables.Publish`; `HirAdjust.Of` is the one conversion from Binding's acquisition vocabularies, and an explicit conversion's cast is its `ConversionBinding` |
| HIR publisher | `Binding/Binding.Hir.cs` | `HirTables.Publish`: at most once per pass, one child walk fills `Op`, `Res` (`PreparedParam` for a parameter read in a later default), `Completion` (Binding's structural completion with its Never evidence), `Adjust` (each consumed child's acquisition and adaptation from the operation Binding recorded, else `Binding.BareAcquire`, the one bare-acquisition rule that argument adaptation and capture entries also apply; a user index under an update or exclusive borrow of its Place path takes `indexUniq`, which its plan row records), PlaceShape (the Place each evaluated node or borrowed Pattern binding designates and the layers its consumer follows, each a `TyCtxt.Layer`), each jump's target and each number literal's fitted value (`PlanRow.Folded`; a directly signed number is one Literal) in its plan row from facts Binding kept; it returns the pass it describes; replaced by inline writes in R7 |
| `SemanticQuery` | `Binding/SemanticQuery.cs` | `Compilation.Semantics`: the facts Binding publishes to Hover, the LSP and tests |
| `CallPlan` | `Binding/CallPlan.cs` | One plan per call, its callee a `CalleeKind` (Function, Requirement, Virtual or Value); `CallPlan.Of(node, ordinal)` is the one way to reach a node's calls, a formatting plan's included; a Requirement callee is its `Target` with its `RequirementContract`, unless the `Target` is a built-in witness (a `Shim`); an omitted default is its selected declaration's `DefaultDecl` (`DefaultDecl.Of`: the default template's owner, slot and declared parameter Types); `CallPlan.Accessor` is the function executing a custom accessor, which its calls target, and `CallPlan.Parameter` a function's parameter as its body reads it (an accessor's `self` and `value`, at -1 a constructor's or destructor's receiver) |
| `PairBinder` | `Binding/BindingModel.cs` | A pair binder in scope of a body with its admitted set: `PairBinder.Collect(body, list)` lists a body's binders outer to inner and `PairBinder.CaseProduct` counts its Semantics cases |
| `ExpansionPlan` | `Binding/ExpansionPlan.cs` | `ExpansionPlan.Of(node)`: the expansion the MIR builder desugars at an expansion Op (Iteration, Try, Format, DictionaryLiteral, Range, Index, CompoundAssign), naming the calls whose role `CallPlan.Of`'s order leaves open (an index's key resolution and selected element or view call, a Property update's getter and setter), the argument that stands for an operand already evaluated and an interpolation passed directly to `writeLine`; the Pattern plan of a match, try or user loop is `BoundMatch.Of`, a closure's definition `BoundClosure.Of` (the plan's PatternPlan and ClosureDef) and an enum Case value's construction `BoundEnumConstruction.Of` |
| `AdtDef` | `Binding/AdtDef.cs` | The stored shape of a struct or enum on its symbol: Fields, Cases, destructor, inline storage and the Field, base and Case Types of each instance |
| `TyCtxt` | `Binding/TyCtxt.cs` | `Compilation.Types`: the one Type intern table (`Intern`, with the Array, Slice, Dictionary and wrapping-integer canonical forms) and the Types the builder forms (`Reference`, `Pointer`, `Tuple`); `Layer`, the Deref or PairLayer (with its pair binder) a selection follows implicitly; the interned `Subst` of a call, closure or function item (`SubstOf`, `SubstOfClosure`, `SubstOfItem`, `Identity`), applied to a declared Type by `Apply`, which is Binding's storage instantiation until R7, and composed by `Compose` (a call in a body another `Subst` instantiates, as Binding forwards it); `Name`, a Type as diagnostics spell it |
| Intrinsic table | `Binding/KimiLibraryKinds.cs` | `IntrinsicDisposition.Of(kind)`: the one disposition of each `CompilerFunctionKind` (plan 5.8), a Kimigayo `Body` (the declaration's own, or for a compiler function the body R8 writes, which the backend serves from the legacy runtime helpers until then), an `Intrinsic` (`IntrinsicId`, at most 30; `Fail` with its fixed SPEC 22.5.4 code) or a `Shim` (a built-in witness whose body the backend generates); `ExpansionKind` states how an expansion's steps without a call are served; the builder-only rows (R3 decision 5) give the `BuiltinForm` of a node (`FormOf`: a sequence's metadata member, a dynamic Array literal, a built-in slicing, a direct interpolation) and the existing compiler function that serves it (`Serving`), whose declaration is `KimiLibrary.Function` |
| MIR model | `Mir/MirVocabulary.cs`, `Mir/MirBody.cs`, `Mir/MirProgram.cs` | The closed MIR vocabularies (plan 5.4) and the operator, cast, constant, local, scope and body kinds; `MirBody`, one body in struct arrays the builder appends to (locals, `_0` the return and the arguments next; blocks, each a range of statements and its terminator with `CheckTarget`; operand lists; switch pairs; interned constants; interned projection chains `(parent, kind, argument, Type)` shared across roots; scopes; pair binders; static roots; Opaque sites with their roots), whose `ProjectedType` is the one rule for the Type a projection selects; `MirProgram`, the bodies by `MirDefKey` (definition symbol, body kind, index), pooled so a warm rebuild allocates nothing |
| Descriptor table | `Mir/MirDescriptors.cs` | `MirDescriptors.Of`: one row per statement, rvalue, terminator, operand, projection and intrinsic kind, its `MirAccess` (reads, writes, moves, borrows, defines, kills, drop use, case dependence) and `MirEffect`s (static read, write and first-access initialization, raw-holding immutable-static read, integer-to-pointer, environment, abort, allocation, unknown for Opaque), an intrinsic's arity and divergence; `MirUses.ForEach`, the one enumeration of a location's operands, Places (index locals included) and effects by those rows through a struct visitor, and `MirUses.Successors`, a terminator's runtime successors, which the validator and the analyses share |
| MIR dump | `Mir/MirDump.cs` | `MirDump.Write`: the one deterministic text of a body or of a module's bodies, records and findings (Types by `TyCtxt.Name`, `@SyntaxId`, `*` at a statement start), which the dump goldens and the MIR snapshot compare |
| MIR builder | `Mir/Build/MirBuilder*.cs` | `Compilation.BuildMir`: once per Binding pass (`HirTables.Publish`'s version), one pooled `MirProgram`: a body per `HirOp.Body` definition (functions, accessors keyed by `CallPlan.Accessor`, static initializers; a module's top-level statements with no symbol), each validated; a node is built by its Op, Adjust and PlaceShape (`Into`, `Assign`, `Operand` with `Adapt` for each `HirAdaptation`, `Place` with the layers `Follow` takes by `TyCtxt.Layer`, `Call` by `CallPlan` into the five callees, the arguments by parameter slot, the receiver first, an implicit exclusive borrow of a call input two-phase; control flow in `MirBuilder.Control.cs`, a transfer to the target its plan row names with a `CheckTarget` to the code after it, a body's end a runtime or check-only edge by its HIR Completion, an operation whose operand does not complete not performed; scopes in `MirBuilder.Drops.cs`, StorageLive/StorageDead of each Var and Temp local and a Drop where `MirTypes.MayNeedDrop`, registered in its block's, statement's or condition's scope and cleaned up in reverse where the scope is left, a transfer leaving through its target's drop tree whose nodes, defer bodies included, are built once per target and shared), and a form without a rule goes through the one `Unsupported(node)`: a record merged by location on the program and an Opaque value naming the roots its syntax touches; an Error node is a derived Opaque |
| MIR Type facts | `Mir/MirTypes.cs` | `MirTypes.MayNeedDrop`, the one rule by which MIR drops a value (an owning handle, a managed sequence or string, a Function value, a destructor or an abstract Type anywhere in its inline storage), and `MirTypes.HoldsRaw`, whether a static's value holds raw authority (SPEC 8.4.10.2), each one walk of a Type's inline storage |
| `MirValidator` | `Mir/MirValidator.cs` | The one structural verifier of MIR (plan 14-5): structure (terminators, target ranges, `CheckTarget` only on Goto, a Call without a target and Unreachable, every block reached through runtime or check edges), locals, scopes, projections against their base Types (`ProjectedType`), constants, binders, operand ranges, arities, Discriminant and Len Types and Opaque sites; each `MirFinding` names its `MirViolation` and syntax, an `InternalInvariant_Kd`; a warm run allocates nothing |

## Stage ledger

| Stage | State | Scope |
| --- | --- | --- |
| R0 | Done (timing baselines carried to before R5) | Baselines, size and rule ratchets, disposition ledger, narrow-implementation and Advice/Note inventories, freeze |
| R1 | Done 2026-10-10 (closed by user instruction at 129,482 lines, above its 127,000 ceiling) | Delete-first: one Unsupported code, Advice/Note reduction, library validation into tests, dead code, control-flow type flow, OCC-X waiting machinery, independent narrow implementations |
| R2a | Done 2026-10-11 (closed at 126,611 lines, above its 125,000 ceiling, by the stage decision to close at the measured size) | `SyntaxId`, `HirTables` behind forwarding properties, `AdtDef`, unified CallPlan type, pure fits, child-walk and operator-class consolidation, Hover move |
| R2b | Done 2026-10-11 (closed at 127,815 lines, within its 129,000 ceiling; the unused 1,185 lines pass to R3) | `TyCtxt` and `Subst`; the published HIR columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and their publisher; `CallPlan.Of`, `ExpansionPlan`, the Pattern plans and closure definitions as views; `DefaultDecl` and `PreparedParam`; the pair-binder view; the intrinsic table. The five read-only declaration-table views (`ImplTable`, `VTableDef`, `UniversalRelations`, `EffectBoundTable`, `AvailableBounds`) are deferred to their first readers in R4/R5 |
| R3 | Active | MIR model, descriptor table, validator, dump and builder (shadow) |
| R4 | Pending | MIR analyses A1–A6 and the difference ledger (shadow) |
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
