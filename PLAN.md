# Kimigayo Compiler Completion Plan

## 1. Goal and Scope

### Baseline

Complete the Kimigayo compiler for **all settled language rules and implementation contracts in the finalized specification**, preserving existing supported programs and required rejection behavior. Minimize allocations, retained memory, compilation cost, generated work, and execution time where measurements and semantic proofs justify changes. This is the authoritative implementation design and execution record for this effort, not a declaration of compiler completion.

**Planning boundary:** modify only `PLAN.md`. No compiler, test, configuration, specification, example, or `draft/` edits are authorized in this phase. Implementation starts only in a subsequent implementation phase. The repository's `AGENTS.md` applies. NativeAOT tests are excluded; compiling Kimigayo programs with LLVM and executing their Windows binaries is ordinary native execution and is required where indicated.

### Specification authority

- `SPEC.md` identifies Chapters 1–22 and Appendix A as normative. Under §1.3, unqualified declarative rules are normative; examples do not introduce APIs or override rules. Each concept's owning section controls its cross-references.
- Appendix B provides optional algorithms; Appendix F is a non-normative syntax summary. Appendix D identifies boundaries rather than granting syntax or permissions. `STATUS.md`, tests, comments, and this plan describe implementation or proposals, not language authority.
- The dependency/artifact rules are integrated into §§18, 20.8, 21.3.7 and A.16. They are settled work, not deferred simply because implementation is absent.
- `SPEC.md` explicitly supersedes the testing draft's old `$require` return behavior with §17.5: false verification records failure; condition/message temporary cleanup precedes Abort; ordinary enclosing cleanup does not run after Abort.
- The withdrawn Composition Root design grants no Entry/Provider declarations or selection. Built-in `$abort`, `$expect`, and `$require` have independent settled rules.
- No draft proposal is used as an independent acceptance criterion. New discoveries of contradictory normative text must enter §5 before dependent implementation.

### Included and excluded work

Include the requirement families R1–R24 in §4, including specified features currently rejected by the executable subset. Windows x64 is the initial native profile; Library inspection output and common generation from source dependencies are in scope, without a stable external Kimigayo ABI.

Exclude undefined public APIs and deferred extensions in Appendix D. In particular: Composition Entry/Provider; source concurrency; runtime Contract Views and general checked casts; virtual/override dispatch; user-defined arithmetic; string `+`/`+=` acquisition; additional Pattern forms; dynamic Non-Copy element Move; mutable Slice; raw-storage acquisition/safe-reference conversion APIs; public Exchange/Swap spelling; private binary distribution; network registries/version ranges; persistent object-code/generation-plan caches; extra targets; debug information; and NativeAOT. Preserve their specified rejection boundaries. Do not exclude settled object/Weak APIs, collection mutation, interpolation, source packages, or test membership merely because nearby extensions are deferred.

Mods and testing contain settled semantic contracts with undefined external interfaces. Implement independently testable internal semantics, but do not invent the interfaces listed in G1–G2. Their separate specification work is outside this effort; integrated acceptance remains explicitly blocked where it depends on that work. An internal collection representation is an implementation choice, not a requirement to standardize a public collection ABI.

## 2. Execution State

### Current snapshot

| Field | Value |
| --- | --- |
| Last updated | 2026-09-15T20:55:57+09:00 (Asia/Tokyo) |
| Code baseline | `7a83d6bc751f4d02815c3f0b2f1ecfa113672588` |
| Working directory | `C:\Users\bwff1\repos\archi-Doc\Kimigayo` |
| Initial user changes | `git status --short` returned no entries; `PLAN.md` did not exist |
| Changes in this effort | New `PLAN.md` only; no implementation started |
| Planning readiness | `PARTIALLY_READY` |
| Current milestone | M1, proposed first execution milestone; implementation state `TODO` |
| Ready work | M1 evidence/coverage setup; M2 semantic completion; M3 project graph/configuration work. Design can proceed for other unblocked items in dependency order |
| Blocking conditions | G1–G2 interface contracts; G3 managed verification environment; G4 LLVM execution environment; G5 remaining per-rule support audit |
| Current verification | Source inspection and limited environment probes only. No managed tests, fixture generation, LLVM verification, native tests, or benchmarks executed |

Evidence labels throughout: **Verified fact** = directly inspected source/configuration or current command result; **Evidence-based inference** = conclusion from those facts, without current execution; **Proposed change** = future work; **Unverified assumption** = premise still requiring investigation. A current source path is evidence of code, not proof of runtime conformance.

Item states: `TODO`, `IN_PROGRESS`, `IMPLEMENTED_UNVERIFIED`, `DONE`, `BLOCKED`, `NOT_APPLICABLE`. `DONE` requires its acceptance criteria and required verification; neither historical success nor code presence alone qualifies. Verification results use `PASS`, `FAIL`, `NOT_RUN`, `BLOCKED`.

### Progress

| ID | State | Completion evidence or remaining work | Next action |
| --- | --- | --- | --- |
| M1 | TODO | Establish current baseline and complete G5 clause-level accounting | Execute V2–V3 after G3; register existing versus new cases |
| M2 | TODO | R1–R4 semantic completion and numeric conversions | Start I3–I4 with inspected conversion rejection cases |
| M3 | TODO | R18 project graph, locks and semantic-only check | Implement I5–I6 without native generation dependency |
| M4 | TODO | General Origins, Loans, effects and usage legality | Implement I7–I8 after M2 and module identity foundation |
| M5 | TODO | Struct/Property/construction/deinit/static execution | Implement I9–I10 after M4 |
| M6 | TODO | Enum/Pattern execution and aggregate control flow | Implement I11–I12 after M4–M5 layout foundation |
| M7 | TODO | Universal generic verification and full specialization | Implement I13–I14 after semantic prerequisites |
| M8 | TODO | Generic generation, entries, metadata and frames | Implement I15–I16 after M7 |
| M9 | TODO | Function values, Closure capture and indirect calls | Implement I17–I18 after M7–M8 |
| M10 | TODO | Object/Weak and runtime struct tests | Implement I19–I20 after M5, M8, M9 |
| M11 | TODO | Index/Range/Slice/collections/iteration | Implement I21–I22 after M4, M6–M8 |
| M12 | TODO | Core comparison mappings, Stringify and interpolation | Implement I23–I24 after M5, M7–M8 |
| M13 | TODO | FFI, Library output and generalized native connection | Implement I25–I26 after M3–M5; native checks need G4 |
| M14 | TODO | Packages, content stores and verified semantic reuse | Implement I27–I28 after M3; complete semantic records after M4–M13 |
| M15 | TODO | Internal Mod sequencing/provisional Binding is executable work | Implement I29; external adapter I30 is blocked by G2 |
| M16 | TODO | Test membership/discovery and verification semantics are executable work | Implement I31–I32; public runner I33 is blocked by G1 |
| M17 | TODO | Full conformance, performance and documentation closure | Implement I34–I35 after preceding applicable milestones |

Update this table and §8 after every meaningful implementation/verification change. For a partially completed milestone, identify completed I/T items; never mark the whole milestone complete while interface or verification dependencies remain.

## 3. Current State and Architecture

### Actual processing path

| Stage / entry points | Actual input and output | Support and needed work |
| --- | --- | --- |
| CLI: `Kimi/Unit/Command/{BuildCommand,EmitCommand,RunCommand,DefaultCommand}.cs`; `Solution` in `Kimi/SolutionAndProject/Solution.cs` | Input path/options → selected Project(s); implicit single-source Application supported | **Verified fact:** build/run/emit command implementations exist; `DefaultCommand.Execute` reports `Unknown command '{args[0]}'. Use build, run, emit, or lsp.` Add specified source/dependency/test commands, not aliases for obsolete syntax |
| `Project.BuildCore` / `BuildTarget`, `Compilation.Prepare` | Project configuration and source bytes → one target-specific Compilation, environment variables and build metadata | **Verified fact:** `Prepare` freezes configuration when parsing begins; external loading remains a comment. `ProjectFile` still exposes `KotonohaArray`, not the adopted Dependencies model. M3 owns resolution and snapshots |
| `SourceDocument.FromUtf8`; `Kotonoha.AddSource` / `ParseSource`; `Tokenizer`, `TokenReader`, `RootKoto.Parse` | Immutable source snapshots and source-local `CodeContext` → merged Koto declarations and `GeneratedFunction` for root executable syntax | **Verified fact:** source contexts and diagnostics are retained; tokenizer is disposed. Reuse this representation. Audit remaining syntax/membership/fragment rules; do not add a separate Bound Tree |
| `Compilation.Bind` → `Binding.Bind(BindingMode.Final)` | Existing Koto → `BoundType`, `BoundSymbol`, selected call/Pattern/Property/conversion information, issues and obligations | **Verified fact:** pass resets semantic fields and ownership, binds schemas/constraints/headers/storage/bodies, verifies capabilities/conformances and acquisitions. It does not execute Mods. General effects, generic body proofs and usage deadlines need completion within this architecture |
| `Binding.CheckStartup`, `ControlFlowAnalysis`, `BindingControlFlowTypes` | Selected source and bound facts → startup selection, structural result/transfer facts, flow issues and pending Binding | **Verified fact:** control-flow checking is separate from execution reachability. Preserve this distinction for unreachable syntax, `require`, covered arms and cleanup |
| `OwnershipAnalysis.Analyze` → pooled `OwnershipBody` | Committed Koto operations and flow → Places, value operations, CFG edges, initialization/Move/Loan facts, cleanup/result plans | **Verified fact:** `BuildBody` and solver use reused storage. Current captures/defaults explicitly call `Unsupported`; one unchecked body invalidates all body certificates. General borrowing/effects are prerequisite work, not a permission to bypass this gate |
| `LlvmEmitter.TryPrepare` / `CheckInputs`; `BodyLowering.Lower` | Current verified ownership bodies and `FunctionAbiPool` → checked physical `EmissionModule` | **Verified fact:** rejects external modules, Library, nested declaration containers, unsupported signatures/captures/specializations. Registers signatures before bodies for recursion; failure clears the module. Lowering validates actual plans, not just a stale boolean |
| `EmissionModule`, `EmissionFunction`, `LlvmModuleWriter`, `WindowsLowering` | Compact physical opcodes/operand arrays, constants, layouts and signatures → LLVM text | **Verified fact:** physical module does not hold Koto/Binding/ownership objects. Preserve syntax-free physical pooling and shared definition/call ABI contracts; add opcodes only for needed behavior |
| `EmissionArtifacts.Publish`, `ArtifactFiles`, `ArtifactPaths` | Checked module/configuration → `.ll` and `.link.json` with identities/hashes | **Verified fact:** publication is separate from inspection `WriteIr`. Extend records/Library/dependency contracts; successful text generation is not LLVM or native success |
| `NativeToolchain.Build`, `ToolchainResolver`, `WindowsProfile`, `Kernel32Imports` | Matched IR/manifest, actual tools and libraries → checked objects, linked executable and build record | **Verified fact:** checks versions/hashes and actual undefined symbols; fixed runtime/backend allowlist is insufficient for general source FFI/dependency supply. M13 extends it using §20.8.2 |
| `Project.Run` / native launch | Existing executable and latest record → program output/exit | **Verified fact:** run does not rebuild. Preserve direct-executable behavior, input summary, byte forwarding and cancellation |

There is no evidence for a separate Bound Tree. Terms such as “bound operation” describe facts attached to Koto or retained tables. `OwnershipBody` is the existing semantic CFG/operation representation; `EmissionModule` is the later physical plan. Generic semantic plans and metadata proposed below extend these responsibilities rather than replacing the whole pipeline.

### Current support by requirement, stage and input range

All execution columns below are **not rerun**. “Implemented” is limited to inspected code paths; whole-family classification remains partial or unknown unless the range is explicit. Existing test classes are under `xUnitTest/Tests/`.

