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
| P7–P9 | Mir | Validator, initialization/Move, drop elaboration, runtime lints, effects, borrow checking per body and Semantics case |
| P10 | — | Acceptance (SPEC §23.2.2), which the records of P11's instance collection join |
| P11–P12 | Backend | Instance collection before acceptance: the checked set, its definition- and instance-stage capability check, limits and layout obligations (DECISIONS R5-M41); then shims, layout/ABI and LLVM text written directly from MIR |
| P13 | Build | Native tools, outside the compiler |

## Dependency rules

| Layer | May read | Must not read |
| --- | --- | --- |
| Syntax | Source | Binding |
| Binding | Syntax tree, its own tables | Mir, Backend |
| MIR builder (`Mir/Build`) | `HirTables`, Plans, declaration tables, pure `TyCtxt` functions, syntax children, `SourceRef` | Binding internals, semantic slots on Koto, parent-walking inference |
| MIR analyses | MIR, declaration tables, pure `TyCtxt` functions | Syntax, HIR |
| Backend | MIR, declaration tables, `TyCtxt`, Core's runtime-location text (`Compilation.Locations`), the Testing catalog's symbol column (DECISIONS R5-M18) | HIR, borrow-checker state, syntax |
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
construction or the capability check (until the R5 plan's unit X2, under MIR code generation only, also by the codegen
coverage rows, the sel `build` refusal from K1 to C0b1 included; DECISIONS R5-M18, PD11). A new vocabulary entry or a second Unsupported code is a design change that needs
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
| Phases after Binding | The `Compilation` entry points (`Analyze`, `Analysis`, `Problems`, `ReportAnalysis`, `WriteIr`, `Validate`, `SyntaxOf`), the pipeline switch and, under mir, the code generator selector (DECISIONS R5-M35) | `Core/Compilation.Pipeline.cs` |
| Analysis records | `Problem(Code, SourceRef)`, merged per code and location; until R6-check the MIR analyses' problems stay unpublished on `MirProgram.Problems` (DECISIONS R4-M1) | `Core/Compilation.Pipeline.cs` |
| Runtime locations | `SourceLocationTable` (`Compilation.Locations.Text(SourceRef)`): the `path:line:column` an Abort reports (SPEC 22.5.1), its source's logical name (IMPL 20.1) escaped (IMPL 21.4.2), memoized for the current program (cleared when the source analysis is revoked); the Backend reads it, and which site an Abort reports stays its generator's rule | `Core/SourceLocationTable.cs` |
| Binding facts for MIR | `HirTables` columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and `PlanRow`s keyed by `SyntaxId`; `HirAdjust.Of`, the one conversion from Binding's acquisition vocabularies; `Binding.BareAcquire`, the one bare-acquisition rule; a definition's parts are published only when Binding resolved the definition; `Binding.HasFacts`, the facts each published Op guarantees, which the builder trusts (a node Binding resolved without them is published Error and listed in `HirTables.Broken`, which the builder records as `InternalInvariant_Kd`; DECISIONS R4-M61) | `Binding/HirTables.cs`, `Binding/HirVocabulary.cs`, `Binding/Binding.Hir.cs`, `Binding/Binding.Hir.Adjust.cs`, `Binding/Binding.Hir.Facts.cs` |
| Binding facts for Hover, LSP and tests | `SemanticQuery` (`Compilation.Semantics`) | `Binding/SemanticQuery.cs` |
| Calls | `CallPlan`, reached only through `CallPlan.Of(node, ordinal)`; an omitted default is its `DefaultDecl` | `Binding/CallPlan.cs` |
| Desugared forms | `ExpansionPlan.Of`; the Pattern plans `BoundMatch.Of`, closures `BoundClosure.Of` and enum Case construction `BoundEnumConstruction.Of` | `Binding/ExpansionPlan.cs` |
| Stored shape of a struct or enum | `AdtDef`; `IsCLayout`, whether a struct's C layout validated, written by layout validation for every struct in every pass (DECISIONS R5-M26) | `Binding/AdtDef.cs`, `Binding/Binding.Attributes.cs` |
| Types and substitutions | `TyCtxt` (`Compilation.Types`): interning, `Subst` (`SubstOf`, `Apply`, `Compose`, `Signature`; a closure's context is the interned `Subst` of the instance that closed it, `ClosureContext` and `SubstOfClosure`, DECISIONS R5-M36), implicit `Layer`s, `CaseType`, `Adaptation` (Binding's selection of an explicit adaptation), `Name` for display; `Retains`, a call's retention under the premises its callee's body is checked under, kept until an edit (DECISIONS R4-M51); `Instantiates`, whether a definition's body is generated per closed substitution (it or an enclosing declaration has a Type, Semantics or length slot; a specialization declares none), the null rule of instance keys, kept until an edit (DECISIONS R5-M21, R5-M34); `Erase`, the Origin-erased form of a Type and a substitution, interned, the identity of instance keys, descriptors, glue and layouts (DECISIONS R5-M39) | `Binding/TyCtxt.cs`, `Binding/TyCtxt.Retention.cs`, `Binding/Binding.Retention.cs`, `Binding/TyCtxt.Instances.cs` |
| Effect facts | `TyCtxt.EffectObligations` (who owes `confined`: `EffectObligation` rows), `DispatchBounds` and `CallableBounds` (the bounds a requirement or virtual call and a call through an abstract Callable input may use under a definition's premises; `ItemContract` a requirement item's Contract reference), `Bodyless` (what a function without a body does), `Implementation` (the bodies a call may run), `Published` (an operation's published row) and `Witnesses` (what a requirement runs on a Type that is not abstract, from the witness core), `Copy`, `Sealed`, all under one premise scope per definition, `Binding.EffectPremises` (DECISIONS R4-M23, R4-M28) | `Binding/TyCtxt.Effects.cs`, `Binding/Binding.EffectObligations.cs` |
| Dispatch facts | `Binding.ResolveRequirementWitness`, the one witness rule from a requirement, a Self that is not abstract, the Contract reference the call selected and a premise scope to what the requirement runs (Builtin, Conformance, Property, FixedArrayEntry, IntegerPosition, Refused or None), shared by the CallPlan materializer, which keeps SPEC 8.15's abstract-Self forwarding, and `Witnesses`; the built-in targets take their requirements' signatures once per pass after headers (DECISIONS R5-M22) | `Binding/Binding.RequirementWitness.cs` |
| Semantics cases | `PairBinder` (a body's binders, `CaseProduct`, `CaseBound`); `Binding.CaseLimits`, the one decision of the bound per definition before its parts are bound; `MirCases`, per-case views of one body that is never copied, under which an `Adapt` is the operation its case selects, and `Views`, the views of a callee a call's substitution selects | `Binding/BindingModel.cs`, `Binding/Binding.CaseLimits.cs`, `Mir/MirCases.cs` |
| Library operations | `IntrinsicDisposition.Of`: a Kimigayo body, an `IntrinsicId` or a shim for each `CompilerFunctionKind`; `IntrinsicDisposition.Effect`, its effect class (SPEC 8.11.2); `KimiLibraryCatalog.Row`, an operation's published row `PublishedRow(Runs, On, Destroys, InPlace)` (SPEC 4.9.6, 12.4.3), which `TyCtxt.Published` gives MIR | `Binding/KimiLibraryKinds.cs`, `Binding/KimiLibraryCatalog.cs` |
| MIR storage | `MirBody` struct arrays with interned projection chains, `ProjectedType` the one projection Type rule; `MirProgram`, bodies by `MirDefKey` and `Ordinal`, the body a callee runs (`Callee`), pooled for warm rebuilds | `Mir/MirVocabulary.cs`, `Mir/MirBody.cs`, `Mir/MirProgram.cs` |
| Operands, accesses and effects | `MirDescriptors.Of` rows; `MirUses.ForEach` (of a body, or under a case view with each `Adapt` resolved) and `MirUses.Successors`, the one enumeration the validator and the analyses share | `Mir/MirDescriptors.cs` |
| Reachability | `MirReach`, the one depth-first walk over a body's blocks along runtime edges and, when asked, CheckTarget edges (the validator, A3, the dataflow's reverse postorder); reached blocks and their `Postorder` accumulate until `Clear` | `Mir/MirReach.cs` |
| Type facts in MIR | `MirTypes`: `Destruction` (the one walk of what destroying a value does, each Type once per query; DECISIONS R4-M58), `MayNeedDrop` and `Observes` (over it: whether a destruction may observe what the value holds), `HoldsLoans` (whether a value may hold a Loan; DECISIONS R4-M36), `HoldsRaw`, `IsExclusive`, `SelectsByValue` (a selection reads its receiver's value, else lends its storage; R4-M56), `Agree` (Type agreement modulo Origins), `RefKind` (the mode of a borrow), `PointerCast` | `Mir/MirTypes.cs`, its concerns `Mir/MirTypes.Destruction.cs` and `Mir/MirTypes.Holding.cs` |
| MIR construction | `MirBuilder` (`Compilation.BuildMir`): one body per definition and Closure, built by Op, Adjust and PlaceShape; `Ref`, the one borrow rule (an exclusive borrow on an invocation's preparation path two-phase, an in-place update written through its reservation; DECISIONS R4-M37), whose `Lent` Place a preparation path continues from instead of forming a written exclusive borrow (R4-M54); `Secure`, an operand read where it is evaluated before a later operand that builds code (`Builds`, R4-M55); a built-in selection's guard where it locates storage, its receiver checked by A1 at the guard and at each access under the selection's subject (R4-M56); `Set`, the one assignment, every node's own value built also where only the checks continue, nothing built for the Never value (assumed completion; R4-M62), and a literal whose Type holds a Never element a located `Unsupported_Kd` until PLAN G129 (R4-M59); one `Adapt` per case adaptation; one drop tree per transfer target; one template per default; `Unsupported(node)`, the one Unsupported record with an Opaque value | `Mir/Build/MirBuilder*.cs` |
| MIR validation | `MirValidator`, the one structural and Type verifier; a finding is `InternalInvariant_Kd` | `Mir/MirValidator.cs`, its Type tier `Mir/MirValidator.Types.cs` |
| MIR analyses | `MirAnalysis` (`Compilation.BuildMir`): the analyses of one program version, once per version after build and validation, in the pass order its contract fixes, steps 1 and 2 (per body) in `Body`, steps 3 and 4 (over the program, A5 with its own loop over the bodies) in `Run` (DECISIONS R4-M2a) | `Mir/Analysis/MirAnalysis.cs` |
| Runtime lints (A3) | `MirLints`: a require failure body whose end a runtime path from its entry reaches (`MirBody.MustNotComplete`) is `RequireFallthrough_Kd` (DECISIONS R4-M7a) | `Mir/Analysis/MirLints.cs` |
| MIR dataflow | `MirDataflow`: bit-set states stored at flow heads (chains of blocks), each chain's phase decided once per body (P1 where the runtime edges from the entry reach it, else P2), the transfer's join (A1 union); forward solves, P1 over runtime edges and P2 over the heads only the checks reach with P1 frozen, and backward solves, a P1 chain joining its runtime successors only; `Replay` and `ReplayBackward` for the checks and later readers, `Before` a head's backward state; one arena row per head for a backward and a forward problem, the analyses' one arena Resource limit; a solve's worklist takes the pending head first in reverse postorder forward and postorder backward (DECISIONS R4-M20, R4-M15a, R4-M34, R4-M65) | `Mir/Analysis/MirDataflow.cs` |
| Move paths | `MirMoveData`: a body's move paths keyed by (parent, step kind, argument) and numbered in pre-order, so a subtree is a range; `Find` gives a Place's path (DECISIONS R4-M21) | `Mir/Analysis/MirMoveData.cs` |
| Initialization and moves | `MirInit` (A1): MaybeUninit, MaybeMoved and EverInit, and for A2 MaybeInit and MaybePartial, per move path and Semantics case view, a runtime index's receiver reported under its selection's subject, at its guard and at its accesses (DECISIONS R4-M21, R4-M6a, R4-M56) | `Mir/Analysis/MirInit.cs` |
| Drop elaboration (A2) | `MirDrops`: per Drop the runtime edges reach and view class a verdict (Keep, Elide, Flag, Open), the drop flags and their set and clear points in `MirProgram.Drops` (`MirDropTable`, a Drop's group by `Group`), read by R5, A4, A5 and the dump; an incomplete layer with its own `drop` is `MovedPlace_Kd` (DECISIONS R4-M6a) | `Mir/Analysis/MirDrops.cs` |
| Effect summaries | `MirEffects` (A4): per body view the classes and atoms of the effect summary, composed through each call's substitution, one worklist to the least fixed point, and the `confined` obligations Binding publishes judged per view (DECISIONS R4-M22); `Keys`, the mutable static Fields a Call or a Drop may access through what it runs (P5, DECISIONS R4-M38) | `Mir/Analysis/MirEffects.cs`, `Mir/Analysis/MirEffects.Solve.cs`, `Mir/Analysis/MirEffects.Keys.cs` |
| Loan stratum | `MirBorrows` (A5): per body view, holders (root locals that may hold a Loan, and E) carrying `MirLoanSets` nodes, a Loan per non-raw Ref site past no shared Deref, holder liveness backward and carrying forward over `MirDataflow`, call retention from `TyCtxt.Retains`, the conflict check by holder with transit and the closure, call reservations: activation at a call or the writeback, the route rule and destroying calls, and a guard candidate's guard-local Origin, a marker its EndBorrow checks (DECISIONS R4-M57) | `Mir/Analysis/MirBorrows.cs`, `Mir/Analysis/MirBorrows.Liveness.cs`, `Mir/Analysis/MirBorrows.Flow.cs`, `Mir/Analysis/MirBorrows.Checks.cs`, `Mir/Analysis/MirBorrows.CheckSets.cs`, `Mir/Analysis/MirBorrows.Activations.cs` |
| Loan sets | `MirLoanSets`: A5's sets of Loan and marker ids as interned ascending runs in one pooled array, a set's id its identity (0 the empty set); memoized `Add`, `Union`, `Remove` and `Range` (a Move Path subtree's run), with a `Scanned` count for `Work` (DECISIONS R4-M53) | `Mir/Analysis/MirLoanSets.cs` |
| MIR analysis problems | `MirReport`: one `MirProblem(Code, Subject, Views)` per code and subject with its views merged and its first producer's facts (`MirFact`); the one home of the derived-problem rules (DECISIONS R4-M3a, R4-M10) | `Mir/Analysis/MirReport.cs` |
| MIR text | `MirDump.Write`, with A2's verdicts and flags on an analyzed program, compared by the dump goldens and the MIR snapshot | `Mir/MirDump.cs`, its operations `Mir/MirDump.Operations.cs` and drop elaboration's output `Mir/MirDump.Drops.cs` |
| LLVM module text | `LlvmModuleText`, the module frame every generator writes through: header with the pooled constants, runtime template with its ABI splice and test variant, external declarations, overflow and memory declarations, footer, external symbol spelling, number formatting; `LlvmConstantPool`, a module's private UTF-8 constants (DECISIONS R5-M14) | `Backend/LlvmModuleText.cs`, `Backend/LlvmConstantPool.cs` |
| Runtime ABI and representation records | `RuntimeAbi`, the runtime ABI: Unit, string and string-handle representations, release kind, zero-sized buffer address, one `FunctionAbi` per runtime routine and the `RuntimeDefinitions` `LlvmModuleText` splices; `AbortReasons`, the closed abort reason table and its template expansion; the records `TypeLayout`, `ValueLowering`, `AbiParameter` and `FunctionAbi`, which take no Type (DECISIONS R5-M15) | `Backend/RuntimeAbi.cs`, `Backend/AbortReasons.cs`, `Backend/AbiRecords.cs` |
| Scalar arithmetic text | `LlvmScalarText`, the LLVM text of scalar arithmetic, numeric conversions and their checks every generator writes through: instructions, overflow intrinsics, the division, remainder and shift checks with IMPL 21.3.3's literal −1 rules and SPEC 13.3.1's divisor substitution, the plain, range-checked, floating-narrowing and position conversions, and each check's own failure block; its stack-only inputs `ScalarOperation`, `ScalarConversion` with `ScalarBounds`, and `ScalarCheck` (DECISIONS R5-M17) | `Backend/LlvmScalarText.cs` |
| Storage layout | `Layout.Of`, the size, alignment, stride, Field offsets and LLVM representation of a closed Type for every generator, as a `Layout.Result` (OK, Limit or CLayout): IMPL 21.1.3's rows (scalars, Unit, string, Never, borrows and raw pointers, object and function handles) and the Slice, Array and Dictionary handles; struct (`Loan<T>` among them), Tuple, fixed-array, enum, Closure and Function Item placement, one shared record per representation; the old pool and `WindowsLowering.GetValue`'s scalar-value gate read it (DECISIONS R5-M19, R5-M29) | `Backend/Layout.cs`, `Backend/Layout.Placement.cs` |
| Capability | `Capability.Code(gap)`: the one table of forms and instances the backend does not generate on windows-x64 (IMPL 21.3.3), the capability check of "Closed vocabularies"; its rows `StorageNesting` (PLAN G128) and, until K5, `EnclosingInstance` (DECISIONS R5-M40) | `Backend/Capability.cs` |
| Instance collection | `InstanceCollector` (`Compilation.BuildMir` under MIR code generation, until G2): the checked set of a program version, `CheckedSet`, one item per `MonoKey` over `Instantiation(MirDefKey, Subst?)`, the roots (the closed bodies of the root modules) first, each item's targets in location order with their runtime bits, from per-body templates of the sites that may carry a target, replayed under the item's representative; one `Admit` with X19's limits and the storage-nesting gap, located by one rule, its records in `CheckedSet.Records` (DECISIONS R5-M40); `IntrinsicRows`, the Backend's row per `IntrinsicId`, whose `Target` column names the target-bearing intrinsics (DECISIONS R5-M35) | `Backend/Instances/InstanceCollector.cs`, `Backend/Instances/InstanceCollector.Admit.cs`, `Backend/Instances/CheckedSet.cs`, `Backend/Instances/Instantiation.cs`, `Backend/IntrinsicRows.cs` |

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
violations are fixed, except syntax-class decisions in the MIR builder, which reach 0 by the end of R6: a Koto type test
or `KotoKind` dispatch that chooses an operation no published `HirOp` decided (an operator token or form read under its
published `HirOp`, and a cast to read a child, are syntax data; DECISIONS R5-M10). Review checks these limits against the existing source and recorded exceptions;
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
| MIR dispatch | No Koto type test or KotoKind dispatch in Mir/Build after R6 that chooses an operation no published `HirOp` decided; remove existing uses by that stage. Reading an operator token or form under its published `HirOp`, and a cast to read a child, are syntax data (DECISIONS R5-M10). |
| Transitional declarations | Keep a removal stage and reason; the stage ledger governs when removal is due. |
| Catch boundaries | No unfiltered catch-all outside CLI, LSP and test-runner boundaries. |
| Member and file structure | Follow the parameter, statement, nesting, logical-chain and token limits above. |
| Density | At most 160 UTF-16 units per physical line, excluding whole multiline-string lines; no nested conditional expression. |
| Deconstruction | Computed tuple literals do not initialize all-new-local deconstructions; existing-variable assignments remain allowed. |
| Unused code | Remove unused/unread private members and unused parameters while preserving behavior. |
| LINQ | No System.Linq.Enumerable in the Backend (`Backend/`, since R5's unit P0; DECISIONS R5-M14); Mir analyses follow at R5 (below); tests are exempt (owner, 2026-10-18, DECISIONS R5-M13). |

Existing excess cannot grow. Only statement and nesting limits admit `CodeRuleException`, with an explicit
bound and reason. `Transitional` records a removal obligation; neither attribute is an analyzer certificate.
New/increased exceptions and later removal stages need owner approval recorded in the commit. Reduced bounds
and earlier stages do not. Mechanical evasions fail review. At a stage exit, record measurements and review
exceptions and transitional declarations with the owner; the deleted Report command is not a gate.

At R7, Syntax's restriction extends to the whole Binding layer (moved from R6, DECISIONS R5-M9), and UnwrapParentheses
is restricted to Parsing/Binding. At R5, the no-LINQ rule extends from the Backend to Mir analyses, rewriting existing uses
first. IntrinsicId returns to 30 at R8.

## Rules during the migration

- **Freeze (D5).** From R0 until R6 completes (Emission until R6-gen, Analysis until R6-check; DECISIONS R4-M35), the old
  Analysis and Emission paths receive no new accepted forms or generation features. Allowed: deletion, miscompile fixes
  mirrored on the new path, the mechanical follow-up that a stage of the plan names, and the owner's exception for the Place
  result removal (DECISIONS PR-1).
- **Size.** Code size has no caps, stage ceilings or forecasts (DECISIONS Q5). Each compiler commit states its net size
  change per area, and stage exits record area sizes (AGENTS). `ArchitectureRulesTest` retains its no-growth ratchets.
- **Missing facts (R3–R5).** Binding publishes a fact the MIR side lacks as a small view; Mir never re-derives it.
- **Difference ledger (R4–R6).** Each difference between the paths is one row in `artifacts/verify/mir-diff`, classified
  as (a) an old-path error, (b) a new-path error, fixed, (c) a SPEC ambiguity, clarified, (d) an intended change (D4 and
  the like) with its clause and plan Appendix B item, or (e) a form not ported under D2, recorded in STATUS and PLAN. The
  ledger is information (DECISIONS R4-M35, A5 plan §8): until classified (a), (c) or (d) with a SPEC clause, a row where
  the old path rejects and MIR accepts, other than a MIR-only relaxation, and a rejection of a library or milestone program
  block the R4 exit; location and code-choice differences do not. The owner is consulted after two consecutive working
  sessions on the A5 plan that land no unit.
- **Temporary structures**, with the stage that removes them: the `KIMI_PIPELINE` switch (R6); the Koto semantic
  forwarding properties (R7, their Parsing and root-helper reads at R6-check; DECISIONS R5-M9); the HIR publisher walk, replaced by inline writes (R7, DECISIONS R2b-D2); the temporary
  intrinsics `Capacity`, `FormatBuffer`, `FormatHint` and `FormatFinish` that the owner admitted on 2026-10-11
  (`IntrinsicId` limit 34, back to 30 in R8; DECISIONS R3-9, R3-10); the `Compilation.Analyzed` event, the difference
  ledger's observation point, and `Binding.IsEffectRecord`, the ledger's old-side effect subset (R6, DECISIONS R4-M17, R4-M23);
  `LegacyRuntimeHelpers`, the Backend's Array, Dictionary, formatting and string-comparison runtime text and its requests
  (R8, DECISIONS R5-M16); `Layout`'s Slice, Array and Dictionary handle word counts (R8, DECISIONS R5-M19). R5's planned
  temporaries, recorded by their first units (DECISIONS R5-M18): the `KIMI_CODEGEN` selector, the pending ledger, the `CodegenDeviations` catalog and `.deviation` targets (G4); the codegen coverage rows (the R5
  plan's unit X2); `MonoKind.LegacyRow` (R8); the out-string `WriteIr`/`Validate` adapters (C0a to G9b); the sel `build` refusal
  (K1 to C0b1); Binding's eager C-layout instance check beside `Layout.Of`, with K10b's double report under sel (G2b); the legacy comparison composition (A-12 or R7); the F2c Dictionary-storage view (R8); the
  `Binding.ClosureCall` bridge (R6-gen); the old generator's specialization view `TyCtxt.InstantiatesInLegacy` and the class
  `InstanceClass.GenericThroughSpecialization` it reads (G7, DECISIONS R5-M34). Transitional attributes identify these declarations (CR-A7). The lasting HIR publication/version
  contract is unmarked; only its publisher walk and private state disappear at R7.
- **Performance and allocation.** Generated native code may not regress in O2 run time or allocation counts (plan §18
  P1, P2); O0 run time and compiler processing time may, when recorded (P1, P3). A timing regression exceeds the
  variation of repeated matched runs (P4). Compiler allocations follow the owner's decision of 2026-10-11 (AGENTS,
  `8071549f`), which supersedes plan §18 P11 and §14-8: an allocation is allowed when it makes equivalent work faster or
  reduces compiler code with a reasonable increase in memory, with time, allocations and retained memory measured and
  recorded. Existing compiler allocation regressions hold until a measured change updates them.
- **R4 and R5 in parallel (DECISIONS R4-M35, A5 plan §9.1).** R5 owns the files it creates and R4 the other files under
  `Mir/Analysis` except the `MirAnalysis` driver; neither stream edits the other's. The rest of `Mir`, the driver
  included, and the `tests/mir` goldens are shared; a test file follows the file it tests. One stream at a time changes a
  shared file: before the unit starts, its planning session records the hold (stream, branch, files) in PLAN §3 on dev
  and removes it in the merge that brings the unit into dev; the other stream merges dev before its next change to that
  file. The same protocol covers these files outside Mir (DECISIONS R5-M7): `Core/Compilation.Pipeline.cs`,
  `Binding/TyCtxt.cs`, `TyCtxt.Effects.cs`, `TyCtxt.Retention.cs`, `BindingModel.cs`, `Binding.ClosureInstances.cs`,
  `Binding.RequirementInstances.cs`, `KimiLibraryCatalog.cs`, `src/Kimi/Checking/Hover/HoverBuilder.cs`,
  `src/Kimi/Diagnostics/DiagnosticRequirement.cs` and `tests/xUnitTest/MirDifferenceLedger.cs`. R5 puts new Binding views
  in new partial files and batches its shared MIR edits into windows (W1, W2 of the R5 plan §3.5), each held the same way.
- **R5 progress (DECISIONS R5-M8; R5 and R6 plan §5.1–§5.2).** M1, the failing tests and fixtures of the pending ledger
  under MIR code generation, only falls and reaches 0 at R5's exit. The owner is consulted after two consecutive R5
  working sessions that land no unit; a unit not landed within two working sessions is re-split or goes to the owner; from
  C0b2, three consecutive C units that do not lower M1 need a stated reason, and a missing input becomes a fact or builder
  unit. An O2 kernel regression beyond the A=B variance blocks its family's merge and is fixed in code generation, never by
  a tolerance.

## Stage ledger

| Stage | State | Scope |
| --- | --- | --- |
| R0 | Done (its timing baselines taken before R5 on 2026-10-17: the O2 corpus and the compiler pipeline series, [O2Corpus.md](../../src/Benchmark/O2Corpus.md)) | Baselines, size and rule ratchets, disposition ledger, narrow-implementation and Advice/Note inventories, freeze |
| R1 | Done 2026-10-10 (closed by user instruction at 129,482 lines, above its 127,000 ceiling) | Delete-first: one Unsupported code, Advice/Note reduction, library validation into tests, dead code, control-flow type flow, OCC-X waiting machinery, independent narrow implementations |
| R2a | Done 2026-10-11 (closed at 126,611 lines, above its 125,000 ceiling, by the stage decision to close at the measured size) | `SyntaxId`, `HirTables` behind forwarding properties, `AdtDef`, unified CallPlan type, pure fits, child-walk and operator-class consolidation, Hover move |
| R2b | Done 2026-10-11 (closed at 127,815 lines, within its 129,000 ceiling; the unused 1,185 lines pass to R3) | `TyCtxt` and `Subst`; the published HIR columns (`Op`, `Res`, `Completion`, `Adjust`, PlaceShape) and their publisher; `CallPlan.Of`, `ExpansionPlan`, the Pattern plans and closure definitions as views; `DefaultDecl` and `PreparedParam`; the pair-binder view; the intrinsic table. The five read-only declaration-table views (`ImplTable`, `VTableDef`, `UniversalRelations`, `EffectBoundTable`, `AvailableBounds`) are deferred to their first readers in R4/R5 |
| R3 | Done 2026-10-11 (closed at 133,004 lines, within its 135,500 ceiling: Mir 4,765, Binding 56,569, Core 1,805, the other areas at their R2b-exit sizes; +5,189 against a forecast of +5,750) | MIR model, descriptor table, validator with its Type tier, dump and builder (shadow): every body of the library and the 43 milestones builds without validator findings, with Opaque values only at Binding-rejected nodes of Milestones 33, 35 and 38; 30 dump goldens. The work paused after R3 by owner instruction |
| R4 | Active; A1–A4 landed; the rest follows the [Complete Polonius A5 Plan](../../draft/Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md) (DECISIONS R4-M33, R4-M35); step 0 (design A3) complete, unit 0b closed by prototype round 10; step 1 active: its 1b design diff decided (A3 §12, DECISIONS R4-M52), unit 1a (`TyCtxt.Retains`) landed; the MIR units and the core lane landed (2026-10-18; A3 E13, E14; R4-M63, R4-M65, R4-M66); next G135's memo, 1b-5 (A5 in the driver, shadow) and 1c | MIR analyses A1–A6 and the difference ledger (shadow): the Binding facts A5 reads published first, then A5 as facts and fixed rules. R4 exits when every family and exit task of the A5 plan is complete, no ledger row blocks and the final end-to-end review passes (A5 plan §6.2, §8). Every analysis unit extends `Mir/Analysis/MirAnalysis.cs`; it and `Mir/MirDump.cs` were restructured by responsibility before the R5 split (DECISIONS R4-M2a, R4-M35) |
| R5 | Active since 2026-10-18, in parallel with R4 under its own planning session, following the [R5 and R6 Implementation Plan](../../draft/Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md) (DECISIONS R4-M35, R5-M1, R5-M2); exit gate: that plan's §5.3 | Monomorphization, shims, layout/ABI, code generation, capability table (shadow); MIR holds the bodies of overrides no instance applies, which generation reads applicability to skip (DECISIONS R4-M28) |
| R6 | Pending; two stages (DECISIONS R4-M35): R6-gen after R5, R6-check after R4 and R6-gen; units and exit gates in the R5 and R6 plan §4.9–§4.10 and §5.4–§5.5 (DECISIONS R5-M1) | R6-gen switches code generation to MIR and deletes the old Emission; R6-check switches acceptance to MIR and deletes the old Analysis, keeping `AnalyzeInstance` as a check-only step until then. An R6 item that only Emission concerns belongs to R6-gen, any other to R6-check. Unit a publishes the MIR problems and folds the MIR construction records, which keep their own merge in `MirProgram` until then, into `MirReport` (DECISIONS R4-M3a). TransferRequired moves to Binding/HIR (R4-M4), which makes A1's StaticMovePathRequired at the bare acquisitions it rejects derived. Deleting the old effect checks ends `Binding.EffectObligations`' reading of their violation tables (`effectViolations`, `virtualEffectViolations`): an obligation then follows the conformance's and the function's own validity (DECISIONS R4-M23); the old twins of A4's rules go with them: `EffectSummary.Function`'s foreign-function branch (beside `TyCtxt.Bodyless`), its formatting-callback and composite-comparison branches and the Dictionary selection and literal comparison of the old walk (beside the published rows and `TyCtxt.Witnesses`), with the `Formatting` effect class only they read, and `CallableEffectFailure`'s decision of a Closure's owner acquisition (beside `AddCallableObligation`'s `Owner`); the Array `append`/`insert`/`pop`/`remove` rows join the catalog (DECISIONS R4-M28) |
| R7 | Pending | Binding consolidation: resolver, inference context, Contract solver, CallPlan pipeline, Problem records. The Binding facts A5 and R5 read are published before them, in R7's target shape, and leave R7's scope (A5 plan §7, DECISIONS R4-M35) |
| R8 | Pending | Library algorithms in Kimigayo, about 28 intrinsics |
| R9 | Pending | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation |
