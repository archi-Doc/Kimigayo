# Kimigayo Compiler Completion Plan

## 1. Goal and Scope

### Baseline

Complete the compiler for all finalized language rules and implementation contracts in `SPEC.md`, Chapters 1–22, and normative Appendix A. Completion means correct acceptance, required rejection and warnings, ownership verification, checked generation, artifacts, and execution on the specified Windows x64 profile. Parsing or successful LLVM verification alone is insufficient.

This is a planning deliverable, not authorization to begin implementation. During this phase only `PLAN.md` is changed. Preserve user changes; do not edit `draft/`, specifications, implementation, tests, examples, or status files. NativeAOT is neither run nor a completion requirement. Ordinary LLVM/native execution is a separate, required future verification layer.

This file is the authoritative execution record for this effort. Its baseline must not be weakened to match implementation limitations. Future product-wide support summaries belong in `STATUS.md`; detailed execution history belongs here.

### Authority and boundaries

1. `SPEC.md` identifies the owning chapters. §1.3 makes unqualified rules normative, distinguishes mandatory requirements from recommendations, and says examples do not add rules.
2. Appendix A is normative verification/implementation guidance. Appendix B is optional algorithms; Appendix F is a non-normative syntax summary. `STATUS.md`, code, tests, and program milestones are evidence, not language authority.
3. Honor explicit supersession: §21.3 integrates the adopted generic sharing/specialization design; integrated dependency/artifact rules are identified by `SPEC.md`; §17.5 supersedes the testing draft's earlier `$require` return behavior. Read a referenced design only if an owning section leaves an authority question; do not edit it.
4. Appendix D and owning sections delimit exclusions. “Specified, not implemented” remains in scope. “Deferred design” and undefined public APIs do not become requirements merely because syntax/examples exist.
5. Specifically include settled object/Weak operations (§13.5.8–9), collection mutation (§4.7), source packages/local publication (§18), and basic test semantics. Old implementation comments calling these unspecified do not supersede the specification.
6. Exclude Composition Root Entry/Provider selection, runtime Contract Views, source concurrency, extra target profiles, stable external Kimigayo/DLL ABI, source transparent aliases, general user-defined arithmetic, extra generic/specialization forms, persistent generation/object-code caches, dynamic generic scratch allocation, and other Appendix D extensions.
7. Exclude undefined raw-storage acquisition/allocation/reference-conversion APIs (§5.6), exchange API spelling (§15.7), checked-cast/exact-test public spellings (§13.6.2), and string concatenation acquisition (§13.3). Preserve their settled constraints without inventing executable syntax. The mention of concatenation in §22.1 does not override §13.3's explicit executable-finalization prohibition. Interpolation and its Stringify contract are independently specified and included.
8. Undefined Mod host interfaces/configuration and test profile interfaces are separate design work (G1–G2). Their settled semantic rules remain baseline requirements; dependent public integration cannot be declared complete by inventing interfaces. Dynamic collection/Slice internal representations may be proposed within settled contracts; a fixed public collection ABI is excluded (G3).

Performance is a first-class constraint: minimize allocations, avoid repeated work, bound graphs/worklists and retained memory, reuse established storage, and measure compile/runtime costs. Do not promise universal zero allocation or throughput improvements without evidence. Language-mandated allocation/complexity bounds are acceptance criteria (R18), not optional tuning.

## 2. Execution State

- Last updated: **2026-09-17T00:58:45+09:00** (Asia/Tokyo).
- Code baseline: **`a31b0aee5efcd41a60302b2d96e5930292d12e19`**.
- Initial `git status --porcelain=v1`: empty. `PLAN.md` did not exist. No existing plan was overwritten.
- Applicable development rules: root `AGENTS.md`; repository search found no nested `AGENTS.md`. English documentation, practical allocation/performance optimization, no draft edits, no NativeAOT tests.
- Planning readiness: **PARTIALLY_READY**. M1 and independent semantic work have concrete entry points. Whole-product certification awaits current verification and the explicitly excluded interface decisions in G1–G2; subsequent milestones require focused clause-level audits rather than assuming every historical support claim is current.
- Current milestone: **M1, TODO**. Investigation for this plan is complete; implementation has not started.
- Immediately executable after a later implementation instruction: resolve the build-environment question G5, establish M1 evidence, then extend existing semantic operation plans in M2. Do not start any milestone during this planning turn.
- Current tests: **none executed**. No compiler/solution build completed. Help probes are not conformance tests. V1 records their actual outcomes.

Use states `TODO`, `IN_PROGRESS`, `IMPLEMENTED_UNVERIFIED`, `DONE`, `BLOCKED`, `NOT_APPLICABLE`. DONE requires the milestone's acceptance evidence, not historical reports. Use verification results `PASS`, `FAIL`, `NOT_RUN`, `BLOCKED`.

| ID | State | Completion evidence or remaining work | Next action |
| --- | --- | --- | --- |
| M1 | TODO | Read-only architecture/tooling investigation recorded; current build/test baseline absent | V1–V3; resolve G5 |
| M2 | TODO | Existing Binding and proof tables reusable; remaining expression/contract obligations need completion | I3–I5 |
| M3 | TODO | Existing CFG, paths, cleanup and struct borrow support reusable; general effects/Loans/refinement incomplete | I6–I8 |
| M4 | TODO | Concrete structs already have execution support; inherited layout/accessors/static storage need integration | I9–I11 |
| M5 | TODO | Concrete enum semantic plans exist; general enum/Pattern execution absent | I12 |
| M6 | TODO | Generic schema/proof foundations exist; universal bodies/selection/shared generation incomplete | I13–I16 |
| M7 | TODO | Capture syntax and Callable identities exist; Closure/function-value execution incomplete | I17–I18 |
| M8 | TODO | Index/Range/Slice/iteration Core entries missing; existing fixed-array element paths reusable | I19–I20 |
| M9 | TODO | Collection/Stringify/comparison contracts specified; executable Core incomplete | I21–I23 |
| M10 | TODO | Object/Weak APIs and profile specified; generation/runtime incomplete | I24–I25 |
| M11 | TODO | Import declarations/pointer syntax partially supported; native execution boundary needs completion | I26 |
| M12 | TODO | Project graph/restore/lock support exists; packages/common generation/semantic persistence incomplete | I27–I29 |
| M13 | TODO | Product exclusion exists; internal test verification/discovery can be developed | I30; external runner interface slice blocked by G2 |
| M14 | BLOCKED | Public Mod host interfaces excluded until separately specified | I32 semantic lifecycle audit can proceed; no invented host API |
| M15 | TODO | Full conformance, performance and documentation closure depend on prior milestones | I33–I34 |

Each I item starts TODO except I31 (BLOCKED by G2) and I32's public interface slice (BLOCKED by G1). No implementation item is initialized DONE.

## 3. Current State and Architecture

### Evidence vocabulary

- **Verified fact:** observed command output or a specific inspected implementation branch. Code inspection establishes structure/guards, not successful current execution.
- **Evidence-based inference:** conclusion from inspected architecture and tests, requiring an implementation-time reproducer before changing behavior.
- **Proposed change:** future design in Sections 6–10; new files/symbols are labeled **new**.
- **Unverified assumption:** explicitly tracked in G entries; cannot certify a requirement.

### Actual processing path

| Stage / existing entry point | Input → output and responsibility | Verified fact / required work |
| --- | --- | --- |
| `Kimi/Unit/Program.cs`, `Kimi/Unit/CommandUnit.cs`, `Kimi/Unit/Command/CommandExecution.cs` | CLI → command options and Solution/Project operations | Commands registered: default, lsp, build, check, restore, emit, run. No pack/publish/store/test command is registered. Extend dispatch only for specified commands (M12–M13). |
| `Kimi/SolutionAndProject/Solution.cs`, `Project.cs` (`BuildCore`, `BuildTarget`), `DependencyResolver.cs`, `DependencyLock.cs` | Selected paths/configuration/locks → fixed source graph and target Compilation | Project dependency loading and lock partitions exist. Package branch explicitly throws `ResolutionFailure("UnsupportedPackage", ...)`. Preserve source-first identity and direct-reference visibility. |
| `Kimi/Compiler/Core/Compilation.cs` (`Prepare`), `Compilation.Modules.cs` (`PrepareModules`) | Target/settings/graph → environments, `SourceModules`, per-module aliases/reference maps | `Prepare` forbids changing inputs after parsing. Existing modules are real source modules, despite older STATUS §6 wording. |
| `SourceDocument`, `Kotonoha.AddSource` / `ParseSource`, `Tokenizer`, parser/Koto classes | Immutable source snapshot → source-local `CodeContext`, Koto tree, syntax diagnostics | Selected source merges into root containers; top-level executable items use a private generated-function wrapper. Reached directives are validated during parsing. No separate Bound Tree exists. |
| `Binding.Bind`, `Binding.CheckBound`, `BindingModel.cs` and `Binding.*.cs` | Existing Koto + environments → `BoundType`, Symbols, selected operations, constraints/Origins, obligations/certificates | Reuses tables/capacity and writes semantic fields on Koto. Final binding resets facts and invalidates ownership. Late certificate propagation exists; universal generic/effect completion remains work. Lowering must consume identities/plans, never redo lookup. |
| `ControlFlowAnalysis`, `StructuralCompletion`, `OwnershipAnalysis.Analyze`, `OwnershipBody`, `OwnershipModel.cs` | Bound operations → CFG, places, acquisition/result/cleanup plans, initialization and Loan checks | Concrete paths, source-checking continuations and runtime reachability are distinct. Captures/default parameters and accessors have explicit unsupported paths. Extend this representation, not a parallel ownership engine. |
| `LlvmEmitter.TryPrepare` / `CheckInputs`, `FunctionAbiPool`, `AggregateLayoutPool`, `BodyLowering.Lower` | Current checked semantic/CFG state → physical `EmissionModule` / functions / operands | A retained module is scratch, not a reusable certificate. `CheckInputs` requires final Binding, startup and verified ownership. Rejects external modules, non-Application output, non-struct nested containers, anonymous/specialized/generic signatures, captures/defaults and other unsupported bodies. Root structs and eligible borrowed struct methods are already admitted. |
| `EmissionModule.Complete`, `LlvmModuleWriter` partials, `WindowsLowering` | Validated typed instructions → LLVM text | Syntax-free physical instructions and pools exist. Validate every added instruction's inputs, layout, CFG and responsibility before writing; preserve failure leaving module unwritable. |
| `EmissionArtifacts`, `ArtifactPaths`, `NativeToolchain`, `ToolchainResolver`, `Kernel32Imports` | LLVM/link manifest → checked object/link inputs → staged executable/build record | `emit` is separate from LLVM invocation; `build` verifies/links; `run` executes existing artifacts. Hashes, tool/profile identity, cancellation and process cleanup already exist. Extend rather than replace publication machinery. |
| `WindowsRuntime.ll.in`, `backend/windows-x64/profile.json` | Native startup/runtime calls → output, cleanup, Abort/process exit | Windows x64 profile pins LLVM 22.1.8, backend ABI 2, and archive/dlltool hashes. String/output/allocation/Abort helpers exist; general object/Weak runtime needs work. |

### Support matrix at this checkout

Classifications apply to the stated input range. “Implemented” below means inspected code paths plus named test coverage exist, **not** a current test PASS. P = parsing, B = Binding, A = analysis, G = checked generation. LLVM verification/native execution for every row is **Unknown in this planning run**; historical evidence is separate below.