| Requirement range | Stage / classification | Evidence and limitation |
| --- | --- | --- |
| R1 UTF-8, Unicode/NFC, layout, literals, selected directives | Parsing: Implemented for inspected paths; complete conformance: Unknown | `Lexing/`, `Parsing/`, `SourceDocument`; `UnicodeIdentifierTest`, `SourceEncodingTest`, `DirectiveConditionValidationTest`, `KotonohaSerializationTest`. No wholesale parser rewrite scheduled |
| R2–R5 names, complete Types, declaration Origins, ordinary generic inference, static Contracts, Property signatures | Binding: Partially Implemented | `Binding.cs`, `Binding.Types.cs`, `Binding.Properties.cs`, `Binding.Contracts.cs`, candidate/constraint partials; `ReceiverShorthandTest`, `TypeBindingTest`, `ContractBindingTest`, `PropertyBindingTest`. Access-domain, symbolic proof and execution completion remain |
| R3 numeric conversions | Binding/IR: Partially Implemented | `Binding.BindConversion` accepts integer pairs and f32 widening/float identity, explicitly rejects remaining conversions; `FloatConversionEmissionTest.RemainingConversionsAreNotMistakenForWidening`. Do not repeat existing integer/widening implementation |
| R6–R7 ordinary owned values and static Tuple/fixed-array paths | Ownership/IR: Partially Implemented | `OwnershipAnalysis.Elements.cs`, `OwnershipBody.MovePaths.cs`, `BodyLowering.MovePaths.cs`; `ElementParameterMoveEmissionTest.ParametersTransferOnlyTheSelectedResponsibility`. Local/parameter static Move and local repair exist; dynamic Non-Copy Move remains forbidden, general field/borrow/effect work remains |
| R6 shared string comparisons/arguments | Ownership/IR: Implemented for the narrow inspected range; general borrowing: Partially Implemented | `ReferenceTypes`, comparison/element paths; `ReferenceEmissionTest`, `ElementBorrowOwnerEmissionTest`. Owned locals/parameters/temporaries with static string elements supported; ref storage/results, uniq, nested borrowing and general receiver paths not certified |
| R8 struct construction, custom/computed Property execution, deinit | Parsing/signature Binding: Partial; executable path: Not Implemented | `Binding.Expressions.cs` explicitly rejects constructors/destructors; `LlvmEmitter.CheckInputs` rejects containers. `AggregateLayoutPool.Get` accepts only owned Tuples/fixed arrays. Existing Property declarations must be reused |
| R9 control flow/results/defer | Analysis/IR: Partially Implemented | `ControlFlowAnalysis`, `StructuralCompletion`, ownership result/cleanup plans and BodyLowering result partials; `ControlFlowConformanceTest`, `DeferredEmissionTest`, `AggregateResultEmissionTest`. General for/iterator/borrowed result paths depend on later milestones |
| R10 enum construction/Pattern coverage | Binding and selected ownership: Partially Implemented; enum layout/native: Not Implemented | `Binding.Enums.cs`, `Binding.Patterns.cs`, `OwnershipAnalysis.Enums.cs`, `OwnershipAnalysis.Match.cs`; `EnumBindingTest`, `EnumOwnershipTest`, `PatternBindingTest`. Scalar/string match code is reusable; no enum native claim |
| R11 Closures/function values; R5 full specialization | Parsing/contract representation: Partial; executable path: Not Implemented | `Binding.Expressions.cs` rejects anonymous/specialization functions; captures/defaults rejected by ownership and emitter. `Binding.CallableContracts.cs` is not a Closure runtime |
| R12 runtime struct `is` | Binding: Partially Implemented; execution: Not Implemented | `BindRuntimeTypeTest` retains `BoundRuntimeTypeTest` for supported concrete struct/object Types, rejects dependent targets. `RuntimeTypeTest`; no completed metadata/ownership emission path established |
| R12 object/Weak catalog | Incorrect or Inconsistent with current specification | `CoreIntrinsics.cs` still says object public spellings are deferred and has a missing unnamed ObjectOwnership entry; §§13.5.8–9 and 22.1 now specify them. Fix catalog and full implementation; do not treat the stale comment as authority |
| R13–R15 sequence/collection/Core runtime | Partially Implemented overall; missing catalog entries verified | `CoreIntrinsics` supplies Copy/Owned/Callable/writeLine/Option/Result and marks Array/Index/Range/Slice/Dictionary/Stringify/comparison/iteration entries Missing. Scalar/string literals and fixed aggregates already execute in code; remaining operations need full paths |
| R16 startup/Abort/string runtime | Partially Implemented | `Binding.Startup.cs`, `WindowsRuntime.ll.in`, `WindowsLowering.Abort.cs`; startup selection, UTF-8 output, allocation/free and Abort paths exist. Demand-driven static execution and object runtime remain |
| R17 raw pointer/C imports | Syntax/type representation: Partial; end-to-end: Unknown or unavailable through current container gate | `Unsafe` syntax, Type representations, fixed kernel32 import runtime do not prove general source imports. M13 must audit source imports and reach intended unsafe/ABI checks |
| R18–R19 dependencies/packages/semantic persistence | Not Implemented on inspected product entry path | `Compilation.Prepare` placeholder, old ProjectFile settings, command registry/default rejection; no resolution integrated into `BuildTarget`. Internal Kotonoha serialization is not package support |
| R20 Mods; R22 language test execution | Not Implemented on inspected product entry path | `Compilation.Bind` explicitly documents no Mod execution; compiler commands have no test command; `#Test` syntax availability is not runner support. Interface portions also have G1–G2 |
| R21 emit/build/run and Windows native support | Partially Implemented | `EmissionArtifactsTest`, `NativeToolchainTest`, `SolutionInputTest`, backend scripts. Application-only generation gate; generalized native input closure and immutable historical records remain |
| R23–R24 current regression/performance claims | Unknown for this checkout's execution | Existing allocation tests/benchmarks and STATUS results are leads only; current V3–V12 remain unexecuted or blocked |

### Reload, invalidation and reuse

**Verified fact:** `Kotonoha.OnDeserialized` reconstructs the tree by reparsing saved source documents into fresh contexts; syntax-tree edits are not persisted. `Binding.Bind` resets semantic fields and calls `InvalidateOwnership`. `LlvmEmitter` clears its declaration-to-ABI map and function context in `finally`, including failure. `AggregateLayoutPool` separates physical shapes from temporary BoundType lookup; it currently limits depth to 64 and offsets/counts/sizes to signed 32-bit implementation storage.

**Proposed change:** keep same-snapshot warm reuse; audit source replacement for retained syntax and rebuild source-local environments. Introduce versioned semantic records only for R19, with verified dependencies, current source mappings and corruption fallback. Physical equality never establishes Type identity, Origin equality, Copy capability or cached proof validity. Document justified resource/representation limits under §21.1.1 instead of silently truncating or claiming support for arbitrarily large layouts.

## 4. Specification Requirements

The following are stable requirement families. Each includes all applicable normative clauses of its cited sections, not just the examples here. M1 must expand families into clause-level T variants in this file before declaring a family complete. Appendix A coverage is mandatory, whereas optional reference algorithms are not acceptance criteria.

| ID | Required observable behavior / owning specification | Implementation / verification coverage |
| --- | --- | --- |
| R1 | §§2, 19; A.1–2, A.6: immutable UTF-8 source identity, Unicode 15.0 identifiers/NFC, exact literals and source spans; layout/Body syntax; immediate case-sensitive directives and precise excluded-syntax boundary | M2/M15; T1–T3, T27; V3–V4, V8 |
| R2 | §§6.1–6.2, 6.4–6.5, 9, 18.1; A.3–4: namespace/role separation, source-local aliases, fragments/access/inheritance, modifier/Attribute validation and API reachability; no alias leakage or inherited fallback after committed lookup | M2/M3/M5/M16; T3–T5, T23, T28; V4, V8–V10 |
| R3 | §§3, 4.1–4.4, 10, 13.2–13.5.4; A.6, A.12, A.14: complete Types/Semantics, fixed lengths, exact fitting, required constant evaluation, checked numeric operations/conversions and specified FP behavior | M2/M7/M8; T2, T6–T8, T15; V4–V7, V11 |
| R4 | §§7.1–7.5, 10, 12.2/12.4, 13.7; A.3: select a unique operation before usage checks; no fallback after Loan/access failure. Receiver first for bound calls; explicit arguments once in source order; defaults in declaration environment; immutable parameters and normal-result acquisition | M2/M4/M5/M7; T5, T9, T12; V4–V8 |
| R5 | §8, §§10.5/10.7/10.8, 21.3–21.4.6; A.11–12, A.14: universal generic definition proofs; restricted constraint calculus, verified witnesses/associated Types; closed mandatory full specialization; legitimate deferred representation obligations; bounded deterministic shared generation/entries/frames | M7/M8; T14–T16, T25, T29; V4–V8, V11 |
| R6 | §§3.3–3.8, 15.2–15.6/15.8–15.9; A.3/A.8/A.12–14: Origins, return contracts, actual Loan anchors, reborrowing, variance, retained dependencies and static/call effects. Unknown proof never certifies usage; Owned is not thread safety | M4/M7/M9–M11; T10–T11, T16–T20; V4–V8, V11 |
| R7 | §§3.5–3.6, 15.1, 16; A.7/A.14: distinct construction/history/completeness, sparse static Move Paths; no borrowed Non-Copy extraction or Partial Move across deinit; secure result → reverse logical cleanup → normal delivery; Abort/divergence stops subsequent work | M4–M11; T9–T13, T17–T20, T30; V4–V8 |
| R8 | §§6.2, 11, 16.3, 21.1; A.4/A.7/A.14: standard versus accessor operations, first placement/custom setters, receiver projection, constructors/base completion/deinit and static storage; Kimigayo alignment sorting versus C packing-16 layout | M5; T11–T13, T22; V4–V8 |
| R9 | §§12, 14, 17.4; A.9–10/A.15: expression contexts, result inference, nearest transfer targets, static versus runtime paths, refinement validity, coverage, guard/body identities and mandatory warnings; for uses the defined non-lending protocol | M2/M4/M6/M11; T9–T10, T13, T20, T26; V4–V8 |
| R10 | §§3.5.2, 6.3, 14.8, 17.2, 22.1; A.10–11: enum identity/Case order/payload acquisition, conditional Copy, Option/Result contracts, positional owned/shared Pattern acquisition and complete cleanup | M6/M7; T13–T14, T19–T20; V4–V8 |
| R11 | §§3.2.1, 7.6, 8.6, 15.8.2–3, 21.2.4–5; A.8/A.14: Function Items, capture order/mutability, callable receivers, Owned common-type erasure, per-call Origins and independent results; empty/inline/heap environments and typed indirect ABI | M9; T16–T17; V4–V8, V11 |
| R12 | §§3.2.2/3.3.3–5, 12.4.4, 13.5.7–9/13.6.1, 21.2.2–3; A.8/A.14: specified objects/Weak, exact dynamic cleanup/view adjustment, completed ObjectCompatible guarantees, runtime struct tests/refinement, rc/arc counts and cyclic builder publication | M10; T10, T18; V4–V8, V11 |
| R13 | §§4.1–4.6, 14.6, 22.1; A.12–14: fixed arrays, Index/Range/ResolvedRange, saved metadata, O(1) shared Slice operations, SharedReadResult families, permanent iterator exhaustion, literal-only Move/disjointness and correct runtime bounds locations | M4/M7/M11; T8, T11, T19–T20; V4–V8, V11 |
| R14 | §4.7, §12.3.4, §22.1; A.13–14: Array/Dictionary mutation/lookup, duplicate checking before value evaluation, capacity/no-allocation and amortized bounds, insertion order, result responsibility, conservative dependency union and user effects | M11; T20–T21; V4–V8, V11 |
| R15 | §§12.3.3–4, 13.4, 22.1/22.4/22.5.6; A.11/A.14: exact Core identities, built-in/user comparison mappings, UTF-8/NUL strings, scalar Stringify and single-evaluation interpolation; document permitted float spelling choices | M7/M12; T2, T14, T21–T22; V4–V8, V11 |
| R16 | §§14.11, 17.1–17.4, 22.2/22.5; A.9/A.14–15: unique startup, demand-driven static initialization, cycle/shutdown guards, Option/Result failure policy, Abort diagnostic provenance and required warnings; no exception unwinding or silent omitted checks | M4–M6/M13; T9–T10, T12, T22, T26; V4–V7, V9 |
| R17 | §5, §§7.5, 21.5.3–4, 22.3; A.5/A.14: safe pointer holding versus unsafe arithmetic/access/conversions; positive stride and supported round trips; exact direct scalar/raw-pointer C imports, no implicit marshalling; unsupported signatures rejected before generation | M13; T22; V4–V7, V9 |
| R18 | §§18.1–18.5, 18.8, 20.1–20.6/20.8.6; A.1–3/A.16: exact-version DAG/direct names, definition environments, conflict paths, immutable snapshots, product/test lock partitions, restore-only atomic lock changes and old-setting migration | M3/M16; T23–T24, T28; V4, V8–V10 |
| R19 | §§18.6–18.7, 21.3.4; A.16: fixed source package closure and canonical identity; local transactional publication; integrity versus semantic validity; bounded stores/pins/reuse, absence/effect dependencies and source remapping; no-cache semantic equivalence | M14; T24–T25; V4, V8–V9, V11 |
| R20 | §20.7, §6.5 Mod markers; A.1–3: single execution in dependency order, selected-syntax snapshots, provisional Binding/query validity, append-only permitted targets, independent generated contexts/provenance and deterministic order; finalization after generation | M15; T27; V4, V8. External interfaces: G2 |
| R21 | §§20.8, 21.4–21.5, 22.2–22.5; A.14/A.16: check/emit/build/run distinctions, Library inspection, pinned windows-x64-v1/LLVM 22.1.8/backend ABI 2, native member closure, checked publication and current-record execution, cancellation and output | M3/M8/M13/M14; T22–T25, T31; V3–V9 |
| R22 | §§6.5.1, 17.5, 18.8, 20.9, 21.3.7, 22.6; A.17: test-only membership, all-body verification before filters, product plan isolation, once/lazy verification, false require Abort, static identities, one process per case, reliable bounded reporting/recovery | M16; T28–T30; V4, V8, V10–V11. Public profile interfaces: G1 |
| R23 | §§1.3, 17.3–17.4; A.1–17: preserve diagnostic category, stage and original location; unsupported/unresolved operations rejected before finalization even unused; poisoned/stale plans cannot publish; optimization/caches cannot alter acceptance | All milestones; T1–T31; V1–V10/V12 |
| R24 | §4.6.8/4.7.7, §§21.3–21.4, 22.6.5, A.14/A.16; repository guidelines | Reused/interned storage, bounded algorithms, measured cold/warm allocation/retention and throughput; semantic checks before optimization. M1/M8/M11/M14/M16/M17; T15/T21/T25/T29/T32; V11–V12 |

### Acceptance rules for negative and unsupported cases

For every negative case, demonstrate that earlier parsing/Binding checks succeeded as needed to reach the intended check, then assert the category/reason, stage and source span. Expected unsupported rejection is temporary for settled features and cannot satisfy their final positive acceptance. For genuinely deferred language features, keep a targeted rejection case and identify the boundary. Unsafe contract violations have no invented deterministic runtime outcome; test valid operations and static prohibitions, not an assumed crash or recovery.

## 5. Gaps and Decisions

### Open issues

| ID / classification | Evidence / affected requirements and milestones | Resolution needed / current disposition |
| --- | --- | --- |
| G1 `SPEC_GAP` | Appendix D.4 explicitly leaves test temporary-directory API, deadline/grace option forms/defaults, budgets/queues/log retention, transport/schemas, empty-set option, exit allocation, ID encodings/versions/bounds unsettled. R22; I33/M16/M17 | Separate specification decision must finalize those interfaces before a conforming public runner can be completed. I31–I32 internal membership/verification work may proceed. Do not invent option spellings or call exit 0 sufficient success |
| G2 `SPEC_GAP` | §20.7.7/Appendix D: query/marker APIs, assembly compatibility, configuration, cache and host cancellation contracts undefined. R20; I30/M15/M17 | Separate Mod host/interface design. Internal sequencing/invalidation tests I29 may proceed with private test doubles; illustrative `IMod` is not an adopted public API |
| G3 `INVESTIGATION_GAP` | Current `dotnet test --help` prints base MTP options but extension discovery attempts MSBuild and reports `Access to the path 'C:\Users\bwff1\AppData\Roaming\NuGet\NuGet.Config' is denied.` No current build/test. R23–R24; all verification-dependent exits | Run V2–V4 in an execution environment authorized to read the configured NuGet file and restore prerequisites. Do not replace user NuGet settings during planning. The help command's process exit 0 does not establish successful extension discovery |
| G4 `INVESTIGATION_GAP` | Tool version probes returned `C:\Users\bwff1\repos\archi-Doc\Kimigayo\toolchain\opt.exe: permission denied` (likewise llc/clang/lld-link). Hashes can be read. R21/R23; V5–V7/V9–V11 | An execution environment permitting the pinned tools is needed. No unpinned fallback, tool reinstall, or claim of LLVM acceptance. Planning remains possible |
| G5 `INVESTIGATION_GAP` | Broad source inspection establishes major gates, not every normative clause or all current regressions. STATUS history is not current evidence. All R families/M1/M17 | Before each family starts, map its owning subsections and Appendix A rows to precise T variants, inspect the actual rejecting branch/caller and confirm with focused checks. Mark residual input ranges Unknown; full completion cannot skip this audit |
| G6 `IMPLEMENTATION_MISMATCH` | `CoreIntrinsics` missing object/Weak catalog and stale comment versus §§13.5.8–9/22.1. R12/R15; M10 | Adopt existing specified names/formation/acquisition, update catalog and negative tests. No language decision required |
| G7 `IMPLEMENTATION_MISMATCH` | `ProjectFile.KotonohaArray`, `Compilation.Prepare` placeholder and current command set versus §§18.4–18.6/20.8.6. R18–R19/R21; M3/M14 | Dependencies replacement plus explicit nonempty legacy-setting migration diagnostic. New diagnostics are proposed, not already available identifiers |
| G8 `INVESTIGATION_GAP` | Current `AggregateLayoutPool` depth/Int32 bounds versus §21.1.1 allowance for reasoned stricter backend limits. R3/R8/R24; M5/M8 | Measure representative large/nested layouts; document and diagnose legitimate limits, widen internal fields only where needed. Do not classify every finite limit as a language contradiction |

No unresolved `SPEC_CONFLICT` has been established by this investigation. If one appears, record both authoritative clauses and pause only dependent work. §22.1's broad mention of string concatenation does not override the explicit executable-finalization prohibition in its owning §13.3; it remains excluded. This is an authority resolution, not permission to select an acquisition strategy.

### Design decisions

| ID | Decision and rationale |
| --- | --- |
| D1 | Extend Koto facts, Binding tables, OwnershipBody and physical emission plans. Existing architecture already separates selection, usage legality and physical representation without duplicating syntax |
| D2 | Start cross-module implementation with source-first loading and conservative module-wide invalidation, explicitly permitted by §18.7.3. Add persistent/finer reuse only after no-cache correctness and measurement |
| D3 | Finish universal generic semantics before shared/specialized code generation. Unknown Copy needs correlated behavior, not hidden constraints or treating all values as Move |
| D4 | Introduce call/static/destructor effect fixed points before general object/Property/collection borrowing. Preserve winner selection; later legality failures cannot reopen overload search |
| D5 | Keep sparse integer-ID Place paths and reused arrays/worklists. Extend projections by semantic Field/Case identity; do not expand fixed arrays by length or allocate per use |
| D6 | Establish a valid generic shared baseline and budget-zero behavior, then bounded optional specialization. Entry ABI is budget-independent; explicit full specialization is mandatory. Budget defaults and physical positions are documented compiler choices |
| D7 | For collections, choose private storage meeting §4.7 cost/retention rules; do not block on a stable public storage ABI. Prototype geometric Array growth and Dictionary reusable indexed slots/order links without per-entry allocations; measure before fixing strategy |
| D8 | G1–G2 go to separate specification work under the user's exclusion. Do not ask the user to choose arbitrary interfaces in this implementation plan or silently interpret silence as agreement |
| D9 | Reuse normal string/runtime cleanup and Abort paths for interpolation/tests. Heap-string construction must gain real allocation/free coverage; Static-backed destruction audits alone are insufficient |
| D10 | Performance work is continuous, with M17 closure. Retain existing warm zero-allocation assertions where applicable, but set no universal zero-allocation promise or unsupported percentage speedup |

## 6. Milestones

All changes below are **proposed**. Existing file/symbol names are evidence anchors; `Binding.*`, `OwnershipAnalysis.*`, `BodyLowering.*`, and `LlvmModuleWriter.*` denote existing partial families. Every explicitly proposed file/symbol is marked **new**. Dependent slices may add private records within existing files where that is the smaller coherent change.

### M1 — Establish executable evidence and clause coverage

- Objective: R23–R24 and the verification foundation for R1–R22. No implementation prerequisite; G3–G4 block execution, not read-only clause mapping.
- I1: create the clause/test inventory in this plan from the owning chapters and A.1–17; inspect only paths implicated by each clause. Compare existing tests with language rules and identify obsolete subset rejections. No blanket source-file rewrite.
- I2: inspect/fix verification configuration only in the implementation phase: `global.json`, project files, `.github/workflows/test.yml`, backend scripts, existing fixture helpers. Establish explicit configuration, runner selection, nonzero test counts, artifact identities and bounded processes. Runner-specific filter syntax must be confirmed after G3.
- Invariants: preserve user changes; don't run tests concurrently against shared fixture/native directories; don't turn historical totals into a current baseline.
- Exit: V2–V3 current baseline recorded, every R family assigned to cases and a milestone, and G5 closed for the next executable slice. Environment failures stay visible. Risk: tests rejecting valid specified features can pass while conformance is incomplete.

### M2 — Complete source/static semantics and numeric operations

- Requirements: R1–R4/R9/R23; depends on M1 inventory, with independent investigation permitted while G3 is open.
- I3: extend `Binding.Access.cs`, `Binding.MemberLookup.cs`, `Binding.CandidateEvaluation.cs`, `Binding.Lengths.cs`, `Binding.Types.cs`, parsing/diagnostic/writer paths only for demonstrated gaps: access domains/fragments, complete nested Semantics/target applications, exact constant-readable lengths, expected-Type boundaries including collection arguments, receiver/default environments and selected Attribute checks. Retain already supported receiver shorthand, Unicode and directive behavior.
- I4: extend `Binding.BindConversion`, `ConversionBinding`, `OwnershipAnalysis.Values.cs`, `BodyLowering.Conversions.cs`, `LlvmModuleWriter.Conversions.cs`: exact integer-literal-to-float fitting, runtime f64→f32 and allowed integer↔float, general identity acquisition where semantics permit. Diagnose prohibited typed i128/u128↔float and bool/char conversions; inspect i128 arithmetic helper needs under M13 rather than silently emitting missing libcalls.
- Invariants: no extra evaluation or premature default literal Type; every adaptation in a chain retains rounding/checks; definition errors versus runtime numeric Abort remain distinct. Unsupported ownership adaptations await M4/M10.
- Exit: applicable nongeneric T1–T8 and affected T26 variants pass V4–V8; numeric admitted pairs reach V5–V6; no redundant replacement of working arithmetic. Generic T8 variants remain assigned to M7–M8, and later-stage T4–T5 variants to their milestones. Main risk: FP overflow boundaries/LLVM poison and expected-Type inference leakage.

### M3 — Source modules, configuration, locks and semantic checking

- Requirements: R2/R18/R21/R23; depends on M2 identity/access foundations. First complete the non-native portion; general dependency execution waits for M13.
- I5: change `ProjectFile`, `ProjectConfiguration`, `Solution`, `Project`, `Compilation.Prepare`, source-local Binding lookup. Add **new** `Kimi/SolutionAndProject/DependencyResolver.cs` and **new** `DependencyLock.cs` for exact-version DAG resolution, direct-name mapping, per-definition settings, conflict paths, Core identity, byte snapshots and product/test partitions. Replace nonempty `KotonohaArray` with a migration error; do not silently reinterpret it.
- I6: add **new** `RestoreCommand.cs` and **new** `CheckCommand.cs` in `Kimi/Unit/Command/`; register through existing command conventions. Restore only resolves local inputs and atomically updates both lock partitions; check performs required semantics without emission/native execution. Stage Package loading through M14 interfaces, rejecting unsupported Package inputs until implemented.
- Invariants: restore alone writes locks, stale empty locks remain errors, source edits don't require lock rewrites, tests can't supply product candidates, module traversal uses bounded explicit worklists. Configurations and source snapshots are fixed once per attempt.
- Exit: Project-only graphs pass T23–T24 via V4/V8/V9; no-cache check is independently verifiable. Package coverage remains assigned to M14. Risks: environment contamination, transitive name exposure, graph explosion and concurrent lock replacement.

### M4 — General ownership, Origins, effects and usage checking