| Requirements / input range | Support by stage | Evidence / remaining limit |
| --- | --- | --- |
| R1–R2: source identity, directives, lexical forms, exact numeric fitting | P: Implemented for inspected rules; B: Implemented for prepared environments; end-to-end clause completeness: Unknown | `SourceDocument.cs`, `Kotonoha.cs`, `CompileTimeConditionEvaluator.cs`; `SourceEncodingTest`, `UnicodeIdentifierTest`, `DirectiveConditionValidationTest`, `NumberLiteralParseTest`. Audit remaining clauses, reuse tests. |
| R3–R5: names, Types, constraints, calls | P/B: Partially Implemented; A/G: Partially Implemented | Extensive `Binding.*` and certificate tests exist. `Binding.Expressions.cs` explicitly rejects anonymous/specialization functions; general callable Property use remains unsupported. Defaults bind but `OwnershipAnalysis.BuildBody` rejects their execution. |
| R6–R9: ownership, cleanup, control flow, primitives | A/G: Implemented for concrete scalar/string/tuple/fixed-array and supported struct paths; full requirement: Partially Implemented | `OwnershipAnalysis.*`, `BodyLowering.*`, `ScalarEmissionTest`, `ElementMoveEmissionTest`, `CurrentControlFlowTest`, `StructEmissionTest`, reference tests. General effects, captures, borrowed aggregates and refinement still need work. |
| R10: struct construction/destruction/borrowed receivers | P/B/A/G: Partially Implemented | `Binding.Structs.cs`, `StructStorage.cs`, `OwnershipAnalysis.Structs.cs`, `BodyLowering.Structs.cs`, `BodyLowering.StructBorrows.cs`. Do not redo program milestones 4–5. `StructStorage` enumerates stored members; inherited logical layout must be audited. |
| R11: custom/computed/static Properties | P/declaration B: Partially Implemented; general A/G: Not Implemented at inspected accessor boundary | `Binding.Properties*.cs`; `OwnershipAnalysis.Collector` reports `OwnershipFailure.Unsupported` for `PropertyAccessorKoto`. Static initialization/shutdown still lacks an integrated executable path. |
| R12: enums and full Patterns | P/B/A: Partially Implemented; enum G: Not Implemented in current layout/ABI path | `Binding.Enums.cs`, `Binding.Patterns*.cs`, `OwnershipAnalysis.Enums.cs`, `MatchCoverage.cs`; `FunctionAbi.GetValue` / `AggregateLayoutPool.Get` admit tuples/arrays/structs, not general enum storage. Scalar/string match G exists. |
| R13–R14: generic universal proofs/selection/generation | P/schema B: Partially Implemented; general A/G: Not Implemented through emitter guards | `Binding.Constraints*.cs`, `Binding.OriginRequirements.cs`; `LlvmEmitter.CheckInputs` rejects generic arguments, Origins, constraints and specializations. Declaration proof tests do not prove universal body execution. |
| R15: Closures/common function values | P: Implemented syntax; B/A: Partially Implemented identities; G: Not Implemented through emitter guard | Capture Koto/function Types, `CoreIntrinsics.Callable`; captures fail ownership, anonymous functions fail final Binding. |
| R16–R18: Index/Range/Slice/for/dynamic collections | P: Partially Implemented; general B/A/G: Not Implemented for missing Core entries | `CoreIntrinsics` missing entries; for/range syntax exists. Fixed-array isize indexing and literal static Move paths are already implemented and reusable. Do not equate absent Core entries with absent fixed-array support. |
| R19: Stringify/interpolation/Contract comparisons | P and primitive comparisons: Partially Implemented; missing Core Contract operations: Not Implemented | Core catalog and `Binding.Expressions.cs`; literal strings and primitive comparisons already execute. No inferred permission for deferred concatenation. |
| R20: objects/Weak/runtime type refinement | B: Partially Implemented runtime tests; A/G/runtime: Partially Implemented foundations only | `Binding.RuntimeTypeTests.cs`, `RuntimeTypeTest`; Core catalog has no Weak declaration or object-operation symbols. `CompilerFunctionKind` currently contains WriteLine and Abort only. |
| R21: raw pointers/FFI/C layout | P/declaration B: Partially Implemented; complete native path: Unknown/blocked by current emitter restrictions | `Binding.Attributes.cs`, `LibraryImportTargetBindingTest`, `LayoutAttributeBindingTest`; imported attributed functions cannot simply pass current function generation checks. Inspect operation-by-operation before implementing. |
| R22–R24: modules/packages/reuse/native inputs | Project B/A and restore: Partially Implemented; Package loading/commands: Not Implemented; cross-module G: Not Implemented | `Compilation.Modules.cs`, resolver guard and command registry; `ModuleBindingTest`, `DependencyLockTest`, `EmissionArtifactsTest`. Existing serialization is source reload, not portable semantic records. |
| R25: language tests | Product exclusion P/B/A/G: Partially Implemented; test-mode discovery/runner: Not Implemented through registered CLI | `TestDefinition`, `ProductTestMembershipTest`, `TestAttributeCertificateBindingTest`; `OwnershipAnalysis.Collector` skips marked tests. |
| R26: Mods | Provisional/final Binding and appended-source infrastructure: Partially Implemented; external execution: Not Implemented | `Compilation.Bind` explicitly says no Mod execution exists; public interfaces separately undefined. |
| R27–R28: diagnostics/performance/conformance | Partially Implemented infrastructure; current comprehensive verification: Unknown | Diagnostic collections/spans, reusable pools, benchmark workloads, native scripts. No measured current-run speedup. |

Core catalog discrepancy: code currently has 18 entries and constructs six validated declarations; the table in §22.1 now also requires Weak and explicitly named object factories/operations. `CoreIntrinsics.cs` still comments that object operation spellings are deferred. This is an **IMPLEMENTATION_MISMATCH**, not permission to omit R20 or invent a new catalog contract.

Reload/invalidation: `Kotonoha.OnDeserialized` reparses saved `SourceDocument` objects with new contexts; it does not restore authoritative semantic proofs. `Binding.Bind` resets semantic fields, invalidates ownership, recomputes conformance/constraint certificates and prunes reused state. `LlvmEmitter` clears its declaration-to-ABI map in `finally`. Preserve these boundaries when adding facts; serialize verified semantic records only under R23, never mutable Koto graphs. Test added/removed declarations, changed aliases/constraints/effects and source replacement, not just serialization round trips.

Historical evidence only: STATUS's 2026-09-17 entries report 6,986 managed tests per Debug/Release and native programs 1–6. Program 6 reportedly outputs `Selected 7.\nControl flow passed.\n`. Reports/old binaries were not certified against this checkout. Earlier STATUS §3/§4/§6 descriptions lag newer struct/borrow/module implementation; `.github/workflows/test.yml` also now explicitly selects Release, a test project and a nonzero-test minimum. Reconcile these descriptions later under I34; do not copy their old omissions into the implementation backlog.

## 4. Specification Requirements

These stable IDs cover requirement families; the cited owning sections and Appendix A enumerate their mandatory cases. Before implementing a family, record its individual clause-to-test mapping under the same ID (suffixes may be added without renumbering). This is a coverage obligation, not permission to omit unlisted subclauses. No examples or internal implementation choices add language acceptance rules.

| ID | Mandatory behavior / acceptance boundary | Authority | Implementation / verification coverage |
| --- | --- | --- | --- |
| R1 | Preserve immutable UTF-8 source identity, Unicode/NFC rules, layout/body syntax, literal values and original diagnostic spans across merging/reload | Ch.2; A.1, A.6 | M1, M2; T1, T27; V2–V3 |
| R2 | Prepare case-sensitive target/configuration environments; validate reached directives immediately; select without extra runtime scopes or generic reselection | Ch.19; §20.1–6; A.2 | M1, M2; T2; V3 |
| R3 | Complete Types/Semantics/Origins, nominal identity, Copy/Owned, scope/name/access/fragment/inheritance rules and recursive API accessibility | Ch.3, §6.1–4, Ch.9; A.3, A.8 | M2–M4; T3, T6, T8; V3 |
| R4 | Candidate-local inference, declared applicability/ranking, result expectations, receivers/named/default arguments; commit once, no fallback after usage failure | Ch.7, Ch.10, §12.1–4; A.3 | M2–M3; T4, T5; V3–V5 |
| R5 | Static Contracts, associated Types, conditional conformances, proof statuses and inherited mappings; no unfinished/cyclic proof as success | §8.1–7, 8.9–10; A.3, A.11–12 | M2, M6; T5, T12, T27; V3 |
| R6 | Initialization/history, Copy/Move/partial paths, replacement permissions, generic conditional acquisition and component responsibility | §3.4–7, §13.5/13.7, §15.1; A.7–8, A.12 | M3–M6; T6, T8, T10, T12; V3–V5 |
| R7 | Origin elision/bounds/variance, returned/retained dependencies, distinct Loan anchors, ref/uniq/Reborrow, liveness/effects and Owned erasure | §15.2–6, 15.8–9; §13.5.5; A.3, A.8 | M3, M6–M10; T6, T7, T13, T16, T20; V3–V5 |
| R8 | Single evaluation and specified order; result acquisition before normal cleanup then delivery; defer/deinit order; Abort/divergence suppress subsequent work | §12.2, Ch.14, Ch.16, §17.3; A.7, A.9, A.15 | M3–M5, M13; T4, T8–T11, T25; V3–V5 |
| R9 | Specified numeric/char/string operations and adaptations, exact literal fitting, checks independent of optimization, warnings and Never/Unit behavior | §2.6–9, §13.1–7, §17.3–4, §21.5.3–6 | M1–M3, M9; T9, T19, T29; V3–V5 |
| R10 | Struct fragments, construction/base initialization, inherited receivers/fields, per-layer completion/deinit, logical cleanup distinct from physical layout | §6.2, §9.5.1, §16.3, §21.1; A.7–8, A.14 | M4; T8; V3–V6 |
| R11 | Stored/custom/computed/Contract accessors, permissions, temporary/result Origins, static effects and first-access initialization/shutdown | Ch.11, §22.2.3; A.4 | M4; T10, T11; V3–V5 |
| R12 | Enum/Option/Result formation, Copy conditions, ordered payload acquisition/destruction, owned/shared Pattern semantics, guards, prescribed coverage/warnings | §6.3, §14.8, §15.1.6, §17.2, §21.1.5; A.10 | M5–M6; T12; V3–V5 |
| R13 | Universal generic body checking, Type/pair/length/Origin slots, public ValidLength obligations, explicit full specialization closed before uses | §4.2–4, §8.8–10, §10.5/10.8; A.12 | M6; T13, T14, T27; V3–V5 |
| R14 | Correct baseline sharing, entry/context pairs and metadata, exact closed-substitution frames, deterministic bounded exploration, independent product/test budgets | §21.2–4; A.12, A.14, A.16 | M6, M12–M13; T14, T15, T26; V3–V6, V9 |
| R15 | Explicit captures and function items, Shared/Exclusive/Consuming Callable receivers, per-call Origins, owned common Function values and erasure | §3.2.1, §7.6, §8.6, §10.7, §15.8, §21.2.5 | M7; T16; V3–V6, V9 |
| R16 | Index/Range/ResolvedRange/Slice formation, checked bounds, O(1) views, complete element results, literal-only static Move paths and zero-size/provenance rules | Ch.4 except §4.7; A.13 | M8; T17; V3–V5, V9 |
| R17 | Recognized Iterable/Iterator, one-time iterable acquisition, short next Loan, immutable per-iteration bindings, exhausted state and transfer cleanup | §14.6, §22.1; A.13 | M8; T18; V3–V5 |
| R18 | Array/Dictionary mutation/capacity/absence/results, duplicate-key literal rules, insertion order, retained dependencies/effects and mandatory allocation/complexity bounds | §4.7, §12.3.4, §22.1; A.13 | M9; T19, T20; V3–V5, V9 |
| R19 | Compiler-compatible Core identities/shape; Equatable/Comparable/Stringify mappings; interpolation evaluates/borrows/stringifies each value once | §12.3.3–4, §13.4, §22.1; A.3, A.14 | M2, M5, M8–M10; T5, T19, T21; V3–V5 |
| R20 | Concrete object/Weak ownership, creation/clone/downgrade/upgrade/cyclic factories, identity/view adjustment, runtime tests/refinement, destruction/count ordering | §3.2.2/3.3, §12.4.4, §13.5.7–9/13.6.1, §14.10, §21.2.1–3; A.8, A.14 | M3, M10; T22, T23; V3–V6, V9 |
| R21 | Specified unsafe pointer semantics, C layout and direct imported scalar/pointer ABI; no provenance/ownership invented by LLVM instructions | Ch.5, §7.5, §21.1/21.5, §22.3; A.5, A.14 | M11; T24; V3–V6 |
| R22 | Exact dependency identities/settings/aliases, locks/partitions, package manifests/integrity, local atomic pack/publish/store lifecycle | §18.1–6/18.8, §20.8; A.16 | M12; T27, T28; V3, V7–V8 |
| R23 | Source correspondence and semantic validity distinct from connection validity; complete/absence/effect dependencies, bounded invalidation and correct no-cache behavior | §18.5/18.7, §21.3.4; A.1, A.3, A.16 | M12; T27, T28; V3, V8–V9 |
| R24 | Application/Library startup roots, common final generation, target ABI/runtime/source locations, native requirement/supply validation and atomic artifact publication | §20.8, §21.4–5, §22.2/22.4–5; A.14, A.16 | M4, M11–M12; T24, T28, T29; V4–V8 |
| R25 | Product/test separation, all-body checking before selection, expect/require semantics, isolated bounded case execution, reliable failure/reporting and immutable all-case artifacts | §6.5.1, §17.5, §18.8, §20.9, §21.3.7, §22.6; A.17 | M13; T25, T26; V3–V5, V10; G2 |
| R26 | Settled Mod ordering/provisional-final Binding/source provenance/invalidation; no per-instantiation generation or arbitrary rewriting | §20.7; A.1–3 | M14; T30; V3, V8; G1 |
| R27 | Diagnose required invalid/unsupported selected syntax at its proper stage and source, including unused/unreachable bodies; no publication on incomplete checks | §17.4, §19.5, §21.4.1; A.1–17 | Every milestone; negative variants of T1–T30; V3–V8, V10 |
| R28 | Practical allocation/throughput/retention improvements without semantic changes; bounded compilation and measured performance evidence | §21.3.5, A.16–17; user instructions | Every milestone, M15; T15, T20, T26–T28; V9, V11 |