- Requirements: R4/R6–R7/R9/R16/R23; depends on M2 and M3 module identities. Recursive effect tests may first use one module; cross-module closure requires M3.
- I7: extend `Binding.Origins.cs`, `Binding.TypeOrigins.cs`, `Binding.OriginRequirements.cs`, `Binding.ArgumentOperations.cs`, callable/Contract mappings and `BindingModel.cs`. Add **new** `Kimi/Compiler/Analysis/EffectAnalysis.cs` for bounded recursive effect components, static-root access, defaults, destructors, returned anchors and implementation-family ObjectCompatible proof. Separate pending work from Proven/NotProven/Error.
- I8: extend `OwnershipModel`, `OwnershipBody`, element/Move/comparison/checking partials, `BodyLowering.Calls.cs` and reference lowering: general ref/uniq/object-borrow storage and results, explicit reborrow/adaptation, universals/variance, retained dependencies, sparse field paths and suspension, destruction lifetime checks, result Loans. Connect default argument acquisition in declaration environments and partial-call cleanup to the selected call mapping; remove the existing default-parameter rejection only when those operations are checked. Use current snapshots/results/checking continuations instead of a second ownership graph.
- Invariants: Origins don't merge distinct Loan authority; result secured before cleanup; temporary life isn't shortened to Loan end; no two-phase receiver reservation; no default/callee/static reentry hidden by a missing summary. Existing parameter immutability and sparse array paths persist.
- Exit: T9–T11 with direct/recursive/module/cleanup interactions pass V4–V8. General reference ABI and cleanup must land together for native tests; semantic negatives can land first. Risks: unsound cycle proof, stale call summaries, false acceptance after rebind.

### M5 — Structs, Properties, construction, destruction and statics

- Requirements: R2/R7–R8/R16/R21; depends on M4, plus M2 fragment/Attribute validation.
- I9: extend `Binding.Storage.cs`, `Binding.Properties.cs`, `Binding.PropertyMatching.cs`, `Binding.Expressions.cs`, `AggregateLayoutPool`, `WindowsLowering`, `FunctionAbiPool`; implement stored/custom/computed operations, source init/base/deinit, complete Types and logical component layout. Add **new** `BodyLowering.Properties.cs` only if needed to keep this coherent.
- I10: extend ownership cleanup/result and physical module/runtime paths for partial construction and per-layer deinit, getter-result temporaries/custom-set self-assignment, demand-driven static slots with initialization/shutdown state and reverse successful-init cleanup. Relax container emission gates only once all selected members are validated.
- Invariants: standard getters aren't synthesized user functions; physical sorting doesn't change logical effects; no deinit on incomplete layers; base stored at zero; C layout packing 16 and fragment restrictions; first static write initializes before replacement.
- Exit: T11–T12 and layout/FFI layout portion of T22 pass V4–V8. Constructor selection, initialization tracking, cleanup and layout form one minimum native slice; no independently callable placeholder constructor. Risks: inaccessible setters, incomplete self escape, static reentry, zero-size responsibility.

### M6 — Full enum and Pattern execution

- Requirements: R7/R9–R10/R16; depends on M4 and M5 aggregate layout/cleanup foundation.
- I11: reuse `Binding.Enums.cs`, `Binding.Patterns.Model.cs`, `MatchCoverage`, `OwnershipAnalysis.Enums.cs` and Match partials; complete borrowed/nested positional acquisition, correlated read Types, guard candidate restrictions and separate body identities.
- I12: extend `AggregateLayoutPool`, `BodyLowering.Match.cs`, physical instructions/writer for tagged layout, payload construction/partial cleanup, owned/shared Subject decomposition, selected Case destruction and Option/Result execution. Preserve existing scalar/string match path.
- Invariants: acquire Subject once; false-guard effects flow onward; covered arms still checked without runtime arrivals; no general enum payload access or deferred Pattern forms; no borrowed referent destruction.
- Exit: T13–T14 pass V4–V8 for nested/borrowed/non-Copy/guard/Abort/divergent cases. Runtime enum layout and match cleanup must be validated together. Risk: layout bytes mistaken for initialized state or overly powerful coverage inference.

### M7 — Universal generic semantics and closed specialization

- Requirements: R3–R6/R10/R15/R23; depends on M2–M6 semantic contracts (can implement independent proof slices earlier).
- I13: extend existing constraints, schemas, associated Types, conditional conformance, call matching and obligations. Verify unused generic bodies under declared premises; retain correlated Copy/Move and SharedReadResult cases. Complete input-order-independent Type/length/Origin inference, occurs checks and permitted projection normalization.
- I14: add **new** `Kimi/Compiler/Binding/Binding.Specializations.cs` for ordinary header matching, closed keys and inherited contracts; finalize sets before calls/function references, validate family effects, and commit required selected implementation without overload reselection. Representation obligations retain definition site, premise/dependency and deadline.
- Invariants: no hidden body capability premise; specialization cannot rescue invalid ordinary body; Unknown is not successful proof; caller aliases never participate in definition lookup. Runtime Contract extensions stay rejected.
- Exit: T14–T16 semantic and reload cases pass V4/V8; no native claim until M8. Risk: conflating candidate isolation with reusable proof state.

### M8 — Generic generation, metadata, entries and fixed frames

- Requirements: R5/R7/R11–R13/R21/R24; depends on M7, existing layout/ABI and cleanup.
- I15: add **new** `Kimi/Compiler/Emission/GenericGenerationPlan.cs` and **new** `TypeMetadataPool.cs`; extend FunctionAbi/BodyLowering/EmissionModule using typed entry/context pairs, complete ArgKey versus payload CoreId, immutable metadata and operation-context schemas. Required plan worklist distinguishes finite recursion, growing instantiations and inline layout cycles.
- I16: implement exact per-substitution fixed scratch frames, alignment/zero-capacity adapters, budget-independent entries and shared baseline; then bounded optional specialization/deduplication. Adopt Appendix B.7's initial proposal: exclusive all-member choices per sharing class, per-original-generic-function and whole-generation-unit growth limits, one finite nonnegative multiplier, deterministic benefit/cost ordering and bounded exploration. Count body work moved into entries/helpers; do not refund cross-class deduplication savings. Choose numerical defaults by measurement. Product/test generation requests have separate budgets and stable product-based tie breaks.
- Invariants: same-size Types do not imply interchangeable destruction/alignment; no dynamic unknown-substitution storage; no maximum-across-instances frame buckets; no operation-only schema casts. Optional exploration exhaustion falls back to verified baseline, not a new source error; required exhaustion gets a resource diagnostic.
- Exit: T15–T16/T29 pass V4–V8/V11 at budget zero and nonzero, including explicit selection, recursion, no-cache/order changes, frame/probe/unwind and call-result storage. Entries/adapters/frame plans must be delivered as a consistent unit. Risks: generation explosion, accidental ABI dependence on budgets, retained syntax in pools.

### M9 — Callable values and Closures

- Requirements: R4/R6–R7/R11; depends on M7–M8, M4 Loans and M5 cleanup.
- I17: extend `FunctionKoto`, existing callable contracts/Binding expressions and capture records only where semantics require missing information; implement capture resolution/acquisition, Function Items, result expectations, receiver classification and common Function Type erasure.
- I18: extend FunctionAbi, metadata and physical plans for empty/inline/heap environments, typed direct/indirect entries, Shared/Exclusive/Consuming calls and environment destruction. Add **new** `BodyLowering.Callables.cs` and focused **new** `CallableEmissionTest.cs`.
- Invariants: direct-syntax capture order and general-expression allocation order follow §21.2.5.3; callable Copy is independent of receiver kind; no hidden receiver/call-local result escape; consuming partial cleanup exact.
- Exit: T16–T17 pass V4–V8/V11 across repeated shared calls, consuming capture Moves, escaping rejection and indirect/cross-module effects. Risks: accidental borrow extension and adapters acquiring twice.

### M10 — Object ownership, Weak, metadata and runtime tests

- Requirements: R6–R7/R12/R15; depends on M5, M8–M9 and completed effect-family proofs.
- I19: update `CoreDeclaration.cs`, `CoreIntrinsics.cs`, existing runtime Type-test Binding and explicit operations for specified makeObj/makeRc/makeArc/clone/downgrade/upgrade/cyclic factories, exact upcasts/Supports and Effective Type facts. Correct G6; do not introduce a general deep clone or runtime Contract Views.
- I20: add **new** `BodyLowering.Objects.cs` and **new** `ObjectRuntime.ll.in` (or a smaller extension of `WindowsRuntime.ll.in`); implement §21.2.3's object/side-table states, payload +16, count limits/migration, arc ordering, Weak guard lifetime and exact dynamic destruction. Keep current backend external contracts unless measured runtime requirements necessitate validated supply changes.
- Invariants: no blanket Owned on ordinary concrete creation; cyclic payload requires Owned; builder result and cleanup before publication; upgrade secures strong count before object-pointer read; no resurrection; source concurrency remains excluded.
- Exit: T18 passes V4–V8/V11; arc protocol ordering review and controlled native interleaving/fault adapters supplement IR tests. Race tests do not introduce source threads. Risks: use-after-free, count overflow, object identity conflation, unsupported alignment.

### M11 — Sequences, collections and iteration

- Requirements: R6–R7/R9–R10/R13–R15/R24; depends on M4, M6–M8. Dictionary work additionally requires M12/I23 Equatable mappings; run that independent Core slice before I22 Dictionary implementation. Add object-element cases after M10.
- I21: complete Core Index/Range/ResolvedRange/Slice, metadata, fixed-array length integration and shared access. Extend `Binding.Elements.cs`, `ElementAccess`, ownership element paths and lowering; add **new** `BodyLowering.Sequences.cs` for bounds/view operations and for protocol calls. Preserve existing local/parameter static element paths.
- I22: implement specified Array/Dictionary construction and mutation via **new** `Kimi/Compiler/Emission/CollectionRuntime.ll.in` and/or generated Core bodies, using shared layout/effect/cleanup metadata. Private contiguous Array and reusable Dictionary slots/order links are a starting proposal under D7, not mandated ABI. Implement consuming and shared iterators using committed Core witnesses.
- Invariants: Slice methods O(1) without per-element allocation; empty Slice retains whole-storage Loan; dynamic Non-Copy index reads borrow rather than Move; lookup versus insertion argument order differs; reserve means additional; within-capacity operations/churn allocate no internal storage; no dependency subtraction after clear/None/Err; reverse current/insertion cleanup.
- Exit: T19–T21 pass V4–V8/V11, including growth/reindex operation counts. Split delivery into Index/Range, fixed-array Slice, iteration, Array mutation, Dictionary mutation; each uses prior closed capabilities and keeps unsupported later APIs rejected. Risks: quadratic churn/order maintenance, hidden equality/destructor effects, conservative dependency loss.

### M12 — Core comparison, Stringify and interpolation

- Requirements: R4/R6–R7/R15; depends on M5/M7–M8; collection equality dependencies can use completed Contract mappings before all M11 runtime work.
- I23: complete Core Stringify/Equatable/Comparable identities/mappings and operator selection using existing Contract machinery. Preserve IEEE float operators while intrinsic Equatable treats same-Type NaNs and signed zeros as specified.
- I24: extend interpolation Binding/ownership and `BodyLowering.Strings.cs`, `LlvmModuleWriter.Strings.cs`, runtime allocation with builder scratch reuse: each embedded value shared once, mapping invoked once, independent owned result appended before next value. Document implementation-defined locale-independent float spellings in usage/profile documentation during implementation.
- Invariants: no Non-Copy input Move merely for printing; shared operation Loans end correctly; actual Heap strings release exactly once; string concatenation remains deferred under §13.3.
- Exit: T2/T14/T21/T22 string cases pass V4–V8/V11, including heap failure injection and exact UTF-8/NUL output. Risks: locale dependence, double rounding/formatting, treating Static-handle audit as heap coverage.

### M13 — FFI, Library output and native connection

- Requirements: R16–R17/R21/R23; depends on M3–M5; instantiate generic/library bodies after M8.
- I25: implement safe versus unsafe raw-pointer operations and `#LibraryImport` direct ABI using shared declarations/calls, checked pointer instruction premises and C-layout facts. Extend existing Binding/physical ABI paths; add **new** `ForeignFunctionAbi.cs` only for the actual distinct C contract. Test valid pointers obtained from a controlled foreign fixture, not invented public raw-storage APIs.
- I26: extend `LlvmEmitter.CheckInputs`, `EmissionArtifacts.Publish`, `NativeToolchain`, native input configuration and manifest/build records for Library inspection, common source-dependency generation, owner-qualified requirements/supplies, COFF/archive member closure and directives. Preserve fail-before-publication and actual symbol inspection; reject unsupported Library native executable requests.
- Invariants: selected unused bodies still checked; Library no OS entry/export ABI; no C aggregate passing; scalar/opaque pointer C calls use §22.3.2; tools run as argument vectors; five-minute tool deadline/cancellation, no fixed product run timeout.
- Exit: T22–T25/T31 pass V4–V9, actual O0/O2 C signature checks and backend audits included when supply changes. G4 blocks native evidence. Risks: false archive allowlists, implicit CRT/TLS/default-library dependencies, mismatched immutable outputs.

### M14 — Source packages, stores and semantic reuse

- Requirements: R18–R19/R21/R24; depends initially on M3, then completed semantic records/effects/generation contracts from relevant milestones.
- I27: add **new** `SourcePackage.cs`, **new** `ContentStore.cs` in `Kimi/SolutionAndProject/`, and **new** `PackCommand.cs`, **new** `PublishCommand.cs`, **new** `StoreCommand.cs`. Implement §18.6 manifest/archive validation, canonical streaming hashes, closure packing, local destination-scoped release transactions, bounded decompression and store verification/pins. Pack validates the final Package graph and never reserves versions or silently rewrites them.
- I28: add **new** `Kimi/Compiler/Core/SemanticRecord.cs` and **new** `SemanticCache.cs`; version verified compiler-owned plans separately from input integrity/native summaries. Begin whole-module reuse; add reverse dependencies, structural correspondence, effect/absence invalidation and current-source remapping only when complete. Regenerate ABI/frame/budget/IR/object plans.
- Invariants: one fixed byte snapshot through check/pack, SourceId is exact manifest-based domain hash, compression doesn't define identity, module versions retain distinct Types; publisher verification claims not trusted; cached corruption never hides package corruption; no load-order-sensitive acceptance.
- Exit: T24–T25 pass V4/V8/V9/V11, with no-cache/cold/warm equality and interrupted/concurrent publication. Risks: incomplete pins/collection, ZIP path collisions, false hash trust, stale private effects or locations.

### M15 — Mod semantics and separately blocked host integration

- Requirements: R1/R2/R20/R23; depends on M3/M7 and source context/invalidation foundations.
- I29: add **new** `Kimi/Compiler/Core/ModPipeline.cs` as an internal scheduler; implement dependency ordering, per-invocation snapshot queries, append-only selected declarations, provisional Binding availability/invalidity and final pass. Use private test doubles and new SourceDocuments for generated fragments; don't mutate original syntax or rerun completed Mods.
- I30: external host/configuration/query adapter is `BLOCKED` by G2 and excluded API design. Concrete production symbol/path is intentionally undecided until the separate contract exists.
- Invariants: no per-instantiation execution, function-body injection or general Koto rewriting; generated source saves are not automatically new ordinary inputs; raw observed bytes invalidate Mod output; compilation is not finalized before generated dependencies stabilize.
- Exit: internal T27/V4/V8 can complete independently. M15 as a whole stays blocked at integration until G2 resolution and its adapter tests; pack continues rejecting Mod-dependent inputs under §18.6.1. Risk: stale semantic query handles and undocumented host observability.

### M16 — Test language and isolated execution

- Requirements: R2/R7/R18/R22/R24; depends on M3 membership foundation, M4/M6 checking, M8 budget isolation, M9 for nested Closure coverage, M13 runtime/artifacts. Independent membership checks can start after M3.
- I31: implement Test membership/Attribute eligibility, source/test partitions and verified discovery; add **new** `Kimi/Compiler/Binding/Binding.Tests.cs` and internal case catalog. Product binding never acquires test candidates; every test body is checked before list/filter, including test-only helpers.
- I32: add missing test statement representation only after inspecting current `$` syntax handling; proposed **new** `TestVerificationKoto` if existing nodes cannot retain the required standalone-item/control boundary. Extend parsing/writing/reload, ownership and lowering for once-only bool/snapshots, basic failure before cleanup/message, lazy messages, outward-message transfer rejection and expect/require continuations. Verify internally through a private failure-event adapter; never treat it as the public protocol.
- I33: **new** `TestCommand.cs` and production runner/reporting/OS process-management adapter are `BLOCKED` by G1. After separate profile specification, implement one immutable all-case artifact, stable IDs, one managed child per case, finite monotonic deadlines/recovery, bounded channels/storage and reliable require-Abort identification. Do not publish interim arbitrary schemas/defaults.
- Invariants: false require does not return or unwind; failed cleanup/message retains basic failure plus actual termination; zero exit without completion is failure; product/test generation budgets independent; no process reuse, source parallelism or implicit retry.
- Exit: T28–T30/V4/V8 semantic portion first; V10/V11 public end-to-end requires G1. No claim of complete test execution from parsing, private adapter events or `--list`. Risks: output saturation, descendant pipe retention, stale ArtifactId and lost failure before Abort.

### M17 — Conformance, performance and documentation closure

- Requirements: all R; depends on all applicable milestones, with G1–G2-dependent completion explicitly unresolved.
- I34: close G5, execute impact-based regression then final V3–V12. Tune measured bottlenecks without weakening normative acceptance or extending deferred syntax. Document mandatory collection bounds and actual compiler/runtime measurements independently.
- I35: update `STATUS.md` with product-wide support levels/input limits and concise evidence; update `SPEC.md` only for index/navigation changes and owning spec sections only for required implementation-defined choices or separately resolved decisions. Update `README.md`, executable examples and `examples/SpecTour/README.md` to distinguish supported versus illustrative APIs. Keep detailed execution history here; do not edit `draft/` automatically. Update `.github/workflows/test.yml` for explicit managed configuration, nonzero counts, retained reports and ordinary Windows native coverage; publishing is separate.
- Exit: §11 satisfied, every in-scope R linked to current passing evidence, and no hidden unsupported range. If interfaces remain unsettled, report completion of the defined subset with M15/M16 blocked; do not rename that result “complete compiler.”

## 7. Implementation Checklist

All unchecked items are `TODO` unless explicitly `BLOCKED`. Completion is recorded here and in §2 with evidence, not by deleting items.

| ID | Milestone / requirements | Implementation action / state |
| --- | --- | --- |
| I1 | M1 / all R | [ ] Clause-to-existing-test inventory and current support audit |
| I2 | M1 / R23–R24 | [ ] Reproducible runner, artifact and environment baseline |
| I3 | M2 / R1–R4/R9 | [ ] Close observed lexical, static, access, constant and inference gaps |
| I4 | M2 / R3/R23 | [ ] Remaining specified numeric conversions and diagnostics |
| I5 | M3 / R2/R18 | [ ] Source DAG, snapshots, configuration and lock model |
| I6 | M3 / R18/R21 | [ ] Restore/check commands and lock lifecycle |
| I7 | M4 / R4/R6/R12 | [ ] Origins, recursive effects and completed public guarantees |
| I8 | M4 / R6–R7/R9 | [ ] General Loans, reference ABI and ownership/result checking |
| I9 | M5 / R2/R8 | [ ] Property/constructor/deinit binding and struct/C layout |
| I10 | M5 / R7–R8/R16 | [ ] Field execution, partial construction, deinit and statics |
| I11 | M6 / R6–R7/R9–R10 | [ ] Complete enum/Pattern acquisition and guard semantics |
| I12 | M6 / R7/R9–R10 | [ ] Tagged layout, enum dispatch and cleanup generation |
| I13 | M7 / R3–R6/R10 | [ ] Universal bodies, constraint and associated-Type proofs |
| I14 | M7 / R4–R6 | [ ] Full specialization and closed-family selection |
| I15 | M8 / R5/R21 | [ ] Metadata, shared entry/context generation and recursion limits |
| I16 | M8 / R5/R7/R24 | [ ] Exact frames, bounded optional specialization and budget isolation |
| I17 | M9 / R4/R6/R11 | [ ] Function Items/capture/callable semantic checking |
| I18 | M9 / R7/R11 | [ ] Callable storage, indirect ABI and cleanup |
| I19 | M10 / R6/R12/R15 | [ ] Specified object/Weak catalog, operations and runtime tests |
| I20 | M10 / R7/R12 | [ ] Object/Weak states, counts, ordering and final release |
| I21 | M11 / R6/R9/R13 | [ ] Sequence metadata/views and non-lending iteration |
| I22 | M11 / R7/R10/R14/R24 | [ ] Array/Dictionary operations, dependency/effect/cost guarantees |
| I23 | M12 / R5/R15 | [ ] Core comparison and Stringify mappings |
| I24 | M12 / R4/R7/R15 | [ ] Heap-string interpolation and formatting |
| I25 | M13 / R17/R21 | [ ] Raw-pointer instructions and exact C imports |
| I26 | M13 / R16/R21 | [ ] Library/common output, native closure and publication |
| I27 | M14 / R18–R19 | [ ] Canonical packages, local stores and transactional commands |
| I28 | M14 / R19/R23–R24 | [ ] Bounded verified semantic records/reuse/invalidation |
| I29 | M15 / R1–R2/R20 | [ ] Internal Mod schedule, snapshots and provisional Binding |
| I30 | M15 / R20 | [ ] `BLOCKED`: public host adapter requires G2 |
| I31 | M16 / R2/R18/R22 | [ ] Product/test membership and verified discovery |
| I32 | M16 / R7/R22 | [ ] Standalone test verifications and cleanup-aware continuations |
| I33 | M16 / R21–R22 | [ ] `BLOCKED`: public runner requires G1 |
| I34 | M17 / all R | [ ] Current conformance/regression/performance evidence |
| I35 | M17 / R23–R24 | [ ] Product status, documentation/examples and verification CI |

## 8. Test Plan and Verification Records

### Test conventions

Reuse `ParseTestHelper` for syntax-only cases; `MinimalEmissionTest.Analyze` for prepared target → Binding → startup → ownership; `ScalarEmissionTest.EmitFixture` / `WriteFixture` for IR plus expected output; `StringEmissionTest.WriteAuditedFixture` and `StringLifetimeAudit` for logical destruction counts/order. Use `AllocationMeasurement.Measure` for warmed isolated-thread allocation checks. Its 30-second thread join throws on timeout; it does not forcibly terminate a hung measuring thread, so the outer test process also needs a deadline.

Test existence and source inspection are listed below; **no case is recorded as currently passing**. “Extend” means proposed cases in an existing class, “new” means proposed class/file. All classes named here map to `xUnitTest/Tests/<Class>.cs`. Positive/Negative/Boundary/Interaction/Regression variants belong to the same stable T family; append suffixes when splitting, retaining the parent ID. No exhaustive irrelevant cross-product is required; Appendix A's specified boundaries and distinct semantic paths must be covered.