## 5. Gaps and Decisions

| ID / classification | Evidence and affected requirements/milestones | Resolution / effect |
| --- | --- | --- |
| G1 `SPEC_GAP` | §20.7.7 and D list concrete Mod query/marker APIs, packaging/compatibility, settings and host limits as design work. R26/M14 | Separate public API/host design must define these before external implementation. Retain existing append/rebind infrastructure and verify settled lifecycle rules internally. Do not treat illustrative `IMod`/`ModContext` as adopted interfaces. |
| G2 `SPEC_GAP` | D.4 explicitly defers temp-directory API, deadlines/recovery defaults/options, budgets, transport/report schemas, empty-set option, exits and ID encodings. R25/M13 | Separate consolidated profile decision required before I31: settle all listed interfaces and bounds together. I30 semantic membership/discovery/verification work is independent. Do not publish a supposedly complete runner using invented public contracts. |
| G3 `INVESTIGATION_GAP` | D's collection storage entry versus settled §4.6.8/§4.7 complexity/storage freedom. R16–R18/M8–M9 | Public storage ABI remains excluded. Proposed internal Slice location/length and Array contiguous storage are implementation choices, subject to layout/Loan/zero-size validation. Proposed Dictionary indexed slots/free-list/order links allow allocation-free churn; benchmark before optimizing search. No user approval is needed for ordinary private layout choices within these contracts. |
| G4 `IMPLEMENTATION_MISMATCH` | Core catalog omits required Weak and leaves object operations missing with a stale deferral comment, despite §22.1 and §13.5.8–9. R19–R20/M2, M10 | Add canonical declarations/identities with their implementation milestones; update catalog tests intentionally. Do not make `IsCompleteLibrary` true just by changing a count or weakening required shapes. |
| G5 `INVESTIGATION_GAP` | `dotnet test --help` printed MTP help then attempted extension discovery/build, reporting `Access to the path 'C:\Users\bwff1\AppData\Roaming\NuGet\NuGet.Config' is denied.` SDK wrapper exited 0 despite the nested failure. All build/test evidence | Environment blocker observed, not a code failure or auto-review rejection. M1 must establish authorized configuration access/restore, inspect actual child results, then build. No need to resolve it during this plan-only phase. Do not edit user NuGet configuration to bypass the issue. |
| G6 `INVESTIGATION_GAP` | Historical success is not current evidence; only selected hot paths/guards were inspected. R1–R28/M1, M15 | Reproduce focused baselines, expand clause coverage using Appendix A before each milestone. Unknown support needs a targeted source reproducer/code trace, not “not implemented” from a failed search. |
| G7 `INVESTIGATION_GAP` | `AggregateLayoutPool` uses int offsets/counts, depth 64 and rejects size > int.MaxValue; §21 defines broader checked layout/resource rules. R10/R14/R16/M4, M6 | Determine which are documented resource limits versus unnecessarily restrictive representations. Use checked wide arithmetic internally where needed; keep finite limits explicit and distinguish semantic, resource and representation diagnostics. Do not allocate giant fixtures to test overflow. |
| G8 `IMPLEMENTATION_MISMATCH` | STATUS historical summaries omit newer struct/borrow/module support and current CI test selection. R27/M15 | Reconcile support descriptions against current evidence in I34; no STATUS edit during planning. |

No unresolved **SPEC_CONFLICT** was established by this investigation. Contradictory status text or stale code comments are not specification conflicts. If two applicable normative rules remain contradictory after owning-section/supersession review, add a SPEC_CONFLICT row with both clauses and block only dependent work.

Decisions/proposals:

- D1: Extend Koto semantic fields and existing reusable Binding/ownership/physical plans. No new Bound Tree or mandatory intermediate compiler stage.
- D2: First complete correctness and a valid baseline sharing plan; optional specialization/tuning cannot change selected implementations, caller ABI, checks or acceptance.
- D3: Start persistent reuse with conservative module invalidation; refine only with complete dependency evidence and measured benefit. Source reparse/rebind is the correctness oracle.
- D4: Internal resource/budget defaults and float Stringify spellings are documented implementation choices where explicitly permitted. Record chosen values and measurements at M6/M9; they are not missing language rules.
- D5: Programs `milestones/Milestone7.kimi`–`Milestone9.kimi` are integration targets, not the definition of compiler completion. They depend respectively on M8; M6+M8; and M5–M8 plus general borrowed storage. Preserve their source and expected behavior.

No user decision is needed to create this plan. Before implementing excluded interfaces, request G1 and G2 as consolidated, concrete design decisions in their separate effort; silence never resolves them.

## 6. Milestones

All changes below are **proposed**. Directory names refer to existing directories. Existing files/symbols are reused unless explicitly marked **new**. A family is complete only when its remaining finalized input range is supported and its mandatory negative boundaries still reject. Tests of known behavior are regression gates, not redundant implementation tasks.

### M1 — Establish the current conformance baseline

- Objective: R1–R28 traceability and reliable evidence; no semantic changes as a shortcut.
- Dependencies/start: later implementation instruction; inspect fresh HEAD/diff and G5. No dependency on G1–G2.
- Work: I1–I2. Use existing `xUnitTest/Tests`, `Benchmark`, `backend/windows-x64` and this plan. Build exact source/configurations and map Appendix A clauses to tests; investigate only uncovered or contradictory behavior.
- Preserve: user edits, draft exclusion, profile pins, current source identity, separate phase evidence.
- Complete: V1–V3 and relevant V4–V5 baseline records with exact counts/hashes; each observed discrepancy assigned an I/T/G ID. A failed baseline is retained, not relabeled PASS.
- Risk: stale ignored DLLs/fixtures and optional help invoking build. Minimum independent unit is a freshly built test module with verified discovery and nonzero selected cases.

### M2 — Complete Binding and operation contracts

- Objective: R1–R5, R19, R27. Audit existing coverage and close actual gaps in function defaults, candidate equivalence/inference, complete-Type/Origin formation, source access, standard operations, static Contract witnesses and obligation deadlines.
- Dependencies: M1. General runtime/effect obligations may remain explicit until M3; they cannot certify completed executable use.
- Files: `Binding.cs`, `Binding.Expressions.cs`, `Binding.Calls.cs`, `Binding.CandidateEvaluation.cs`, `Binding.ArgumentOperations.cs`, `Binding.Contracts*.cs`, `Binding.Constraints*.cs`, `Binding.Properties*.cs`, `CoreIntrinsics.cs`, relevant Koto/parser/diagnostic files only when syntax or diagnostics actually require changes.
- Work: I3–I5. Retain receiver/argument/default evaluation plans and original defining environments. Add new Core declarations together with their operations rather than empty library promises.
- Preserve: winner-only commit, lookup stopping, no usage-failure overload fallback, independent proof paths, source-order independence, immediate directives.
- Complete: T1–T5 and T27 via V3; every finalization obligation either discharged or a legitimate representation obligation with a deadline. R4 default evaluation then integrates through M3/V5.
- Risk: recursive proof stabilization and stale absence facts. Reuse bounded proof scratch/worklists; avoid repeated whole-world fixed points where reverse dependencies suffice.

### M3 — General ownership, effects, refinement and cleanup

- Objective: R6–R9, R20's static guarantees, R27. Extend existing place/Loan/CFG models to full settled borrowing and result contracts, static effects and checking continuations.
- Dependencies: M2 operation plans. Object runtime implementation is not required to test static receiver/effect proofs.
- Files: `OwnershipAnalysis.*.cs`, `OwnershipBody.*.cs`, `OwnershipModel.cs`, `ControlFlowAnalysis.cs`, `StructuralCompletion.cs`, `Binding.Origins*.cs`, `Binding.RuntimeTypeTests.cs`, `BodyLowering.*.cs`.
- Work: I6–I8. Model defaults as ordered caller acquisitions, call-family effects including defaults/cleanup/statics, returned Loan anchors, per-path initialization and retained storage, normal and checking-only successors. No private-body dependency at public call sites.
- Preserve: result secured before cleanup; no delivery after divergence/Abort; scope registration separate from cleanup execution; distinct Loan identities despite equal Origins; no new runtime lifetime tags.
- Complete: T4, T6–T9, T23 negatives via V3–V5; live-state and cleanup tests at joins/backedges, unreachable violations and separate-module effects. Effect-family fixed point and dependent usage completion must land together; a stub empty summary cannot unblock calls.
- Risk: unsound acceptance when an effect/callee is unknown. Retain diagnostics/incomplete status until proof exists.

### M4 — Complete structs, Properties, inheritance and statics

- Objective: R10–R11 and their R3/R6–R8/R24 interactions.
- Dependencies: M2–M3; generic substitution-specific extension finishes in M6.
- Files: `Binding.Structs.cs`, `Binding.Storage.cs`, `Binding.Properties*.cs`, `StructStorage.cs`, `AggregateLayout.cs`, `FunctionAbi.cs`, `OwnershipAnalysis.Structs.cs`, `BodyLowering.Structs.cs`, `BodyLowering.StructBorrows.cs`, `LlvmEmitter.cs`, `WindowsRuntime.ll.in`.
- Work: I9–I11. Extend existing concrete constructors/destructors, field identities and borrowed receivers; integrate base-layer initialization/destruction and stored/custom/computed access plans. Add **new** `StaticInitializationPlan` in `Kimi/Compiler/Emission/StaticInitializationPlan.cs` only as the compact per-slot/state/shutdown representation needed by §22.2.3.
- Preserve: logical order despite alignment sorting; exact deinit layer; no accessors during destruction; first write initializes before replacement; no eager initialization; shutdown in reverse successful order.
- Complete: T8/T10/T11, V3–V6; borrowed results, failed partial construction, access restrictions, static cycle/reentry Abort and no duplicated deinit. Keep existing struct fixtures passing.
- Risk: pooling layout by shape erases destructor or base identity. Completion requires layout and cleanup verification together, not offset tests alone.

### M5 — Enum and full Pattern execution

- Objective: R12 and general enum results/ownership needed by later Core operations.
- Dependencies: M3; M4 logical layout utilities where shared. Concrete nongeneric cases can complete before M6.
- Files: `Binding.Enums.cs`, `Binding.Patterns*.cs`, `MatchCoverage.cs`, `OwnershipAnalysis.Enums.cs`, `OwnershipAnalysis.Match.cs`, `BodyLowering.Match.cs`, `AggregateLayout.cs`, `FunctionAbi.cs`, `EmissionModule.cs`, writer partials.
- Work: I12. Retain tag/Case identity, logical payload layout, guard candidates versus selected bindings, shared/owned access and partial cleanup. Extend physical opcodes only as needed; no AST reinterpretation in writer.
- Preserve: one Subject acquisition, source-order guards, mandatory conservative coverage/subsumption rules, warned arms still checked, no payload consumption before arm selection.
- Complete: nongeneric T12 cases via V3–V5; tag/layout/read/cleanup must ship as one verifiable native slice. Generic Option/Result and dependent payload cases remain explicitly assigned to M6 integration, so M6 can depend on this concrete enum slice without a cycle. R12 is not fully complete until those cases pass. No requirement for deferred Pattern forms or object-Semantics enums.

### M6 — Universal generic semantics and bounded generation

- Objective: R5, R13–R14 and generic forms of earlier milestones.
- Dependencies: M2–M5. Start definition-side proof work after M2/M3; no need to wait for runtime objects or dynamic collections.
- Files: existing generic/constraint/Origin Binding files, `OwnershipModel.cs`, `LlvmEmitter.cs`, `AggregateLayout.cs`, `FunctionAbiPool.cs`, `EmissionModule.cs`; **new** `GenericGenerationPlan.cs`, `GenericContextSchema.cs`, `GenericFramePlan.cs` under `Kimi/Compiler/Emission/`.
- Work: I13–I16. Verify symbolic Copy/Move, effects and cleanup universally; close explicit-specialization sets; form semantic/selection/body/entry/context keys with distinct roles. Worklist closed substitutions; substitute verified plans without cloning/rebinding syntax. Validate immutable 8-byte GenericContext slots and operation/context pairs, fixed facts, finite recursive graphs, caller-facing ABI and exact per-entry scratch reservation.
- Preserve: explicit selection at budget zero, Origins in proofs even when erased from representation keys, full-width Type tokens, no dynamic scratch layout/alloca/heap fallback, no optimization-induced acceptance change, separate mandatory resource limits.
- Complete: T13–T15/T27 and M5's remaining generic T12 cases via V3–V6/V9. Baseline shared code and adapters must work before bounded optional specialization; measure generation/code-size/runtime tradeoffs. Adding another instance cannot inflate an existing exact frame.
- Risk: exponential key growth or schema/ABI confusion. Require deterministic bounded worklists and separate diagnostics for invalid recursive inline layout versus finite cycles versus required resource exhaustion.

### M7 — Closures, function items and callable values

- Objective: R15, with R7/R14/R19 contracts.
- Dependencies: M2–M4; M6 for generic Callable witnesses, common contexts and concrete instantiations.
- Files: `FunctionKoto.cs`, `Binding.Expressions.cs`, `Binding.CallableContracts.cs`, `Binding.FunctionWitnesses.cs`, `OwnershipAnalysis.*`, `FunctionAbi.cs`, `AggregateLayout.cs`, `EmissionModule.cs`; **new** `BodyLowering.Callables.cs` and `CallableLayout.cs` in Emission.
- Work: I17–I18. Capture acquisition/environment layout, concrete receiver kind/Copy, function-reference acquisition, indirect call entry/context, per-call Origins and common Function storage/adapters.
- Preserve: capture order and unused explicit captures, no shared-to-exclusive upgrade, Owned environment only where required by erasure, call result cannot depend on hidden receiver/call-local storage.
- Complete: T16 via V3–V6/V9, including empty/inline/heap and zero-size environments, partial consuming cleanup and all receivers. Callable representation and cleanup must be verified together; parser nodes already exist.

### M8 — Sequence views and iteration

- Objective: R16–R17 plus fixed-array portions of R13 and the supporting Core identities/witnesses of R19.
- Dependencies: M3, M5, M6; existing fixed-array paths are the starting point. M9 dynamic collection adapters follow later.
- Files: `CoreIntrinsics.cs`, `Binding.Elements.cs`, `Binding.Lengths.cs`, `Binding.ArrayInference.cs`, `ForKoto.cs`, `OwnershipAnalysis.Elements.cs`, `BodyLowering.Elements.cs`; **new** `Binding.Iteration.cs`, `OwnershipAnalysis.Iteration.cs`, `BodyLowering.Iteration.cs`, **new** sequence-view physical records as needed under Emission.
- Work: I19–I20. Canonical Index/Range/ResolvedRange/Slice/Iterator/Iterable declarations, their required Copy/Owned/Equatable witnesses, checked access plans, metadata snapshots and consuming/external-borrow iteration. Establish the minimal Equatable identity/mapping prerequisite here using I5, without waiting for M9's general comparison/Dictionary support. Lower for using selected contract operations and existing cleanup/CFG mechanisms, without source desugaring that changes contexts or Name lookup.
- Preserve: RHS-first assignment, exclusive activation timing, no intermediate array Copy, no constant-folding expansion of Move paths, empty/zero-size Slice provenance, next Loan ends before body.
- Complete: T17–T18 via V3–V5/V9; unchanged program 7 passes native O0/O2 plus invalid/Abort variants. Support both built-in and user-defined conformances, not just a special-case numeric loop.
- Risk: iterator-owned reference escape and excessive temporary materialization. Require no element-proportional storage for views/iteration.

### M9 — Collections, comparison contracts and strings

- Objective: R18–R19; complete the settled Core surface together with its dependencies.
- Dependencies: M2–M8 for witnesses, general values, returned borrows, iterators and generic cleanup.
- Files: `CoreIntrinsics.cs`, `Binding.Comparisons.cs` if a split is needed (**new**; current comparisons are in existing Binding expression/call files), `Binding.Expressions.cs`, `BodyLowering.Strings.cs`, `WindowsRuntime.ll.in`; **new** `CoreCollectionOperations.cs`, `BodyLowering.Collections.cs`, `CollectionLayout.cs` under compiler Binding/Emission as appropriate.
- Work: I21–I23. Canonical Array/Dictionary APIs and effects, explicit internal allocation/relocation/cleanup plans, literal duplicate checks, insertion-order mutation/iteration. Add Equatable/Comparable and Stringify witnesses plus owned interpolation output. Keep float Contract equality distinct from IEEE operators; document implementation-defined float formatting.
- Preserve: no blanket Owned constraint on collections, no dependency subtraction on clear/None/Err, no allocation within capacity including churn, growth bound, preserved placement on failed shrink, scalar/string operations already working.
- Complete: T19–T21 via V3–V5/V9; counts/instrumentation prove allocation and amortized obligations in addition to timing. String concatenation and extra APIs remain excluded.
- Risk: equality reentry/unstable keys must not break memory safety; whole-collection Loan/effect plans and storage implementation must be integrated, not validated in isolation.

### M10 — Objects, Weak and runtime refinement

- Objective: R20 with settled runtime metadata and ownership intrinsics.
- Dependencies: M3–M7; cyclic factories require consuming Callable and enum Option results.
- Files: `CoreIntrinsics.cs`, `CoreDeclaration.cs`, `Binding.RuntimeTypeTests.cs`, capability/Origin/effect files, `WindowsRuntime.ll.in`, `WindowsLowering.cs`; **new** `ObjectLayout.cs`, `BodyLowering.Objects.cs`, `ObjectMetadataPlan.cs` in Emission.
- Work: I24–I25. Concrete object payload eligibility and view adjustment, obj/rc/arc creation and sharing, Weak table/count migration, downgrade/upgrade, irreversible last-strong destruction, cyclic Building→Alive publication after builder cleanup, Runtime Type Identity and flow refinements.
- Preserve: complete dynamic destruction, no source-handle Loan retained by clone, no resurrection, count overflow Abort, payload +16 profile, Owned constraints only at specified erasure/cyclic boundaries, no source concurrency feature.
- Complete: T22–T23 via V3–V6/V9. Native tests/fault adapters plus separate memory-order argument for atomic protocols; O0/O2 tests alone do not prove weak-memory correctness.
- Risk: count/table lifetime bugs and incomplete public ObjectCompatible status. Release/upgrade/count/cleanup changes must land together for each ownership mode.

### M11 — Unsafe pointers and foreign ABI

- Objective: R21 and native portions of R24.
- Dependencies: M2–M4 checked layout/operations. R22 native graph integration proceeds in M12.
- Files: `Binding.Attributes.cs`, pointer/operator Binding paths, `FunctionAbi.cs`, `WindowsLowering.cs`, `LlvmModuleWriter.*`, `NativeToolchain.cs`, `NativeLibraryInput.cs`, backend C/LLVM test adapters.
- Work: I26. Implement only specified pointer equality/arithmetic/conversion/dereference rules and direct unsafe imports with exact physical signatures. Validate C layout independently of C aggregate passing, which remains unsupported.
- Preserve: no invented safe-pointer acquisition or allocation API; no unjustified inbounds/noalias/nounwind; exact scalar widths, >4-argument ABI, foreign initialization/FP constraints.
- Complete: T24 via V3–V6; Clang cross-checks of size/alignment/offsets and call results. Undefined unsafe behavior has no prescribed runtime outcome/test oracle.
- Risk: backend accepting valid LLVM that violates source provenance, external ABI or hidden CRT restrictions.