| ID / requirements | Input and setup | Expected result; verification stage / observable evidence | Existing or proposed location |
| --- | --- | --- | --- |
| T1 / R1/R23 | Valid UTF-8; malformed byte sequence; decomposed identifier; Unicode line endings, indentation/continuation and Body forms | Parse exact valid forms; reject encoding/NFC/layout error at original document/span; parse/write/parse preserves Body form | Extend `SourceEncodingTest`, `UnicodeIdentifierTest`, `SourceDocumentAndDiagnosticTest`, `ControlFlowConformanceTest`; V4/V8 |
| T2 / R1/R3/R15 | Direct literal `5000000000@f64`, `-0.0@f32`, `3.5e38@f32`, decoded NUL string and reload | Exact one-round fitting; direct overflow is Binding error at literal/adaptation; byte/bits preserved through reload and emission | Extend `NumberLiteralParseTest`, `FloatConversionEmissionTest`, `StringLiteralParseTest`; V4–V8 |
| T3 / R1–R2 | Valid/invalid reached Conditions, unknown name inside unselected switch arm, same name within false if target; merged source fragments with different aliases | Respect §19.5 validation boundary; no alias leakage/extra scope; current source locations retained | Extend `DirectiveConditionValidationTest`, `CompilationSpecificationTest`, `BindingTest`; V4/V8 |
| T4 / R2/R8 | Multi-file fragments; inherited same-name member; private Type in public/internal signature; Layout Attribute conflict | Correct declaration/Binding access/fragment failures even unused; failures identify offending header/use rather than later unsupported emission | Extend `InheritedReceiverBindingTest`, `TypeBindingTest`, `PropertyBindingTest`; new `LayoutEmissionTest`; V4/V8 |
| T5 / R2/R4 | Named/default arguments and receiver in non-first parameter position; incomparable candidates; selected candidate fails borrow/access | Receiver and arguments execute once in specified order; default uses definition environment; fail intended selected use, no fallback candidate | Extend `BindingTest`, `FunctionEmissionTest`, `ReceiverShorthandTest`; V4–V8 |
| T6 / R3 | `let x: f64 = 1.25` then `let y = x@f32`; float-to-integer fractions, adjacent representable boundary floats, NaN/infinities/±0; allowed integer widths | Native truncation/rounding/checks match §13.5.4; failures Abort at conversion, literal-fit failures static. Explicitly test prohibited typed 128-bit↔float and bool/char conversions | Extend `FloatConversionEmissionTest`, `ConversionEmissionTest`; V4–V7 |
| T7 / R3/R16 | Signed minimum / -1 including remainder; shift width -1/width; overflow in executed ordinary expression versus required length constant; i128 helper-requiring operations | Runtime Abort versus constant-evaluation error correctly separated; actual object dependencies validated; no overflow/poison suppressed at O2 | Extend `IntegerEmissionTest`, `WideIntegerEmissionTest`, `DivisionEmissionTest`, `BitwiseEmissionTest`; V4–V7 |
| T8 / R3/R13 | Fixed array lengths 0, dependent N, invalid negative intermediate, zero-stride elements; named length and literal-only index comparison | Correct formation proofs/deadlines; zero-size values retain semantics; arithmetic/name index cannot become Move Path through folding | Extend `TypeBindingTest`, `ElementPathEmissionTest`; new `GenericGenerationTest`; V4–V8 |
| T9 / R4/R7/R9/R16 | `var x = 2` then `let y = x + x++`; RHS-first self-replacement, target-first compound assignment, named result transfer with defer/Abort/divergence | Snapshots/order/counts; secure before cleanup, no result delivery after non-normal cleanup; invalid premature physical consumption refused | Extend `ScalarEmissionTest`, `DeferredEmissionTest`, `AggregateResultEmissionTest`, `EmissionPlanTest`; V4–V8 |
| T10 / R6/R9/R12 | require failure body that can continue, including `require true`; object struct tests, Move/replace refinement invalidation; dead checking continuations | Flow/Binding errors at intended require/use; truth doesn't hide invalid failure body; live paths never regain Moved values or ended Loans; runtime test evaluates once | Extend `SpecRevisionAnalysisTest`, `RuntimeTypeTest`, `UnreachableOwnershipTest`; V4–V8 |
| T11 / R6–R8/R13 | P2 below; returned local borrow; simultaneous shared arguments; nested ref/uniq reborrow; borrowed Non-Copy extraction; mutation while Slice remains live | Legal shared results stay valid; escape/conflict/missing-part rejected in ownership/use legality at use; parameter immutability remains Binding error | Extend `OwnershipAnalysisTest`, `ElementParameterMoveEmissionTest`, `ElementBorrowOwnerEmissionTest`; new `BorrowEmissionTest`; V4–V8 |
| T12 / R7–R8/R16 | Constructor/base fields with logged deinit; partial transfer; custom getter/setter self-assignment; static first-write/cycle and shutdown reentry | Per-layer initialization and reverse logical cleanup; no getter backing bypass; cyclic access Aborts at operation; no unused static eager init | Extend `PropertyBindingTest`, `StartupBindingTest`; new `PropertyEmissionTest`, `StaticInitializationEmissionTest`; V4–V8 |
| T13 / R7/R9–R10 | P3 below; nested Tuple/enum, guarded arm then catch-all, shared/non-Copy payload, duplicate fitted literal, `.Some`/`.None()` invalid | Complete coverage under specified structural rule; exact Case construction/acquisition/cleanup, warning at later contained Pattern; invalid shape fails Binding before emission | Extend `EnumBindingTest`, `EnumOwnershipTest`, `PatternBindingTest`, `MatchOwnershipTest`; new `EnumEmissionTest`; V4–V8 |
| T14 / R5/R10/R15 | Canonical and reordered Core Option/Result condition atoms; wrong same-spelled Copy identity; inherited associated Types and conditional witnesses; user Stringify/comparison | Correct nominal intrinsic identity and required mappings; no unresolved/cyclic proof published; selected map survives reload | Extend `CoreCatalogTest`, `CoreModelTest`, `ConditionalConformanceBindingTest`, `ContractBindingTest`; V4/V8 and V6 after generation |
| T15 / R3/R5/R24 | P4 below; length-generic arrays, mandatory specialization at optional budget zero; finite mutual recursion versus growing Type keys; shared body with same-size/different-cleanup Types | Generic invalidTwice rejected at definition even unused; valid transfer works for Copy and Non-Copy. Stable selection/entry ABI, exact frames, bounded required-generation diagnostic | Extend `ConstraintBindingTest`, `TypeBindingTest`; new `GenericGenerationTest`; V4–V8/V11 |
| T16 / R5–R6/R11 | Same generic/callable call with permuted argument discovery order, multiple input-Origin meets, indirect/recursive default or destructor static effect | Principal inference stable; no hidden lifetime/capability requirement, no unsound static access while Loan active; invalidate after effect/specialization change | Extend `ConstraintBindingTest`, `ContractBindingTest`; new `BorrowEmissionTest`, `CallableEmissionTest`; V4–V8 |
| T17 / R6–R7/R11 | Empty/inline/heap Closure captures, explicit unused capture, repeated Shared calls, Consuming partial Move, borrowed capture escape | Capture/call/cleanup order; reject escape/illegal receiver in semantic phase; native no duplicate acquisition/destruction and exact result lifetime | New `CallableEmissionTest`, extend `FrontEndSyntaxTest`; V4–V8/V11 |
| T18 / R6–R7/R12 | §13.5.9 `expired()` and cyclic Node examples; clone/drop in different views, count maximum and allocation-failure adapters; upgrade before/after publication/final release | Weak absence versus expiry distinct; one payload cleanup; no resurrection/freed-pointer reads; exact dynamic Supports and arc ordering. No undefined source concurrency tests | New `ObjectEmissionTest`, extend `RuntimeTypeTest`; native adapter additions under `backend/windows-x64/tests/` (new cases); V4–V8/V11 |
| T19 / R6/R13 | P5 below; ^0/^1, negative/saved Index, inclusive/reversed ranges, empty split, shorter target reuse, nested reference Slice and tryGet | Correct None versus Abort, once-only bounds location, Copy read versus explicit slot borrow, persistent source Loan for empty view; O(1) view operations | Extend `RangeIndexParseTest`, `ElementEmissionTest`; new `SequenceEmissionTest`; V4–V8/V11 |
| T20 / R6–R7/R9–R10/R13–R14 | for over owned arrays/dictionary and shared Slice, immediate exit/continue/outer return, Tuple binding arity, iterator stays exhausted | Single iterable acquisition; short next Loan ends before body; binding then remaining iterator cleanup correct; Range (unresolved) iteration rejected at conformance check | Extend `ForParseTest`; new `SequenceEmissionTest`, `CollectionEmissionTest`; V4–V8 |
| T21 / R7/R14–R15/R24 | P6 below; §4.7 mutation matrix, duplicate literal separated by nonliteral; runtime duplicate with effectful value; NaN/±0 keys; reserve/shrink, full-capacity churn | Static duplicates at later key when eligible; runtime duplicate before later value; insertion identity/order; Result owns rejected inputs; zero internal allocations within capacity; operation-count growth bounds | Extend `CollectionLiteralParseTest`; new `CollectionEmissionTest`; V4–V8/V11 |
| T22 / R8/R15–R17/R21 | P1/ P7; interpolation of scalar and user Stringify, C NativeRecord (§21.1.3), small signed/unsigned/mixed FP/5+ C arguments, valid opaque-pointer round trip, runtime fault adapters | Exact bytes/format rules; C size/offset/ABI matches; forbidden bool/aggregate import fails source ABI check; runtime allocation/write/free failures retain provenance; no promised unsafe-violation output | Extend `MinimalEmissionTest`, `StringEmissionTest`, `CharEmissionTest`; new `InterpolationEmissionTest`, `ForeignFunctionEmissionTest`, `LayoutEmissionTest`; V4–V9 |
| T23 / R2/R18/R21 | P8 two-Project fixture; diamond aliases, two versions, cycle, mismatched content and inaccessible transitive name; nonempty old KotonohaArray | Binding uses direct reference and definition contexts; conflict reports both graph paths; old setting receives migration reason; check does not run native tools | New `DependencyResolutionTest`; extend `SolutionInputTest`; V4/V8/V9 |
| T24 / R18–R19/R21 | restore product success/test failure; source edit/relocation without edge change; corrupt stale empty lock; package trials then same-release conflicting publish; races/interruption | Restore-only lock updates with both partitions; no unnoticed stale lock; full closure integrity/semantic validation; publication all-or-nothing mappings with exact SourceIds | New `DependencyLockTest`, `SourcePackageTest`; V4/V8/V9 |
| T25 / R5/R19/R21/R24 | Same graph no-cache/cold/warm; private effect/absence/specialization edits, comment/location change, corrupt semantic/package bytes; reused product generic with added tests | Equal semantic decisions, current diagnostic locations and changed relevant artifact IDs; recompute dependent proofs, no Type merging across versions; measure lazy body loads/hash/read/retained memory | New `SemanticReuseTest`, extend `KotonohaSerializationTest`, `EmissionArtifactsTest`; V4/V8/V9/V11 |
| T26 / R9/R16/R23 | Inferred Unit through discarded selection/do, discarded Result versus effect-free value, final defer, covered/unreachable errors | Exact warning priority/count at source discard; no call-purity assumption; mandatory unused-body diagnostics before optimization | Extend `SpecRevisionAnalysisTest`, `ControlFlowConformanceTest`, `SpecReviewTest`; V4/V8 |
| T27 / R1/R2/R20 | §20.7.7 two-step model through private test doubles; cycle/missing prerequisite, semantic query then append, independent generated source context, changed observed comment | Once/order, query invalidation, final binding and original diagnostic provenance; no original source rewrite; external adapter tests withheld until G2 | New `ModPipelineTest`; V4/V8 |
| T28 / R2/R18/R22 | P9 below; invalid Test signature/Attribute, direct call/value acquisition, helper in TestSources versus product file, erroneous filtered-out body | Product membership unchanged; test bodies checked before list/filter; prohibited context/target fails correct parse/Binding stage; list has no generation/native side effect | New `TestMembershipTest`, `TestDiscoveryTest`; V4/V8/V10 |
| T29 / R5/R22/R24 | Add unrelated inline tests and test-only generic substitutions; change filter/case; budget zero/nonzero | Product lookup/layout/sharing/frames/budgets unchanged; current locations may change; one all-case artifact across filters, semantic-only list no generation | New `TestGenerationTest`; V4/V8/V10/V11 |
| T30 / R7/R22/R23 | P9; side-effecting condition, short circuit, lazy message with Move, nested failure, cleanup/message Abort before require reaches Abort; output/ID saturation, corrupt completion, descendant retains pipes | Basic failure before cleanup/message; once evaluation; actual termination retained, no enclosing defer after Abort; bounds omit storage only; recovery finite and failure never misclassified success | New `TestVerificationTest`, `TestRunnerTest`; V4/V8 internal subset, V10/V11 public subset blocked by G1 |
| T31 / R21/R23 | Fresh emit/build/run; failed build after previous success; corrupt executable/hash, changed output settings, mismatched backend/library directive; paths with spaces | No mixed publication; emit no tools, build no execution, run no rebuild; exact exit/cancellation/output contract and actual native member closure | Extend `EmissionArtifactsTest`, `NativeToolchainTest`, `ToolchainResolverTest`; backend CLI/artifact scripts; V4–V9 |
| T32 / R24 | Representative small/large sources, sparse large arrays, many-path DAG, generic recursion, cold and warm reruns, failed then successful analysis, reload dropping old tree | Measure time/allocations/peak and retained memory, no growth proportional to repeated warm attempts or unreferenced syntax; preserve relevant existing zero-allocation tests | Existing `AllocationMeasurementTest`, allocation cases and `Benchmark/Benchmarks/`; new benchmark cases only for uncovered workloads; V11 |

### Representative complete inputs

These are proposed test inputs, not claims they compile today. Comments state the intended check, and native assertions must use exact expected bytes/events. Additional family variants derive from their cited owning clauses.

**P1 — existing minimal native regression (T22).**

```kimi
::Core.writeLine("Hello, world!")
```

Expected program stdout is exactly `Hello, world!\n` (14 UTF-8 bytes), stderr empty, exit 0. Exclude CLI settings-summary output from the program-byte assertion.

**P2 — initialization history and static partial Move (T11).**

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0
let age = pair.1
pair.0 = "Bob"
let all = pair
::Core.writeLine(name)
::Core.writeLine(all.0)
```

Expected stdout `Alice\nBob\n`, all remaining owned parts destroyed once. Negative variant inserts `let invalid = pair` before repair; it must reach ownership and fail for incomplete/Moved use at that identifier, not parsing or startup.

**P3 — executable Option payload (T13).**

```kimi
let value: Option<string> = .Some("payload")
match value
    .Some(let text) => ::Core.writeLine(text)
    .None => ()
```

Expected stdout `payload\n`, one string responsibility. Negative variant removes `.None`; require non-exhaustive Binding diagnostic at match. Do not add a fallback because only Some is constructed here.

**P4 — universal body checking (T15).**

```kimi
func transfer<T>(value: T) -> T => value
func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
func invalidTwice<T>(value: T) -> (T, T)
    return (value, value)