### M12 — Source packages, common generation and verified reuse

- Objective: R22–R24. Existing Project graph/locks stay the base; no binary distribution redesign.
- Dependencies: M1–M3 for initial package semantic verification; M4–M11 for full common generation of supported operations. Package parsing/integrity can be developed independently of later native features.
- Files: `DependencyResolver.cs`, `DependencyLock.cs`, `ProjectFile.cs`, `Project.cs`, `Compilation.Modules.cs`, `LlvmEmitter.cs`, `EmissionArtifacts.cs`, `NativeToolchain.cs`, `CommandUnit.cs`; **new** `SourcePackage.cs`, `PackageStore.cs`, `SemanticRecord.cs` under SolutionAndProject/Core, **new** `PackCommand.cs`, `PublishCommand.cs`, `StoreCommand.cs` under Unit/Command.
- Work: I27–I29. Fixed-byte package ZIP/manifest handling, canonical writer, SourceId and graph validation; destination-local atomic publication; user cache integrity/pinning/collection; common final generation with Library roots and direct-reference environments. Native requirement/supply/member-closure validation includes actual COFF and directives. Persist complete semantic records, regenerate physical/generation plans.
- Preserve: no lock rewrite during source commands, no host paths in packages, no semantic trust from package claims, no cross-version Type merging, no stale absence/effect proofs, no-cache equivalence, no persistent runtime context/object-code graphs.
- Complete: T27–T28 via V3/V7–V9; same-graph cold/warm/relocated/corrupt runs agree on mandatory semantics and current locations; downstream native tests use final common artifacts.
- Risk: publication races, resource exhaustion and stale dependency identities. Stream hashes, use finite graph/work/memory bounds, staged atomic publication and reverse dependencies. No network publication is required.

### M13 — Language test semantics and bounded runner

- Objective: R25, with immutable product semantics and test-region generation.
- Dependencies: I30 can begin after M2–M3; complete execution needs M4/M6/M7/M12. I31 public interface work requires G2 resolved in a separate design effort.
- Files: `TestDefinition.cs`, `Binding.Attributes.cs`, `Project.cs`, `DependencyLock.cs`, `LlvmEmitter.cs`, `WindowsRuntime.ll.in`, `CommandUnit.cs`; **new** `TestCompilation.cs`, `TestDiscovery.cs`, `BodyLowering.Verification.cs`, then **new** `TestCommand.cs` and `TestRunner.cs` after G2.
- Work: I30–I31. Test mode verifies all selected bodies before filters/listing, rejects ordinary test calls/product verifications, lowers expect/require using one condition snapshot and lazy message, fixes product plan before test additions. Runner uses one immutable all-case artifact and a fresh managed child per case, bounded logs/control channel/recovery and stable reports.
- Preserve: false `$require` Aborts after condition/message temporaries, not helper return; no outer cleanup/shutdown after Abort; failure latch survives message/cleanup failure; no product budget/frame changes from tests; successful checks allocate no failure diagnostics.
- Complete: T25–T26 via V3–V5/V10; process tree/recovery fault tests and exact failure-stage/site evidence. G2-dependent criteria remain BLOCKED, never silently removed.
- Risk: bounded data storage accidentally suppresses required evaluation, or exit 0 without valid completion becomes success. Report control integrity independently of stdout/stderr.

### M14 — Settled Mod lifecycle integration (interface gate)

- Objective: R26 without adopting illustrative APIs.
- Dependencies: M2/M12 semantic identity/reuse; G1 blocks the public host slice.
- Files: `Compilation.Bind`, `Kotonoha.AddSource`, `Binding.Bind`, dependency/processing records; potential **new** `ModExecutionPlan.cs` under Core after separate design.
- Work: I32. Audit provisional snapshots, append-only source contexts, topological once-only scheduling and finalization. After G1, implement the adopted host API and bounded execution/provenance contract.
- Preserve: no generic reselection of directives, no shared Koto mutation during finalized generation, no arbitrary rewriting/function insertion, no automatic retries or per-instantiation invocation.
- Complete: T30 via V3/V8 with order independence, cycles/missing IDs, regeneration and generated diagnostics. Pack's initial rejection of Mod-requiring inputs remains specified.
- Risk: unfinished provisional information mistakenly exported as proof. Do not unblock public integration with a fake empty Mod result.

### M15 — Full conformance, performance and delivery record

- Objective: R1–R28 closure, not just the nine demonstration programs.
- Dependencies: all in-scope milestone criteria satisfied; any excluded-interface blocks remain explicitly disclosed. Do not claim whole compiler completion while an included settled guarantee still lacks its required implementation/verification.
- Files: this plan, `STATUS.md`, `SPEC.md`/owning chapters only for necessary documented choices or approved corrections, `README.md`, relevant example/milestone READMEs, existing benchmark/CI scripts. No draft edits.
- Work: I33–I34. Full Appendix A clause audit, focused interaction coverage, Debug/Release and native O0/O2 regression, measured allocation/time/memory/code size, accurate usage/support records. Extend `.github/workflows/test.yml` with applicable native/evidence checks rather than re-adding its already present Release/nonzero-test selection.
- Complete: V2–V11, no missing requirement/test mapping or unexplained failure, exact residual exclusions listed. A performance change is accepted only with preserved semantics and comparable measurements.
- Risk: claiming completion from historical tests or benchmarks that exclude expensive cold/reparse/module paths.

## 7. Implementation Checklist

All boxes are unchecked because no implementation work was performed for this plan.

| ID | Milestone / requirements | Implementation item and required outcome |
| --- | --- | --- |
| I1 | M1 / R1–R28 | [ ] Record baseline source/config/tool hashes and current focused/full results; resolve G5. |
| I2 | M1 / R1–R28 | [ ] Expand Appendix A clause coverage, identify genuine gaps, preserve existing verified slices. |
| I3 | M2 / R1–R3, R27 | [ ] Close lexical/directive/type/name/access gaps found by audit; avoid parser rewrites without a failing requirement. |
| I4 | M2 / R4 | [ ] Complete call/default/receiver and candidate-local inference plans with no reselection. |
| I5 | M2 / R5, R19 | [ ] Complete retained Contract/projection/certificate obligations and canonical Core identities. |
| I6 | M3 / R6–R7 | [ ] Generalize authorized places/Loans/Origins and retained result/storage dependencies. |
| I7 | M3 / R7, R20 | [ ] Compute effect-family fixed points and public guarantees; complete dependent usage checks. |
| I8 | M3 / R8–R9, R27 | [ ] Complete default acquisition, checking-only continuations/refinement, result/cleanup and warning behavior. |
| I9 | M4 / R10 | [ ] Extend existing struct/base layout/construction/destruction and inherited receiver support. |
| I10 | M4 / R11 | [ ] Connect standard/custom/computed/Contract Property operations through ownership and generation. |
| I11 | M4 / R11, R24 | [ ] Implement first-access static states, effects, cycle checks and ordered shutdown. |
| I12 | M5 / R12 | [ ] Complete enum/tag/payload ABI and owned/shared Pattern lowering. |
| I13 | M6 / R5–R7, R13 | [ ] Verify generic bodies universally and preserve symbolic acquisition/cleanup plans. |
| I14 | M6 / R13 | [ ] Close and validate explicit specialization sets; retain selected implementation at every call/reference. |
| I15 | M6 / R14 | [ ] Build baseline shared bodies, metadata/schemas, entry adapters and exact fixed frames. |
| I16 | M6 / R14, R28 | [ ] Add deterministic finite resource limits and bounded optional optimization; measure effects. |
| I17 | M7 / R15 | [ ] Implement captures/concrete environments and receiver-specific callable analysis/lowering. |
| I18 | M7 / R7, R14–R15 | [ ] Implement function items, common Function values, erasure and indirect-call adapters. |
| I19 | M8 / R16, R19 | [ ] Implement specified Index/Range/Slice operations, prerequisite Core witnesses and general safe sequence access. |
| I20 | M8 / R17 | [ ] Implement recognized iteration protocol and transfer cleanup; integrate unchanged program 7. |
| I21 | M9 / R18 | [ ] Implement Array capacity/mutation/owning iteration with mandatory cost bounds. |
| I22 | M9 / R18–R19 | [ ] Implement Dictionary literal/mutation/equality/order/borrow contracts and allocation-free churn. |
| I23 | M9 / R19 | [ ] Complete comparison/Stringify witnesses and interpolation, documenting float formatting. |
| I24 | M10 / R20 | [ ] Implement object metadata/views/creation/strong ownership and refinement. |
| I25 | M10 / R20 | [ ] Implement Weak migration/upgrade/release/cyclic construction and atomic ordering proof. |
| I26 | M11 / R21, R24 | [ ] Complete specified pointers/C layout/direct foreign calls and ABI validation. |
| I27 | M12 / R22 | [ ] Implement package integrity/loading/packing/local publication/store lifecycle using fixed bytes. |
| I28 | M12 / R22, R24 | [ ] Complete common module/Library generation and native input requirement/supply validation. |
| I29 | M12 / R23, R28 | [ ] Implement verified semantic records/correspondence/invalidation with cold/warm equivalence. |
| I30 | M13 / R25 | [ ] Complete test-mode membership, all-body discovery/checking, verification semantics and plan isolation. |
| I31 | M13 / R25 | [ ] BLOCKED: after G2, implement bounded isolated runner and adopted public reporting interfaces. |
| I32 | M14 / R26 | [ ] Audit/test settled Mod lifecycle; public host integration BLOCKED by G1. |
| I33 | M15 / R1–R28 | [ ] Close Appendix A coverage and cross-feature performance/conformance evidence. |
| I34 | M15 / R27–R28 | [ ] Maintain English support/spec-choice/usage records and examples; retain honest residual boundaries. |

## 8. Test Plan and Verification Records

### Test cases

T IDs denote coherent test families, not one assertion each. Expand only relevant positive, negative, boundary, interaction and regression cases. Existing test class names below are locations under `xUnitTest/Tests/`; a **new** name is a proposed file/class. Reuse `ParseTestHelper`, `MinimalEmissionTest.Analyze`, `ScalarEmissionTest.EmitFixture` / `WriteFixture`, and `StringLifetimeAudit` where suitable.

For a negative test, first make the surrounding program valid, then change only the triggering construct. Assert the intended Binding/ownership/generation diagnostic category, source document/span and lack of published artifacts. An unrelated parse/type error does not satisfy a Loan/ABI/runtime test. New diagnostic identifiers/text are to be selected through the existing diagnostic catalog at implementation time; do not invent exact existing diagnostic codes in this plan.

| ID / requirements | Input/setup and expected result | Stage/evidence | Existing or proposed location |
| --- | --- | --- | --- |
| T1 / R1,R27 | UTF-8/NFC and malformed bytes, CR/LF, comments/indentation/body forms, exact numeric rounding, merged-source aliases; parse/write/reload retain value and original spans | P/B diagnostics and serialization evidence; no early rounding through f64 | SourceEncodingTest, UnicodeIdentifierTest, NumberLiteralParseTest, KotonohaSerializationTest |
| T2 / R2 | Reached invalid condition in unselected `#switch` arm diagnoses; false `#if` target exclusion remains distinct; selected defer/Names share scope; changed settings require fresh Compilation | P/B plus selected cleanup G/native | DirectiveConditionValidationTest, CompilationSpecificationTest, DeferredEmissionTest |
| T3 / R3 | Fragment/header/default-access/base/protected/API-domain variants, added inherited names and source-order permutations; reject restricted Types exposed in broader APIs | B diagnostic at declaration/use; repaired/reloaded input recovers | OriginFragmentBindingTest, InheritedNameBindingTest, FunctionApiCertificateBindingTest |
| T4 / R4,R8 | Example A below plus named/default/receiver ordering, default borrowing, interrupted acquisition; count each evaluation once and clean acquired values in specified order | B/A plans, native log, failed default/use span | DefaultBindingTest, FunctionEmissionTest; **new** DefaultExecutionTest |
| T5 / R5,R19 | Core impostors/invalid shape, conditional witnesses, dependent projections, proof cycles/Unknown/Error, comparison mappings; declaration/order/reload variants keep same result | B certificates and diagnostic causes, no stale winner after repair | CoreCatalogTest, ContractBindingTest, ConditionalConformanceBindingTest, NormalizedConstraintProofBindingTest |
| T6 / R6–R7 | Move then read, partial Move/repair, branch/loop state, simultaneous ref/uniq and Reborrow; returned reference to local rejected, input-derived reference accepted | A at the invalid use/escaping result; native final cleanup after accepted borrow ends | OwnershipAnalysisTest, ElementMoveEmissionTest, ReferenceEmissionTest; **new** BorrowCompletionTest |
| T7 / R7 | Direct/recursive/indirect/imported callee mutates a static protected by a live Loan; changed callee effect invalidates use; independent shared read stays valid | B/A public summaries, caller diagnostics, reload/cross-module comparison | ModuleBindingTest; **new** EffectSummaryTest |
| T8 / R6,R8,R10 | Existing Resource fixtures plus base/derived construction, partial failure, field reordering and zero-size deinit; child/base destruction order observable once | B/A/TypeLayout/IR/native, C cross-check only for C layout | StructEmissionTest; **new** InheritedStructEmissionTest |
| T9 / R8–R9 | Existing checked arithmetic/conversion boundaries, short circuit, Never/Unit results, unreachable invalid use, defer divergence and Abort; acceptance unchanged by O0/O2 | Managed diagnostics then LLVM/native stderr/exit/cleanup | NumericConversionEmissionTest, WideIntegerEmissionTest, CurrentControlFlowTest, DeferredEmissionTest, AbortEmissionTest |
| T10 / R11 | Standard vs custom getter borrowing, private setter Move, Non-Copy self-assignment/custom setter, construction prohibition and Contract slot vs value witness | B/A check exact permission/Origin; one getter/setter call and cleanup observed natively | PropertyBindingTest, PropertyCompletionBindingTest; **new** PropertyEmissionTest |
| T11 / R11,R24 | Access a static twice, first-write replacement, A→B→A initialization cycle, unused invalid initializer, reverse successful-init shutdown and shutdown reentry | B/A reject invalid dependencies; native order/cycle Abort and no extra shutdown | **new** StaticInitializationTest / StaticEmissionTest |
| T12 / R12 | Example B, nested enum/Tuple/shared Subject and guard effects, moved payloads; `.Some(0)` plus `.None` alone must not prove full Option<i32> coverage; warned arms still checked | B/A coverage/warnings/spans, G tag/payload checks, native effect/destruction order | EnumBindingTest, EnumOwnershipTest, PatternCoverageCompletionTest, MatchEmissionTest; **new** EnumEmissionTest |
| T13 / R13 | Generic `func twice<T>(x: T) -> (T, T) => (x, x)` without Copy must fail universal ownership checking even if only i32 is requested; with `T is Copy`, legal uses succeed. N=0/negative/overflow formation, Type/pair slot and Origin bounds | B/A definition/use deadlines, symbolic plans, native instantiated values | GenericConstraintCertificateBindingTest, ConstantLengthBindingTest; **new** GenericBodyTest |
| T14 / R13–R14 | Example C, explicit specialization through generic forwarding/function reference, budget zero/nonzero and reordered inputs; same implementation/result/checks; invalid unused specialization rejected | B selection, G entry/context/schema and native result; no lookup in shared body | **new** GenericGenerationTest / SpecializationEmissionTest |
| T15 / R14,R28 | Same-size different-destructor/alignment Types, finite recursion vs growing keys, full-width u64 tokens, N=0/zero stride, independent product/test classes, new instance added | G validated pairs/layout/frame/code size, deterministic plans, finite resource diagnostic, V9 counts | **new** GenericFrameTest / GenericBudgetTest |
| T16 / R7,R15 | Function item and explicit capture list with Copy/Non-Copy/borrowed captures; each receiver kind, common-value erasure, nested unused captures and input-derived results; reject hidden-receiver/call-local escape | B/A capture/Loan diagnostics; G/native cleanup and empty/inline/heap storage | FunctionTypeConstraintBindingTest; **new** CallableEmissionTest / CaptureOwnershipTest |
| T17 / R16 | Length 0/1, ^0/^1, saved Index/Range, reversed/inclusive/max-isize, Slice split/reslice/try operations, dynamic Non-Copy Move rejection, empty Slice Loan, zero-size elements | B/A intended failure; runtime bounds reason/site, O(1) allocation/count evidence | ElementEmissionTest, ElementPathEmissionTest; **new** SequenceViewTest |
| T18 / R17 | Unchanged program 7; user Iterable/Iterator, tuple bindings, Copy vs consumed arrays, continue/exit/return cleanup, repeated None, reject direct Range iteration and iterator-owned result borrow | B/A and native stdout/remaining-element destruction; exactly one iterate and next per step | ForParseTest; **new** IterationEmissionTest; milestones/Milestone7.kimi |
| T19 / R18–R19 | Example D; literal duplicate `let d: Dictionary<i32, string> = [1:"a", 1:"b"]` fails at later key; replace second literal key by a function returning 1 and verify Abort before second value evaluation; float NaN/signed-zero keys | B literal diagnostic vs native duplicate check, equality call/effect order; no artificial Hash contract | CollectionLiteralParseTest; **new** DictionaryEmissionTest |
| T20 / R7,R18,R28 | Example E; reserve/add/remove churn at full capacity; removal results retain dependencies; mutation while even empty Slice lives rejects; forced shrink allocation failure keeps placement; cleanup-free clear cost | A and native fault/counter evidence; capacity bounds and amortized relocation/index work | **new** CollectionOwnershipTest / CollectionCapacityTest; Benchmark additions |
| T21 / R19 | Interpolation of integers, bool, char, Unit, string and custom Stringify; each value/mapping once, shared source stays initialized, owned output escapes source Loan; locale-independent documented float results | B witness and A borrow, native output/allocation/destruction order | StringLiteralParseTest, StringEmissionTest; **new** InterpolationEmissionTest |
| T22 / R20 | §13.5.8–9 normal/cyclic factories: distinct make identities, clone same identity, upgrade during Building None, after publication Some, after last strong None; count overflow/allocation failure via adapters | B/A, metadata, native count/final-deinit/free evidence and atomic-order review | RuntimeTypeTest, RuntimeTestFormationBindingTest; **new** ObjectWeakEmissionTest |
| T23 / R7,R20 | Runtime `is` branches, short-circuit joins and require refinement; Move/replacement invalidates facts; inherited receiver Whole replacement fails while legal Part replacement works; unknown family effect not Proven | B/A provenance and diagnostic use/cause; native exact/base view tests after runtime exists | InheritedReceiverBindingTest, RuntimeTypeTest; **new** RefinementCompletionTest |
| T24 / R21,R24 | Valid C-layout nested/zero-sized cases, mixed i8/u16/i64/f32/f64/pointer and 5+ args; reject bool/char/aggregate/i128 imported by-value signature and logical-library collisions. Legal null+0/one-past pointer paths only | B/G error location; Clang size/offset/call oracle, LLVM object/unwind/dependency inspection, native result | LayoutAttributeBindingTest, LibraryImportTargetBindingTest, NativeToolchainTest; **new** ForeignEmissionTest |
| T25 / R25 | Example F; condition once, message only false, message/control-boundary errors, nested/defer/test-only helper verification, product helper rejection; condition/message cleanup before require Abort only | B/A membership and source site; native stdout/exit plus retained failure/actual termination | ProductTestMembershipTest, TestAttributeCertificateBindingTest; **new** VerificationOperationTest |
| T26 / R14,R25,R28 | Add tests/generic requests without changing product plan; invalid filtered-out test still rejects; list runs no init/codegen; one artifact across filters; stale ID, exit 0 without completion, flood logs, timeout/cancel/descendant pipe | B/G immutable plan evidence and runner protocol/process recovery; finite memory/time/control reserve | **new** TestDiscoveryTest / TestRunnerTest after G2 |
| T27 / R1,R5,R13,R22–R23 | Existing Project dependency scenarios plus added/removed overload/Name/specialization, changed private effects/access/constraints/source spans, rebind/save/reload/repair; no stale proof accepted | Current-source diagnostics and cold/warm equivalence; native changed selected result after regenerate | ModuleBindingTest, DependencyResolutionTest, KotonohaSerializationTest; **new** SemanticReuseTest |
| T28 / R22–R24 | Malformed/Unicode-colliding/oversized/corrupt packages, different valid ZIP compression, release conflicts across whole closure, interrupted/concurrent publication, pin/GC races, stale native summaries/ambiguous required symbols | Loader/CLI failure stage, atomic files/table snapshot, bounded work; valid local source package compiles/runs | DependencyLockTest, EmissionArtifactsTest, NativeToolchainTest; **new** PackageStoreTest / PackageCommandTest |
| T29 / R9,R24,R27 | Existing Hello/UTF-8/NUL/runtime failures, unique startup/library/no entry, unused unsupported source, stale/tampered build inputs and build/run/emit distinctions | Exact native bytes/stderr/exit, pre-optimization Library IR, object/link records, no output after rejected generation | MinimalEmissionTest, StartupBindingTest; existing backend scripts |
| T30 / R26 | Existing provisional append/rebind transition plus separate generated contexts, dependency-order permutations, missing/cyclic Mod IDs, changed observed input and final unresolved query | B/provenance/record invalidation; public host tests only after G1 | ProvisionalContractPremiseBindingTest, SourceDocumentAndDiagnosticTest; **new** ModLifecycleTest |

Representative source inputs (proposed tests, not files created in this phase):

**A — default evaluation and normal result (T4).** Expect `default\nbody\n`; return value 7. A separately supplied explicit argument must suppress the default call.

```kimi
func defaultValue() -> i32
    ::Core.writeLine("default")
    return 7
func take(value?: i32 = defaultValue()) -> i32
    ::Core.writeLine("body")
    return value
let result = take()
require result == 7 else => $abort("wrong result")
```

**B — owned enum payload (T12).** Expect `held\n`; string responsibility transfers into then out of Some exactly once.

```kimi
let value: ::Core.Option<string> = .Some("held")
match value
    .Some(let text) => ::Core.writeLine(text)
    .None => $abort("missing")
```