()
```

The last function must fail definition checking at reuse even without calls; `()` supplies valid startup. Remove it for positive tests invoking transfer with i32 and string and twice with i32. Existing successful instantiations cannot certify the negative generic body.

**P5 — Slice Loan conflict (T19).**

```kimi
var values: [3 of i32] = [1, 2, 3]
let shared = values[..]
values[0] = 10
let first = shared[0]
```

Require the mutation's Loan conflict after sequence Binding succeeds. Positive counterpart omits the mutation and checks shared[0] = 1 by an ordinary conditional output. Another negative writes `shared[0] = 20` and must fail read-only element permission, not the prior whole-array conflict.

**P6 — collection receiver activation (T21).**

```kimi
var values: Array<i32> = []
values.reserve(additional: 3)
values.append(10)
values.insert(^0, 30)
values.insert(^1, 20)
let last = values.remove(^1)
let first = values[0]
values.append(first)
```

Expect final order [10, 20, 10], last = 30, unchanged capacity for remove and in-capacity addition. Negative counterpart `values.append(values[0])` must reach Loan checking and fail because the receiver is already exclusively borrowed. Instrument internal allocation separately from argument/destructor costs.

**P7 — numeric conversion (T6/T22).**

```kimi
let value: f64 = 3.9
let result = value@i32
if result == 3 => ::Core.writeLine("ok")
```

Expected `ok\n`, empty stderr, exit 0. Replace value with a runtime expression yielding infinity to require conversion Abort at `value@i32`; direct literal overflow is a different Binding case.

**P8 — source dependency (T23).**

Use §18.4.2's `example.math` 1.2.0 Library with `public group Arithmetic` and `public func twice(value: i32) -> i32 => value + value`. The consuming Project declares `Dependencies.Math={PackageId="example.math" PackageVersion="1.2.0" Project="../Math/Math.kimiproj"}` and target `x86_64-pc-windows-msvc`; source:

```kimi
alias Math.Arithmetic
if twice(3) == 6 => ::Core.writeLine("ok")
```

Restore fixes the graph; check succeeds without native tools; build/run later print `ok\n`. All fixture projects are created in a unique test workspace, not by altering repository examples.

**P9 — require Abort (T28/T30), in a test build with no top-level runtime body.**

```kimi
group Verification
    #Test
    func stops()
        defer => ::Core.writeLine("cleanup")
        $expect(false, message: "first")
        $require(false, message: "second")
        ::Core.writeLine("after")
```

Two failures are retained in order. Neither `cleanup\n` nor `after\n` is printed. Require's termination is reported separately from failure records; no normal completion. Public wire bytes/exit allocation await G1; internal semantic assertions do not invent them.

### Verification procedures

All commands use the repository working directory from §2. `Debug`/`Release` below are compiler configurations; language `--Debug true`/default Release and native `O0`/`O2` are separate axes. Existing command signatures were inspected; only V1–V2 probes were attempted. Commands for **new** targets are explicitly proposed. Stop on failures; do not allow a downstream step to consume older successful outputs.

| ID / kind | Commands, prerequisites and exact target | Success criteria / ordering and output constraints |
| --- | --- | --- |
| V1 / plan and source audit | `git rev-parse HEAD`; `git status --short`; `rg --files`; targeted file reads; final `git diff --check` and plan-reference audit | Current commit and changes recorded; all existing references resolve; new items labeled; requirement/test coverage complete; only PLAN.md changed in planning |
| V2 / environment | `dotnet --info`; `dotnet test --help`; `./toolchain/opt.exe --version`; `./toolchain/llc.exe --version`; `./toolchain/clang.exe --version`; `./toolchain/lld-link.exe --version`; `Get-FileHash -Algorithm SHA256 -LiteralPath toolchain/windows_x64/kimi_backend_windows_x64_v1.lib,toolchain/llvm-dlltool.exe` | SDK/runner/profile/hashes verified separately. Help may evaluate projects: after G3 use explicit project, `--no-build --no-restore --help` to obtain extension/filter syntax; no tests or zero-count inference from help. Recheck every actual required tool before native acceptance |
| V3 / compiler and test build | If needed in implementation environment: `dotnet restore Kimigayo.slnx`. Then `dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal`; later same with `-c Release` | .NET 10, package access/restore assets required. Build exit 0; record warnings/errors and compiler DLL hash/MVID. Do not use IsAotCompatible as a request to publish/test NativeAOT. Serialize configurations sharing outputs |
| V4 / managed checks and fixture generation | `dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore --minimum-expected-tests 1`; repeat for Release only after that V3 build. Focused **proposed** suffix `--filter-class 'XunitTest.FloatConversionEmissionTest'` (or exact class in the T row) must be confirmed by V2 extension help before use | Correct configuration/assembly, nonzero selected count, no failures/unexpected skips; retain counts and targeted class list. First focused checks per I item, then impacted families, full suite at integration. Base MTP options verified by help; class-filter spelling still unverified. No VSTest filter assumption |
| V5 / LLVM verification | Example after current Float conversion fixtures: `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/FloatConvertRoundTrip.ll`. Repeat for each current fixture and optimized IR. Prerequisites V3/V4, G4 resolved | Pinned LLVM 22.1.8 accepts exact hash-matched IR; does not prove execution, layout or C ABI. Record fixture hashes and source/configuration. No fixture generator running concurrently |
| V6 / ordinary native fixture execution | `pwsh -NoProfile -File backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'FloatConvert*.ll'`; use exact family patterns after verifying their generated membership; final `-FixturePattern '*.ll'` only against audited current inventory | Script verifies, optimizes, emits objects, inspects dependencies, links /NODEFAULTLIB and runs O0/O2. Compare stdout/stderr/exit/destruction order; 30-second normal fixture deadline, per-fixture `.timeout` for divergence and process-tree kill. Count expected fixtures × 2; zero runs fail. Output `bin/scalar-native` shared across configurations |
| V7 / runtime/backend/layout/ABI | `pwsh -NoProfile -File backend/windows-x64/build.ps1 -ToolchainRoot ./toolchain`; then `pwsh -NoProfile -File backend/windows-x64/test-emission.ps1 -ToolchainRoot ./toolchain -Configuration Release` (or Debug following its fixture generation) | Inspected scripts require tested pinned backend candidate under `backend/windows-x64/bin/verification.json`, matching archive and current configuration emission fixtures. Capture C ABI/layout comparisons, unwind/undefined-symbol checks and fault adapters. Rebuild script can install a matching verified backend reproduction; inspect these writes in implementation phase. G4 currently blocks |
| V8 / serialization/rebinding/reuse | V4 focused on `KotonohaSerializationTest`, `ReceiverShorthandTest`, `FloatConversionEmissionTest`, `EmissionPlanTest`, and **new** SemanticReuse/GenericGeneration/Mod/TestGeneration cases once added | Save/load creates fresh contexts; rebind invalidates ownership/emission until rerun; corrupt plans reject before text publication; no-cache/cold/warm and load-order acceptance equal. Record exact test list/filter after V2; no separate native claim |
| V9 / CLI and artifacts | `pwsh -NoProfile -File backend/windows-x64/test-cli.ps1 -ToolchainRoot ./toolchain -Configuration Release`; `pwsh -NoProfile -File backend/windows-x64/test-artifact-paths.ps1`; after V3: `dotnet Kimi/bin/Release/net10.0/Kimi.dll build examples/Hello/Hello.kimiproj --ToolchainRoot ./toolchain`, then `dotnet Kimi/bin/Release/net10.0/Kimi.dll run examples/Hello/Hello.kimiproj` | CLI script creates unique workspaces, compiler-call deadline 60 seconds; capture JSON and hashes, expected summary separate from child output. New restore/check/pack/publish/store and Library/FFI cases extend harness after implementation. Run only local fixture stores; public publication unnecessary. Existing .exe alone is not a fresh build certificate |
| V10 / language tests | **Proposed after I31/I33:** `dotnet Kimi/bin/Release/net10.0/Kimi.dll test <fixture-project> --list`; same with `--filter Arithmetic`; `--case <CaseId>` obtained from verified list; `--no-parallel`; `--jobs 2`. Concrete fixture path/IDs recorded at execution, not invented now | G1-resolved options/protocol and fresh compiler/artifact required. Verify all bodies before selection, bounded logs/descendants/recovery and identity, once-per-case isolation, failure continues other cases, unchanged all-case artifact across filters. Additional deadline/budget option commands remain blocked pending contract |
| V11 / performance | Existing command pattern: `dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*'`; analogous exact registered `*OwnershipAnalysisBenchmark*` / `*FrontEndBenchmark*`. Allocation assertions via V4; **new** collection/generic/cache/runner/native performance harnesses require I items and baseline workloads first | Record environment, cold/warm setup, source size/hash, warmup, repeats, time dispersion, allocated bytes, retained/peak memory, code size and program time. M3/M14 also bytes read/hash passes/judgments/bodies; M11 operation/allocation counters; M16 startup/drain/recovery cost. Run benchmarks alone, same inputs/config/toolchain, preserve required checks. No speedup assertion from one noisy run |
| V12 / final integration and documentation | V1 coverage audit, one justified full V3/V4 per configuration, fresh fixture inventory then V5–V10, V11 representative baselines; review STATUS/SPEC index/README/examples | No unresolved in-scope clause hidden by a subset filter; all outputs traceable; expected rejection cases distinguish deferred rules from temporary implementation gates; documentation reports actual support. NativeAOT is NOT_APPLICABLE, not a missing mandatory gate |

**Artifact integrity protocol (applies V3–V12).** Record commit plus relevant uncommitted diff, SDK, compiler configuration/DLL identity, semantic target/mode/settings, fixture source, expected files and SHA-256, tool/runtime/backend identities. `bin/scalar-fixtures` is shared by Debug/Release and tests; `bin/emission-fixtures/<Configuration>` is configuration-specific. Before a focused generation, identify exactly which filenames the tests write and isolate only the prior family outputs or use a scoped inventory; do not include stale matching fixtures. Any move/delete needs resolved paths within that owned output directory. After generation, inventory `.ll`, `.stdout`, `.stderr`, `.exit`, optional `.timeout` and audit variants; verify hashes immediately before downstream use. Serialize generation/native execution/rebuild. Retain immutable per-run summaries under a new implementation-time subdirectory of ignored `TestResults/` or `bin/`; never treat directory existence as success.

**Process discipline.** NativeToolchain has five-minute native-tool bounds; script compiler calls and runtime fixtures have their own bounds above. Several direct script tool invocations and stream EOF reads are not independently bounded; I2 must add an outer process-tree deadline/drain bound before testing hostile/hanging inputs. Managed full runs and benchmarks need an explicit recorded harness deadline sized from their baseline (proposed initial full-run ceiling: 15 minutes; benchmark ceiling set per selected job), async output draining and tree termination on timeout/cancellation. Such harness limits are not product execution semantics. Never run unbounded `ReadToEnd` on descendants' retained pipes during recovery tests.

### Current verification records

| Verification ID | Target and configuration | Relevant code or artifact state | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Baseline/source architecture/specification/plan audit | `7a83d6bc751f4d02815c3f0b2f1ecfa113672588`; initially clean; new PLAN.md only | PASS | Current git result; referenced test classes checked against inventory with additions marked new; 24 R / 17 M / 35 I / 32 T / 12 V IDs; English-only prose and no trailing whitespace; `git diff --no-index --check -- NUL PLAN.md` exits 0. This is a planning audit, not complete clause-level compiler verification | Each plan/code baseline change and G5 family expansion |
| V2 | .NET environment | Current host | PASS | `dotnet --info`: SDK `10.0.401`, MSBuild `18.9.11+e34a38d2a`, runtime `10.0.12`, win-x64. `global.json` selects Microsoft.Testing.Platform but pins no SDK version | Before executable baseline or environment change |
| V2 | MTP base help and extension discovery | No compiler/test build requested; help evaluated projects | BLOCKED | Base options printed; extension discovery produced NuGet access diagnostic in G3. Outer command exit 0 despite reported build failure. No test ran; no managed conformance failure inferred | Resolve G3, verify exact project-specific filters without implicit restore |
| V2 | Installed backend/dlltool hashes | Current installed files versus `backend/windows-x64/profile.json` | PASS | Backend SHA-256 `4ef5b90f70bf22dea6fd2a6fb495f11025e3ecf4b7afa68b356801c9252c5d26`; dlltool `3733b6d47353a29f18b0e543675d83c525655a7cdfa3fb9c06cfc5d4a1a4fdfc`; both match | Before native verification and any tool/backend change |
| V2 | opt/llc/clang/lld-link banners | Current toolchain files | BLOCKED | Permission denied for each attempted process; no version result established | Resolve G4, inspect every required actual tool |
| V3 | Debug/Release builds | Current checkout; outputs not freshly built | NOT_RUN | Planning did not run a build; help's failed evaluation is not a build result | Required before tests |
| V4 | Managed tests / fixtures | Existing binaries/fixtures not trusted as current | NOT_RUN | Test existence inspected only | Required after V3/G3 |
| V5–V7 | LLVM, native fixtures and runtime/backend | Hash match alone is insufficient | NOT_RUN | Planning did not execute verification/native tests; G4 prevents them in this sandbox | Required after current fixture generation/G4 |
| V8 | Reload/rebind/cache regressions | Current source inspected | NOT_RUN | Existing tests read, not executed | Run affected families after each semantic/cache change |
| V9 | CLI/artifact validation | Existing scripts inspected | NOT_RUN | No project build/run/emit/pack/publish performed | After fresh compiler/toolchain prerequisites |
| V10 | Language test public runner | No integrated implementation; G1 open | BLOCKED | Specified language/runner contracts exceed current code; public profile undefined | After M16 prerequisites and G1 |
| V11 | Allocation/throughput/retention | Current measurements unavailable | NOT_RUN | Existing tests and Benchmark configuration inspected; STATUS performance claims are historical | Establish baseline before tuning; repeat after relevant changes |
| V12 | Full completion | All I items unimplemented in this effort | NOT_RUN | Planning only | After milestone closure |