**C — mandatory specialization (T14), from §21.3.1.** Expect the require to succeed even when optional specialization budget is zero. Budget option spelling is an internal design decision, not invented CLI syntax here.

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1
func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let value: i32 = 7
require forward<i32>(value@ref) == 1 else => $abort("wrong implementation")
```

**D — Dictionary mutation (T19), §4.7.3.** Expect `replacement\n`; tryInsert consumes both arguments and indexed assignment replaces an existing value.

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Core.writeLine(entry.1)
names[1] = "replacement"
match names.remove(1)
    .Some(let entry) => Core.writeLine(entry.1)
    .None => $abort("missing")
```

**E — capacity and receiver Loan (T20), §4.7.2.** Accepted prefix leaves `[10, 20]` and returns 30. In the separate negative variant, `values.append(values[0])` must reach the exclusive-receiver versus element-read conflict, not fail because Array/Core is still missing.

```kimi
var values: Array<i32> = []
values.reserve(additional: 3)
values.append(10)
values.insert(^0, 30)
values.insert(^1, 20)
let last = values.remove(^1)
require last == 30 else => $abort("wrong element")
```

**F — verification failure (T25), §17.5.2.** A test-only input; failure latched at `$require`, no printed defer or final line. Runner exit encoding remains G2, so do not invent an exact parent exit code.

```kimi
#Test
func stopsAfterFailure()
    defer => ::Core.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Core.writeLine("Not reached")
```

Omissions: no expected outcomes for undefined unsafe behavior; no runtime Contract Views, mutable Slices, source threads, parameterized test extensions, unknown-substitution scratch, or NativeAOT tests. Do not build an exhaustive Cartesian feature cross-product. Select interactions that exercise distinct acquisition, cleanup, proof, ABI and invalidation boundaries; Appendix A's explicitly required cases still apply.

### Verification procedures

All commands below use working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`. They are future commands unless a V1/V12 record says otherwise. Command paths, script parameters and runner switches were checked against source/help; execution success is **not** implied. SDK observed: 10.0.401. Projects target net10.0; `global.json` selects Microsoft.Testing.Platform but does not pin an SDK version. Packages include xunit.v3 4.0.1, Microsoft.NET.Test.Sdk 18.10.0 and BenchmarkDotNet 0.15.8.

Use `backend/windows-x64/invoke-verification.ps1` for bounded external work: it captures logs/result JSON, optionally hashes declared inputs before/after, and uses `VerificationJob.cs` plus `verification-worker.ps1` for Windows process-tree management. Default limit is 900 seconds; choose a justified larger finite value for benchmarks/full native suites. Direct native fixtures already have per-process limits (normally 30 seconds; milestone wrappers 60 seconds). Keep output-drain/recovery bounds too. Never use a displayed launcher exit code alone when nested output reports failure.

Example wrapper for the focused suite, after V2:

```powershell
./backend/windows-x64/invoke-verification.ps1 -FilePath dotnet -ArgumentList @('xUnitTest/bin/Debug/net10.0/xUnitTest.dll', '-class', 'XunitTest.StructEmissionTest', '-parallelMode', 'none', '-failSkips') -InputPath @('xUnitTest/bin/Debug/net10.0/xUnitTest.dll', 'Kimi/bin/Debug/net10.0/Kimi.dll') -TimeoutSeconds 900
```

The direct DLL is the xUnit in-process runner, whose observed switches are `-class`, `-method`, `-parallelMode none`, `-failSkips`, `-list tests` and `-result-xml`. Do not mix these with MTP `dotnet test` switches or VSTest `--filter`. Require a nonzero selected case count and save it; use XML/runner output to fail zero-test runs. NativeAOT is not triggered by these managed commands. Existing stale DLL help only verified switch syntax.

| ID | Command / exact target and prerequisites | Success evidence / order / output constraints |
| --- | --- | --- |
| V1 | `git rev-parse HEAD`; `git status --porcelain=v1`; `dotnet --version`; `dotnet --list-sdks`; inspect global/project/profile files. For fresh runner help use `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll --help` only as a help probe | Record HEAD/diff, SDK/tool existence and actual errors; help exit 1 is not a failed test. Resolve G5 before restore. Validate tools/archive hashes against profile before native work; existence is not integrity evidence. |
| V2 | `dotnet restore Kimigayo.slnx`, then `dotnet build Kimigayo.slnx -c Debug --no-restore`, then Release equivalent | Restore/config access and package sources available. Zero build errors; investigate warnings/new warnings. Hash actual compiler/test DLLs and source inputs. Build sequentially; do not use old bin output if either build fails. No publish/AOT. |
| V3 | After matching V2: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.StructEmissionTest -parallelMode none -failSkips` as initial focused check; substitute exact T-table class for each item. Full gate: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips`; repeat using Release path | Managed checks and fixture generation are distinct results. All selected cases pass, count >0, no unexplained skips. Inspect T-specific tests' side effects before running. Log exact classes/methods and counts; full suite only at M1, broad semantic changes and M15 or a justified concern. |
| V4 | LLVM verification after V3 generation: `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/StructLocal.ll`; future fixture paths recorded per test. `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'` performs pre/post-O2 verification as part of V5 | Pinned LLVM 22.1.8 and a freshly regenerated exact fixture set required. Record IR hash and compiler/test configuration. LLVM verifier PASS does not prove ABI/execution. New fixtures require matching generated names, not an assumed glob. |
| V5 | `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'`; replace pattern with exact generated family; final relevant fixture set `*.ll` only after freshness inventory | Pinned installed backend and dlltool hashes; script verifies O0/O2, inspects undefined symbols, links `/NODEFAULTLIB`, executes and checks stdout/stderr/exit/timeouts. `bin/scalar-fixtures` and `bin/scalar-native` are **shared across Debug/Release**: generate → hash/inventory → execute → archive evidence before switching configuration. |
| V6 | `./backend/windows-x64/build.ps1 -LlvmBin ./toolchain` when backend changes or pinned candidate prerequisite is missing; then `./backend/windows-x64/test-emission.ps1 -ToolchainRoot ./toolchain -Configuration Debug` and Release after fresh MinimalEmissionTest fixtures | Inspect build script adoption/install side effects first; it can install a matching tested reproduction. Clang/llvm-lib/llvm-objdump plus normal tools required. No unpinned override for conformance. Candidate archive/report must match; verify C ABI/layout, unwind, no hidden CRT dependencies and runtime fault adapters. New T24 adapters/commands must be added and verified before calling them available. |
| V7 | After V2: `dotnet Kimi/bin/Release/net10.0/Kimi.dll restore examples/SourceDependencies/Geometry/Geometry.kimiproj`, then `dotnet Kimi/bin/Release/net10.0/Kimi.dll check examples/SourceDependencies/Geometry/Geometry.kimiproj --locked`; native CLI regressions: `./backend/windows-x64/test-cli.ps1 -ToolchainRoot ./toolchain -Configuration Release` | Restore intentionally writes the example lock; check must not rewrite it. Use an isolated copy when preserving an existing example lock matters. Paths verified against examples/SourceDependencies/README.md; script parameters inspected. After M12 add new isolated package/CLI tests rather than invoking unregistered commands now. Validate diagnostic category, failed publication, hashes and stale-artifact rejection. |
| V8 | V3 with `-class XunitTest.ModuleBindingTest`, `-class XunitTest.DependencyLockTest` and `-class XunitTest.KotonohaSerializationTest` (OR filters); after I29 use **new** SemanticReuseTest/PackageStoreTest classes | Same exact source/settings exercised cold, warm, reload, replacement, relocated and corrupt-input modes; compare acceptance, required diagnostics/current spans and selected results. Package/user-cache tests use isolated test-owned roots, not real publication stores. Record actual new filters when created. |
| V9 | `dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*'`; choose existing workloads by inspected Benchmark/Program.cs; new feature workloads added under Benchmark/Benchmarks | Build/restore environment ready, stable machine/load/settings, no concurrent builds/native tests; BenchmarkDotNet MemoryDiagnoser already configured. Record samples, variance, allocated bytes, retained/peak memory, cold/warm/reparse costs, source scale, IR/code size and runtime separately. New collection/generic instrumentation measures required operation/allocation counts, not just elapsed time. |
| V10 | After I30: V3 with **new** VerificationOperationTest/TestDiscoveryTest; after G2/I31: **new** TestRunnerTest using adopted CLI/transport arguments | Commands/fixtures for public runner remain NOT_RUN/BLOCKED until defined; no fabricated flags/defaults. Verify active-case protocol, all-body list/filter semantics, finite recovery, logs, failure latching, final display and artifact identity. |
| V11 | `./backend/windows-x64/test-milestone6.ps1 -ToolchainRoot ./toolchain -Configuration Debug` then Release; same verified parameter shape for scripts test-milestone1.ps1 through test-milestone5.ps1. **New** scripts for programs 7–9 only when implemented | Exact original and renamed/changed inputs, invalid/Abort cases, source/compiler/build identities; preserve expected program outputs in milestones/README.md. Integrates earlier V checks, does not replace Appendix A coverage. Add current Windows native CI evidence without package publishing. |
| V12 | `git diff --check`; `git status --short`; inspect PLAN requirement/item/test references, file/symbol existence and new markers | Planning/documentation-only gate: only PLAN.md differs; 12 required sections, no implemented support inferred from plans, no NativeAOT or draft edits. Repeat after plan edits. |

Fixture handling prerequisite for V4–V6: `ScalarEmissionTest.EmitFixture` includes implementation-specific scalar-IR assertions, so reuse its write/capture conventions without copying its incidental scalar opcode restrictions into unrelated generic/object tests. `WriteFixture` writes `.ll`, `.stdout`, `.stderr`, `.exit`, `.timeout` under shared `bin/scalar-fixtures`; it does not certify which compiler source produced an old file. In M1 establish a per-run generated-file inventory plus hashes. Use dedicated prefixes or safely move only identified stale family outputs within the verified repository bin root; do not delete user files or trust timestamp equality. No Debug/Release fixture producers/consumers run concurrently.

### Verification records (current planning phase)

| Verification ID | Target/configuration | Relevant code/artifact state | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Repository identity/rules | HEAD above; initial working tree clean | PASS | `git rev-parse HEAD`, `git status --porcelain=v1`, root AGENTS and targeted reads | At resumption and before implementation |
| V1 | SDK inventory | Local SDK only, no compiler execution | PASS | `dotnet --version` = `10.0.401`; SDK list also contains `10.0.100-rc.1.25451.107` | If environment changes |
| V1 | MTP CLI help/extension discovery | `dotnet test --help`; not a test run | BLOCKED | Help printed, then NuGet.Config access denied; output says nested build failed with code 1 although wrapper exit was 0 | Resolve G5; rerun build/runner verification, not repeated broad help |
| V1 | Existing Debug test DLL runner help | Existing artifact, not tied to current source by this plan | PASS | Banner `xUnit.net v3 In-Process Runner v4.0.1+8ed8aa354c (64-bit .NET 10.0.12)`; usage/switches printed; command exit 1 | Fresh V2 build; help validates syntax only |
| V1 | LLVM/backend inventory | `toolchain` contains required executable names and `windows_x64` directory | NOT_RUN | Versions/hashes and actual native launch not checked | Before V4–V7 |
| V2 | Debug/Release solution | Current source | NOT_RUN | No deliberate restore/build run; help-triggered discovery failed before a completed build | M1 |
| V3 | Managed conformance/fixture generation | Current source | NOT_RUN | No tests executed; historical 6,986 figure is not current evidence | M1 then milestone-focused |
| V4–V8 | LLVM/native/CLI/ABI/reuse | Current source/artifacts | NOT_RUN | Script/control-flow inspection only | After fresh matching builds/fixtures |
| V9 | Allocation/time/memory | Current source | NOT_RUN | Existing tests/benchmarks inspected, no new measurement | Baseline and changed hot paths |
| V10 | Language runner | Unimplemented public interface | BLOCKED | G2; internal semantic tests also not yet added | I30, then G2/I31 |
| V11 | Programs 1–9 integration | Historical 1–6 reports; source 7–9 inspected | NOT_RUN | No program compiled or executed in this planning phase | At relevant milestone |
| V12 | Final plan-only review | New PLAN.md; source unchanged | PASS | Twelve numbered sections; contiguous R1–R28/M1–M15/I1–I34/T1–T30/V1–V12 references checked; existing test-class references checked against repository inventory; new paths marked; `git diff --check` clean and `git status --short` shows only `?? PLAN.md`. Untracked plan whitespace checked separately. | After any further plan edit; no compiler support certified |