Historical evidence: STATUS.md §§7.2–7.7 reports previous Debug/Release managed passes and O0/O2 native fixture successes, including recent element/parameter Move work; its receiver-shorthand note reports 390 related passes. These are **historical**, not added to current totals. Do not reuse its source baseline or assume unchanged outputs prove this checkout. NativeAOT remains excluded and was not run.

## 9. Risks and Implementation Notes

| Cause / trigger | Impact | Mitigation | Verification / affected milestone |
| --- | --- | --- | --- |
| Early semantic success used as generation certificate | Invalid unused/captured/generic body emitted or silently omitted | Keep final obligations, all-body verification and freshly checked physical plans; narrow gates only after complete slice | T4/T13/T15/T26/T31, V4/V8; all M |
| Candidate reuse leaks tentative mappings or usage state | Different overload/Origin result by enumeration order | Candidate-local scratch, commit winner once, separate legality after selection | T5/T16/V4/V8; M2/M4/M7 |
| Effects/cyclic conformance use unfinished summaries | Unsound static reentry/ObjectCompatible proof | Bounded component fixed points, retained causes and dependencies, never default missing summary to empty | T10/T16/T18/T25; M4/M7/M10/M14 |
| Place identity tied to physical slot or general constant folding | Wrong disjointness/Move permission, zero-size alias confusion | Semantic integer path IDs, literal-only indices, sparse tracking, distinct logical initialization | T8/T11/T19; M4/M5/M11 |
| Added temporary or incorrect cleanup edge | Changed evaluation count/order, premature destructor or result escape | Reuse snapshots/Write/Deliver/Join validation; audit normal/Abort/divergent paths and nested defer lifetimes | T9/T12/T13/T17/T30; M4–M12/M16 |
| Physical sort or bytes mistaken for logical responsibility | Wrong field/base/Case destruction, padding reads | Layout offset maps plus separate logical order and initialized components | T12/T13/T22; M5/M6/M8 |
| General borrow emission adds unproved LLVM attributes | Optimizer miscompilation despite valid IR | Prove call-wide ref/uniq rules, no element noalias without premises, conservative attributes otherwise | T11/T16/T22, O0/O2; M4/M8/M13 |
| Generic metadata/frame proliferation | Excessive code, time, memory or wrong frame capacity | Worklist key interning, exact substitution frames, bounded optional exploration and measured budgets | T15/T29/T32/V11; M8/M17 |
| Object atomic protocol mistaken for source memory model | Unsafe release/upgrade or accidental concurrency promise | Implement specified arc ordering; separate runtime ordering review from source concurrency boundary | T18/V7; M10 |
| Collection capacity/churn design uses scratch/per-entry allocations | Violates normative no-allocation/amortization guarantees | Preallocated reusable indices/free lists, geometric growth, operation counters, failed shrink leaves placement intact | T21/V11; M11 |
| Cache uses path/mtime/hash equality as semantic proof | Stale aliases/effects/absence facts/diagnostic displays | Fixed bytes, structural/dependency checks, current source maps, no-cache oracle, bounded reverse invalidation | T23–T25/T27/T29; M3/M14–M16 |
| ZIP/publication or native supply checks inspect only indexes | Corrupt package, inconsistent release or unresolved runtime dependency | Validate actual snapshots, entire closure and atomic mappings; inspect actual selected COFF members and undefined symbols | T24/T25/T31/V9; M13–M14 |
| Test output or descendant handles never terminate | Hung runner, lost failure or false success | Independent control budget/channel, launch-time process management, monotonic deadlines and bounded drain/recovery | T30/V10; M16; blocked interface details G1 |
| Old fixtures or overlapping configurations | False native success for stale source | Inventory/hash protocol; generate then consume serially; exact nonzero count | V3–V9; M1/M17 |
| Warm allocation measurements hide retained memory or cold cost | Performance improvement claimed while total cost regresses | Separate cold/warm, failed runs and source reload; collect peak/retention plus throughput distributions | T32/V11; M1/M17 |

Allocation strategy: prefer existing interned Types/Origins/identifiers, structs and integer indices, reusable lists/bit lanes/adjacency tables and pooled physical layouts. Clear transient references on success and failure. Use streaming hashes and immutable shared content for packages. Avoid arbitrary pooling of long-lived syntax across contexts, forced inlining/full specialization everywhere, duplicate object graphs, or new abstractions without a measured or correctness need.

## 10. Execution Order and Next Actions

| Step | Preconditions | Changes and rationale | Verification | Completion criteria | Failure or blocking conditions |
| --- | --- | --- | --- | --- | --- |
| 1 | Subsequent implementation phase authorized; re-read current git state/rules | Resume M1/I1–I2; compare this baseline to current checkout and preserve new user changes | V1–V3 | Current baseline and runnable focused-check procedure; evidence inventory | G3/G4 prohibit tests/native; record exact limits without weakening gates |
| 2 | M1 mapping for first slice | M2/I4 first bounded feature: remaining numeric conversions using existing conversion plan; I3 audit in focused sub-slices | T2/T6/T7, V4–V8 | Each admitted conversion has static/runtime/error evidence; no premature expansion of borrowing | Stop at unsupported FFI/helper contract or numeric ambiguity; investigate owning clause |
| 3 | Source identity/access baseline | M3/I5–I6 Project graph and check/restore; Package consumers initially reject until M14 | T23/T24, V4/V8/V9 | Exact Project DAG and deterministic lock handling without native dependency | Graph conflict/invalid config is expected negative, not fallback selection |
| 4 | Committed call/module identities | M4 then M5/M6: effects/Loans → storage/deinit/static → enum execution | T9–T14, V4–V8 | General ownership and representation invariants proved for each added range | Unfinished effects/Origin proofs block that use; keep selected operation fixed |
| 5 | Ordinary semantic contracts complete | M7 universal proofs/specialization → M8 typed shared generation/frames | T14–T16/T29, V4–V8/V11 | Verified generic baseline at budget zero, then bounded optimization | No favorable-instantiation workaround for body errors |
| 6 | M4–M8 foundations | M9 callables; M10 objects; M11 sequence/Array slices; M12/I23 Core mappings before M11 Dictionary; M12 interpolation, in independently closed slices | T17–T22/T32, V4–V8/V11 | Each new source API reaches execution and preserves existing paths | Gaps in later element kinds recorded without declaring whole family done |
| 7 | Module/ABI/runtime capabilities | M13 native closure/Library/FFI; M14 package/store/reuse | T22–T25/T31, V4–V9/V11 | Actual native and no-cache/cached artifact evidence, matched inputs | G4; corrupt/stale outputs must reject, never use old success |
| 8 | Generation and membership foundations | M15/I29 and M16/I31–I32 internal semantics; resume I30/I33 only with separate finalized contracts | T27–T30, V4/V8 then V10 | Independent semantics complete; external integration only after G1/G2 | Keep interface blockers; no default invented from elapsed time |
| 9 | All applicable milestone exits | M17/I34–I35 final coverage/performance/doc closure | V12 | §11 satisfied or exact incomplete/blocked subset reported | Missing in-scope evidence prevents complete-compiler claim |

**Concrete next actions:** (1) authorize no new implementation implicitly from this plan; when implementation is requested, confirm the current baseline; (2) resolve G3/G4 through the execution environment; (3) complete M1's next-family clause inventory and exact MTP filter discovery; (4) execute the M2 conversion slice as the first coherent code change; (5) keep separate specification work queued for G1/G2 without stopping independent milestones.

**Resumption protocol:** read §§2, 5, 7, 8 and the active milestone; compare HEAD and diff; validate last output identities/configuration before reuse. Re-run only invalidated checks. Update time, item state, relevant evidence, unresolved decision effects and next action in the same change. If scope changes, retain old IDs with superseded disposition and rationale in §12; don't erase failures or weaken the Baseline to match incomplete implementation.

## 11. Definition of Done

- Every applicable finalized normative clause in R1–R24 has an explicit implemented range, stage-specific tests and current verification evidence; no temporary unsupported gate is counted as successful feature implementation.
- All selected source, unused bodies and generic definitions receive required semantic checks. Deferred obligations are legitimate representation/substitution work and discharged at the defined deadline. No Unknown/pending proof is accepted.
- Valid programs preserve specified evaluation, Copy/Move, Loans/Origins, result delivery, cleanup, Abort, ABI/runtime and artifact behavior at Debug/Release and O0/O2. Negative cases reach the intended check with correct reason/stage/source location.
- Core identities and all settled public operations are integrated. Source dependencies/packages/locks/publication and cache invalidation work from fixed snapshots, and no-cache compilation agrees with reuse.
- Managed build/tests, fixture generation, LLVM verification, ordinary native execution, relevant C/runtime/backend checks and measured performance each have their own current records and traceable artifacts. Nonzero counts and failure/cancellation cleanup are verified.
- Normative allocation/complexity guarantees are demonstrated; broader performance changes have reproducible measurements. Existing low-allocation paths remain protected. No unmeasured speedup or universal zero-allocation claim.
- G5 is closed; no unresolved in-scope mismatch remains. G1/G2-dependent integration cannot be declared complete while interfaces remain unsettled. A separately completed defined subset must state that boundary prominently.
- `STATUS.md`, specification index/implementation-defined documentation, usage and examples agree with actual support; this plan records execution history. Draft proposals and unrelated tooling remain separate.
- NativeAOT is not required and has not been run unless later explicitly requested. Only `PLAN.md` changes during this planning phase.

## 12. Plan Changes and Out-of-Scope Findings

### Material plan changes

| Date | Change | Rationale / disposition |
| --- | --- | --- |
| 2026-09-15 | Created initial plan at specified `PLAN.md` | Entire compiler completion scope; clean initial checkout; no pre-existing plan to preserve |
| 2026-09-15 | Separated settled operations from undefined public interfaces | G1/G2 retain explicit blockers without excluding settled testing/Mod semantics or inventing APIs |
| 2026-09-15 | Recorded current tool/NuGet restrictions and inspected artifact side effects | Prevent historical reports/hash equality/help exit code from becoming false current execution evidence |
| 2026-09-15 | Chose incremental Koto/ownership/physical-plan extension and source-first module foundation | Reuses actual architecture and current working subset; no speculative Bound Tree/redesign |

No work IDs have been removed or superseded in this initial version. Later changes retain IDs and their disposition here.

### Separate findings

- `STATUS.md` mentions incomplete LSP diagnostics and KimiCode host integration. They are not compiler-language completion prerequisites; only source provenance/invalidation behavior required by R1/R23 is included. Do not schedule an editor redesign.
- `autoframe/`, `obsolete/`, general IDE packaging and remote distribution are separate projects or historical material. No changes planned here.
- `.github/workflows/test.yml` currently builds Release then calls a configuration-unspecified `dotnet test`; current explicit MTP target/configuration and Windows native coverage need I2/I35. No CI workflow or NuGet publication was executed during planning.
- Comments/status can lag the finalized specification: G6 is a concrete example. Correct them with the relevant implementation, not by changing the language to match the old subset.
- A completed PLAN.md is a completed planning artifact only. The compiler implementation remains TODO, with independent work ready and the listed interfaces/environment checks blocked.