For future records append run ID, exact command/working directory, HEAD and diff fingerprint, source/compiler/test/tool/backend hashes, configuration, fixture inventory, result counts, logs/report paths and any timeout/cleanup outcome. Keep failures and superseded evidence. PASS survives only while its relevant inputs remain unchanged.

## 9. Risks and Implementation Notes

| Cause / trigger | Impact | Mitigation | Verification / milestone |
| --- | --- | --- | --- |
| Treating Binding certificates as complete ownership/generation evidence | Invalid executable accepted | Explicit deadlines; finalization checks current results; no fallback semantic substitutes | T5–T7/T13; M2–M6 |
| Rebinding or source reload retains stale Symbols/Loan/ABI/destructor records | Wrong call, leak, use-after-free, memory retention | Reset per-parse facts, prune strong references, invalidate dependent evidence, preserve source contexts | T27, V8–V9; all |
| Candidate/default/receiver order changes during lowering | Different effects, premature Move or Loan conflict | Retain selected evaluation/acquisition plans; default environment separate from caller names | T4/T20; M2–M3/M9 |
| Cleanup/partial construction/transfer modeled only at happy-path exit | Double destruction, skipped base cleanup, incorrect result delivery | Reuse explicit edges/responsibilities; diagnose checking continuations independently | T6/T8–T12; M3–M5 |
| Different Types/destructors share physical shape | Wrong metadata/cleanup or generic implementation | Separate semantic identity from layouts/selected implementation; keys include all embedded facts | T14–T15/T22; M4/M6/M10 |
| Optional specialization or added test expands shared frame/class | ABI/resource behavior changes with budget or unrelated tests | Budget-independent entries, exact per-entry reservation, separate product/test accounting | T15/T26; M6/M13 |
| Library/cache proof trusted from hashes alone | Stale acceptance or unverified unused body | Separate identity, semantics and native-connection validation; required all-body checks before laziness | T27–T28; M12 |
| Collection reserve/churn implemented using repeated resize/order rebuild | Quadratic work or forbidden within-capacity allocation | Growth policy, reusable slots/indices, counted relocation/order work, fault adapters | T19–T20/V9; M9 |
| User equality/destructor reentry while collection/object is inconsistent | Memory unsafety or dependency violation | Stable operation commit points, public effects, active Loans, no implicit rollback | T7/T20/T22; M3/M9/M10 |
| Atomic arc/Weak native tests mistaken for an ordering proof | Rare lifetime race | Review required atomic protocol/order separately; stress/fault tests supplement it | T22; M10 |
| Valid LLVM with unjustified alias/provenance/FP attributes | Optimized miscompilation | Only proven attributes, legal unsafe cases, Clang/ABI/object checks and O0/O2 behavior | T24/V4–V6; M11 |
| Shared fixture paths, old manifests or unpinned tools | False PASS against wrong source/configuration | Serial generation/execution, hash inventories, nonzero counts, pinned native identity | V1–V7; M1/all |
| Runner floods/retained descendant pipes | Unbounded storage/wait or lost failure | Separate control reserve, finite execution/recovery, launch-time OS process management | T26/V10; M13 |
| Scope quietly narrowed to programs 1–9 or undefined API invented | Incomplete/incorrect “completion” | R/A clause coverage and G1–G2 gates; explicit residual exclusions | V12/M15 |

Allocation implementation notes: use interned Types/identifiers and reusable candidate/CFG/layout buffers already present; avoid allocating paths/diagnostic strings on successful hot paths. Cache keys must include all semantic dependencies, including negative lookup/selection facts. Prefer compact indices, stable structural keys, streaming hashes and bounded worklists. Measure both warm reuse and first-use/reparse retention; a zero-byte warm microtest is not evidence about module loading or process startup. Add abstractions only where a concrete new representation/contract needs them.

## 10. Execution Order and Next Actions

| Step | Preconditions | Changes and rationale | Verification | Completion criteria | Failure or blocking conditions |
| --- | --- | --- | --- | --- | --- |
| 1 | Later implementation instruction; fresh HEAD/diff | M1/I1–I2 baseline and clause mapping | V1–V3, selected V4–V5 | Current evidence and known failures recorded | G5 environment, missing tools/artifacts; retain block evidence |
| 2 | M1 and selected clause reproducers | M2 then M3 semantic/ownership contracts | T1–T9/T27, V3–V5 | Checked plans usable by downstream work | Unresolved proofs or real SPEC_GAP block dependent slice |
| 3 | Checked storage/effect contracts | M4, then M5 concrete complete representations | T8/T10–T12, V3–V6 | Layout/acquisition/destruction verified together | No placeholder zero/undef or unsupported-body omission |
| 4 | Definition-side proof and concrete layout baseline | M6/I13–I15 baseline generic sharing, then I16 bounded optimization | T13–T15, V3–V6/V9 | Explicit selection/entry/frame invariants hold at all budgets | Required growth limit produces explicit resource diagnostic |
| 5 | Generic/call contracts available | M7 callables and M8 views/iteration; independent slices may be developed sequentially in either order | T16–T18, V3–V5/V9 | Program 7 and focused operations verified | Borrow escape or missing witness cannot use special-case fallback |
| 6 | General values/borrows/cleanup | M9 collections/strings; M10 objects after callable/Option support | T19–T23, V3–V6/V9 | API outcomes, effects, cost and lifecycle contracts | Undefined public extensions remain excluded |
| 7 | Checked layout/ABI; package syntax can start earlier | M11 foreign operations; M12 packages/common generation/reuse | T24/T27–T29, V3–V9 | Source closure and native connection independently validated | Corruption/stale proof blocks publication, not hidden by cache |
| 8 | Product semantics fixed | I30 test semantics/discovery; I31 only after G2; I32 external host only after G1 | T25–T26/T30, V3/V8/V10 | Settled contracts verified, public interface blocks resolved explicitly | G1/G2 keep affected items BLOCKED |
| 9 | Relevant features ready | Unchanged programs 8–9 and full M15 clause/interaction audit, docs/performance | V2–V12 | Section 11 satisfied, residual exclusions accurate | Historical results cannot substitute for missing evidence |

Resumption instructions:

1. Read Sections 1–2 and 5, then inspect actual HEAD/diff. Preserve any later user changes. Update baseline/time and invalidate only evidence whose inputs changed.
2. Choose the first dependency-ready I item; mark IN_PROGRESS and record targeted R/T/V IDs before coding. If support already exists, verify and close that item with evidence rather than implement it again.
3. Confirm relevant owning clauses and exact existing tests/helpers; reduce each observed gap to a positive/negative reproducer at the correct stage. Extend stable T IDs with case suffixes.
4. Implement the smallest coherent vertical slice. Keep unfinished paths rejected before generation. After source changes, run focused checks, then the milestone's native/performance interactions; broaden only according to impact.
5. Use IMPLEMENTED_UNVERIFIED until all required evidence exists. Record command/result/artifact identity immediately; update STATUS only for verified product support. Reopen DONE when a dependency change invalidates evidence.
6. Carry unresolved decisions forward with effect and resolution condition. Do not silently delete superseded items or turn an implementation limit into a language restriction.

## 11. Definition of Done

The plan is complete when its scope, architecture, stable coverage IDs, dependencies, gates and honest evidence records are reviewable, and only `PLAN.md` changed. **That is not compiler completion.**

Compiler completion requires:

- Every finalized in-scope R family and mandatory Appendix A case mapped to completed implementation/verification evidence, including unused/unreachable source and rejection boundaries.
- No missing/Unknown/unfinished proof used as success, no incorrect semantic fallback, no required operation left rejected merely because an old emitter subset cannot lower it.
- Binding, ownership, physical validation, LLVM verification, native ABI/linking and ordinary execution independently demonstrated where applicable; Debug/Release compiler acceptance and O0/O2 observable behavior agree.
- All new representations preserve evaluation count/order, complete Types/Origins, Loan authority, initialization, normal cleanup, Abort and result-delivery invariants.
- Packages/reuse/native inputs correctly distinguish identity, proof and connection validity; no-cache and reused results agree, corrupted/stale inputs cannot certify artifacts, failed runs cannot leave valid-looking success records.
- Required allocation/complexity bounds tested; practical performance/retained-memory costs measured against the same workload/configuration. No unjustified universal zero-allocation claim.
- G1/G2-dependent public interfaces either separately specified and completed, or explicitly reported as excluded, blocked integration with no blanket claim that their dependent settled execution contracts are finished. A remaining included guarantee prevents claiming the entire compiler complete.
- English SPEC choice notes/STATUS/usage/examples accurate, no planned support labeled implemented, no draft changes or NativeAOT requirement introduced.
- Every relevant verification record has commands, counts, source/artifact/tool identity and retrievable evidence. No unresolved required failure or environment blocker is hidden by historical PASS reports.

## 12. Plan Changes and Out-of-Scope Findings

| Date | Change / disposition | Rationale |
| --- | --- | --- |
| 2026-09-17 | Created PLAN.md at the user-designated path; implementation remains TODO | Full finalized-spec completion requires a maintained execution record, not a single-feature patch. |
| 2026-09-17 | Reused existing struct/borrow/CFG/Project graph work rather than planning replacements | Inspected current code is newer than some STATUS summaries; program milestone numbers are distinct from M IDs. |
| 2026-09-17 | Retained object/Weak and collection contracts in scope; separated undefined host/test interfaces | Owning specifications settle former features while explicitly deferring the latter interfaces. |
| 2026-09-17 | Recorded help-triggered NuGet access failure and existing runner help separately from tests | No current conformance test run occurred; planning did not justify restore/build or native artifact mutation. |
| 2026-09-17 | Corrected representative optional-parameter syntax, verified actual dependency-example commands, and assigned generic enum integration to M6 | Final review prevented an unintended earlier error in T4 and a circular M5/M6 completion dependency. No baseline requirement was removed. |

Out-of-scope findings: general LSP/editor integration, KimiCode configuration, debug information, additional target profiles, remote registries/network publication and NuGet distribution automation are not necessary to implement the finalized compiler contracts here. Existing CI improvements may be extended only as needed for conformance evidence. Public APIs omitted by the specification belong in separate design work. No unrelated fix, draft edit, package publication, or implementation change is part of this planning delivery.
