# Kimigayo Compiler Completion Plan

## Program Milestone 8 integration (2026-09-17)

This request targets `milestones/Milestone8.kimi`, independently of this plan's
M1–M17 stages. HEAD `cf6fa41` and the initial worktree were clean, so there were
no uncommitted changes to archive. Root AGENTS.md and all fourteen current
programs were read (the request referred to five). Programs 1–7 supply scalar
execution, functions, destruction, borrowing, transfers and sequence views.
Program 8 requires nested groups, generic construction/ownership and direct
fixed-array iteration. Programs 9–14 require additional callbacks, enums,
length generics, specialization, Iterator and object support; those remain outside
this checkpoint. No draft or specification program is modified.

The current-source Debug baseline built cleanly and 174 focused tests passed.
The unchanged target first failed final Binding at generic construction calls
and direct `for` iteration. Ownership and generation were initially untested,
not established failures. Evidence is under `bin/milestone8-work/`.

**Program 8 COMPLETE.** The unchanged target passes Binding, universal ownership
checking, LLVM verification, linking and ordinary native O0/O2 execution. Exact
stdout is `Chosen item.\nBoxed array total is 12.\nGeneric scope finished.\n`,
with empty stderr and exit 0. Generic field acquisition retains CopyOrMove path
state; deinit-bearing partial acquisition and possible reuse are rejected at
definition. Generation shares each admitted generic storage CFG, with concrete
ABI entries, deduplicated symbolic type policies, immutable 8-byte context slots
and operation/context pairs. Entry scratch reserves only required local storage,
excluding parameters/results and empty values; conditional destruction flags are
limited to paths whose live state can vary. Copy fixed arrays are acquired once
for iteration. No source-specific special case or language-rule relaxation is used.

Final Debug/Release solution builds pass with zero warnings/errors. Both full
suites pass 7,567 tests each with no failures/skips, including 40 added cases.
Tests cover Copy and Move instantiations, shared-body/policy identity, exact frame
capacity, reverse field destruction, unused definitions, malformed lowering plans,
reanalysis, snapshot/empty array loops, and related invalid source rejection.
The new `backend/windows-x64/test-milestone8.ps1` passes 46 checks per configuration:
unchanged/renamed inputs, both choices, alternate/empty arrays, explicit Abort,
and thirteen rejected inputs. Completed programs 1–7 also pass their respective
25/21/37/33/47/63/52 checks in both configurations: 648 integration checks total.
Each final report matches the current compiler and source SHA-256 hashes.

LLVM verification and native O0/O2 regressions pass for 13 GenericStorage,
23 Sequence, 466 Element, 18 Struct and 50 Reference fixtures: 570 fixtures and
1,140 executions. Final Debug/Release fixture regeneration matches the saved
native IR/expectation hashes; native execution and regeneration were serialized.
Evidence, logs, hashes and the final summary are in
`bin/milestone8-work/verification.json`. Original LLVM sandbox permission denial
was resolved by authorized execution; no required check remains unverified.
NativeAOT tests were not run. Work stops at program 8; the broader plan stages
below retain their separate incomplete obligations.

The shared backend currently admits owned transfer/placement/cleanup, boolean
selection and direct generic stored fields. Generic forwarding calls, arbitrary
symbolic compound fields, length-dependent storage, specialization, borrowed
generic ABI and general generic arithmetic remain guarded. Direct iteration is
limited to supported scalar Copy fixed arrays; general owned element iteration,
Slice iteration and user Iterable/Iterator dispatch remain separate work.

## Program Milestone 7 integration (2026-09-17)

This request targets `milestones/Milestone7.kimi`, independently of this plan's
broader M1–M17 stages. The initial worktree at `6496482` was clean. All nine
checked-in programs were read (the request referred to five): 5 supplies borrowing
and destruction, 6 supplies value control flow and transfers, 7 adds sequence
metadata/iteration/views, 8 needs generic ownership, and 9 additionally needs
length generics, callbacks and generic enum results. Programs 8–9 are not being
implemented in this checkpoint.

The current-source Debug baseline built cleanly; 353 focused existing tests passed.
The unchanged program first failed final Binding at 9:11 (`for`), with additional
unimplemented `indices` and full-range Slice syntax. Ownership and generation
were then untested, rather than established failures. Evidence is retained under
`bin/milestone7-work/`, including `baseline.log` and `baseline-tests.log`.

Implemented the vertical subset in dependency order: array/Slice metadata and
independent ResolvedRange snapshots; immutable isize for bindings and finite
range iteration in the existing ownership CFG; full Slice creation, Copy handles
with backing Origins, and checked scalar indexed reads. Existing element
projections retain receiver evaluation, bounds checks and inline addresses;
existing transfer cleanup handles nested continue/exit/return/yield. Slice Loans
are tracked through copies and last uses, including index evaluation, and retain
static path footprints so writes to a different row remain legal. Physical range
and Slice handles use two words; neither copies backing element storage or
allocates it on the heap. Generation validates sequence plans against their
evaluated sources. No source-program rewrite, filename match, numeric-output
special case, or change to the language rules is used.

**Program 7 COMPLETE.** Debug/Release solution builds pass with zero warnings/errors;
both full suites pass 7,527 tests each, including 40 new sequence cases, with no
failures/skips. The Milestone 7 script passes 52 checks per configuration, including
unchanged and renamed O0/O2 inputs, alternate arithmetic and transfer paths,
matrix/Slice bounds Abort, and thirteen rejected inputs. Exact expected stdout,
empty stderr and exit 0 are verified for the original; Abort variants exit 1.
Reports: `bin/milestone7/Debug/dabe834f49cf46f1aea7b8b66c29c4c9/verification.json`
and `bin/milestone7/Release/602c33a1a4694ec0897c46e9377e418d/verification.json`.

Completed programs 1–6 pass 25/21/37/33/47/63 checks respectively in each
configuration (226 each). LLVM verification and O0/O2 native regression pass for
19 Sequence, 466 Element, 18 Struct and 50 Reference fixtures: 553 fixtures and
1,106 executions. All were freshly emitted by the final Debug suite. All 2,765
fixture/expectation files regenerated by the final Release suite match their
saved SHA-256 hashes, linking that native evidence to both builds without
unnecessary duplicate executions. Native generation/execution and managed fixture
regeneration were serialized. Logs, hashes and `verification.json` are retained
under `bin/milestone7-work/`. Initial LLVM permission denial was resolved by
authorized execution; no required check remains blocked or unverified. NativeAOT
and new throughput benchmarks were not run. Work stops at program 7.

I19/I20 and plan stage M8 remain incomplete: this integration implements the
built-in ResolvedRange path from `indices`, not general user-defined
Iterable/Iterator dispatch. General Core sequence declarations and explicit Type
APIs, Index/from-end/bounded Range operations, arbitrary Slice element results,
direct collection/Slice iteration, tuple iteration bindings and generic
collection/iteration ABI remain separate work. Mixed Slice provenance widens to
a conservative root footprint. The unchanged program requires none of these
later extensions. SPEC.md already defines the implemented behavior; this task
does not change its rules. Drafts and NativeAOT are excluded.

Concurrent workspace changes observed at final review added Milestones 10–14,
their README sections, and the SPEC index link. They were preserved and are not
part of this task's implementation or verification. The original nine programs,
including the target, remain unchanged. The shared milestone README retains both
the program-7 verification section and those independent additions.

## 1. Goal and Scope

### Baseline

Complete the compiler for all finalized language rules and implementation contracts in `SPEC.md`, Chapters 1–22, and normative Appendix A. Completion means correct acceptance, required rejection and warnings, ownership verification, checked generation, artifacts, and execution on the specified Windows x64 profile. Parsing or successful LLVM verification alone is insufficient.

The initial planning phase changed only `PLAN.md`. Subsequent explicit implementation requests authorize the unfinished in-scope work and necessary tests/documentation under `prompts/implementation-execution.md`. Preserve user changes and do not edit `draft/`. NativeAOT is neither run nor a completion requirement. Ordinary LLVM/native execution is a separate verification layer. A user-specified time budget applies to its own execution; earlier 60-minute deadlines are historical.

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

- Timed execution closed **2026-09-17T15:04:22+09:00**; elapsed **20m17s**. The 20-minute limit has elapsed. Units 31–32 are DONE; no further unit was started. M2/M3 remain IN_PROGRESS. Final diff checks pass; the workspace is coherent and uncommitted.

- Last updated: **2026-09-17T15:03:00+09:00** (Asia/Tokyo).
- Current execution: 20-minute soft limit starting **14:44:05 JST**, including inspection and verification; deadline **15:04:05 JST**. At the deadline, finish the current bounded unit or leave a coherent documented checkpoint; do not start another unit. Earlier execution limits are historical.
- Fresh checkpoint: HEAD `cf6fa41`, with the completed program-8 changes still uncommitted. Re-read the current instructions, plan and affected implementation. Preserve those changes; initial tracked/untracked files and patch are archived under `bin/plan-execution/20260917-144405/`. Program 8 is complete; resume the broad plan's latest explicit M2/M3 next action rather than starting program 9.
- **Unit 31 / I6/I8 T4n-r DONE:** unchanged mixed joins retain their constituent targets. Both clean builds and full suites PASS 7,583/configuration; LLVM/native regression PASS 380 Never + 414 Default executions, including 32 new executions. Milestone 1/8 PASS 25/46 checks per configuration. Current evidence: `bin/plan-execution/20260917-144405/`.
- **Unit 32 / I6/I8 T4n-s DONE:** started 14:59:35 JST (15m30s elapsed), verified at 15:03 JST. Bare checking-only transfers retain original targets and pre-cleanup seeds, including consecutive transfers. Both clean builds and full suites PASS 7,594/configuration; 32 new ordinary O0/O2 executions PASS. All prior Never/Default native fixture and expectation hashes match final regeneration. Assignments/calls/operand effects remain guarded; no unverified implementation work remains in units 31–32.
- Previous resumption (unit 30): HEAD `97bebe1` (user commit `.`); the working tree was clean and unit 29's implementation and evidence were available. Root AGENTS.md, plan, relevant specification, implementation and verification setup inspected; no nested AGENTS.md. Current evidence root: `bin/plan-execution/20260917-092418/`.
- Previous checkpoint: **M2/M3 IN_PROGRESS**. **T4n-q DONE** (execution unit 30): per-path transfer targets support completing loop/while continuations and filter internal transfers. Full Debug/Release suites PASS **7,487/configuration**, zero failures/skips, and new fixtures PASS **32 ordinary native O0/O2 executions/configuration**. Broader native regression PASS **348 Never + 414 Default executions**; Milestone 1 PASS **25 checks/configuration**. Unit 29's T4n-p evidence is historical and preserved. Changes are uncommitted.
- Current next action after this timed stop: reproduce `dotnet Kimi/bin/Debug/net10.0/Kimi.dll check bin/plan-execution/20260917-144405/next-mixed-effect.kimi` (UnsupportedOwnership_Kd at 13:13). Carry subsequent checking effects per target before an enclosing extent filters paths; do not propagate one merged state or omit a path. This requires a separate coherent unit and is not started in the remaining minute. Outer-mutating divergent loops, deferred-cleanup joins, unequal Loan joins, short-circuit conditions and broader M2/M3 remain incomplete.
- Earlier executions: the 02:42–03:42 run completed T4a–T4f; the 03:43–04:43 run completed T4g–T4m and added 91 tests. Their changes are part of the current committed baseline and are preserved by this run.
- Historical execution baseline evidence is retained under `bin/plan-execution/20260917-024236/`; the 04:43 execution verified `bin/plan-execution/20260917-034310/final-manifest.txt` at its start. Preserve all prior changes and the committed user draft.
- Previous execution: 01:42:22–02:42:20 JST, 59m58s; T1a, T5a/G9, T27a, T27b and T7a DONE. Final full suites passed 7,050 cases per configuration. Native Milestone 1 passed 25 checks per configuration at T27b. These records remain tied to their inputs; rerun affected checks after new changes.
- Previous execution milestones: **M2/M3 IN_PROGRESS**. That run verified **T4n-a–g DONE**, seven bounded units and 95 additional cases. Final full suites pass **7,302/configuration**, zero failures/skips; builds have zero warnings/errors. Final native replay passes **444 O0/O2 executions**, and Milestone 1 passes **25 checks/configuration**. No implementation unit remains unfinished. All changes remain uncommitted; detailed evidence and remaining limits are in execution unit 20 below.
- Historical next action (resolved by unit 21): **I6/I8 T4n-h**, reproduce `bin/plan-execution/20260917-044326/next-scope-continuation.kimi` and propagate a correct checking continuation out of a noncompleting default do body with local scalar effects. That baseline CLI rejected the initialized caller-local read at line 8:9; final checks now accept it. Do not substitute scope-entry state for terminal state. General effectful/branching joins, recursive-default completion proof, direct Never operand fitting, generic Copy proof, escaping Borrow/captures, mixed nested-array Binding and owned/default cleanup remain unfinished.
- T4a checkpoint (about 9 minutes elapsed): **IMPLEMENTED_UNVERIFIED**. `BoundDefaultArgument` and reusable `BoundCall.DefaultArguments` retain selected omitted expressions, parameter Symbols and substituted parameter Types. Winner slots are reconstructed from the committed receiver/argument mappings; defaults do not participate in inference or selection. Twelve new cases and 105 focused cases pass, including forward declarations, chained/named defaults, inherited Type and declared Origin substitution, declaration replacement, reload and zero-allocation warm Binding. Full suites are running. A first inherited fixture placed a container-parameter premise under a nongeneric method and reached an earlier syntax/Binding boundary; moving that premise to its container makes the intended inherited-substitution test reach the call plan.
- **T4a DONE at about 02:53 JST (11 minutes elapsed)**: both builds have zero warnings/errors and both full suites pass 7,062 cases, zero failures/skips. Runtime rejection remains unchanged. Next unit **I4/T4b IN_PROGRESS**: audit/check every default expression's control flow and ensure its transfers cannot target outside the default, including when all calls supply that argument. This prerequisite precedes any default-execution support; existing `ControlFlowAnalysis.VisitFunction` visits only the function body.
- T4b checkpoint (about 16 minutes elapsed): **IMPLEMENTED_UNVERIFIED**. Ten original-code reproducers failed: default flow was absent, value-producing selections/do were discarded, and jumps escaped into outer function/loop bodies. Defaults are now visited independently, recognized as value contexts and form a transfer boundary. Twelve new cases and 222 focused cases pass, including bodyless requirements, checking-only branches, internal exits, independent callee completion and zero-allocation warmed reanalysis. Both clean builds pass after a helper-order warning fix; final full suites are running.
- **T4b DONE at 03:01 JST (about 18 minutes elapsed)**: both clean builds and full suites pass, 7,074 cases per configuration. Next **T4c IN_PROGRESS**, a bounded I4/I8 default-execution slice whose call-plan and flow prerequisites are T4a/T4b: support scalar literal/preceding-parameter arithmetic defaults through prepared argument slots, ownership and lowering. Check every default declaration; keep generic, borrowed/owned, effectful and other unsupported default expressions rejected. Other M2/M3 obligations remain open. Required evidence: original-code emission failures, ordering/snapshot/repeated-call cases, meaningful malformed-plan checks, full Debug/Release suites and new O0/O2 native execution/Abort cases before DONE.
- Remaining: I3 final R1–R3 clause closure; I4 default/call/inference plans; I5 remaining Contract/projection/Core obligations. Effect-family verification remains I7/M3, default ownership/cleanup I8/M3, persistent semantic artifact reuse I29/M12. G9 is resolved; G1/G2 still block only their undefined public integration slices.
- Applicable rules: root AGENTS.md; no nested instruction file found. English documentation, practical allocation/performance optimization, no draft edits and no NativeAOT tests. The current 20-minute soft stopping rule takes precedence over continuing through all remaining work.
Use states `TODO`, `IN_PROGRESS`, `IMPLEMENTED_UNVERIFIED`, `DONE`, `BLOCKED`, `NOT_APPLICABLE`. DONE requires the milestone's acceptance evidence, not historical reports. Use verification results `PASS`, `FAIL`, `NOT_RUN`, `BLOCKED`.

| ID | State | Completion evidence or remaining work | Next action |
| --- | --- | --- | --- |
| M1 | DONE | 2026-09-17 at HEAD `8c49edb`: I1 baseline (V1–V3 PASS in both configurations, V4–V5 Struct-family PASS, G5 resolved) and I2 Appendix A clause coverage map recorded in Section 4; no discrepancy observed, all uncovered clause families assigned to existing T/I IDs | M2/I3 |
| M2 | IN_PROGRESS | Selected defaults, declaration checks and omitted-call completion verified through T4n; I3 final clause closure and broader I4/I5 obligations remain | Remaining inference/call closure and declaration ownership proofs |
| M3 | IN_PROGRESS | T4n-a–s verify bounded noncompletion/checking, terminal-path joins, target-aware loops, unchanged mixed-target joins and bare-transfer continuations. General effects/Loans/refinement, effectful divergent loops, effects after mixed joins, deferred cleanup, unequal Loan joins and owned/borrowed defaults remain incomplete | I6/I8 per-target checking effects (unit 32 next action) |
| M4 | TODO | Concrete structs already have execution support; inherited layout/accessors/static storage need integration | I9–I11 |
| M5 | TODO | Concrete enum semantic plans exist; general enum/Pattern execution absent | I12 |
| M6 | IN_PROGRESS | Program 8 adds universal stored-field CopyOrMove checking and shared transfer/selection CFGs with concrete entries and fixed frames; broader universal bodies, explicit selection and generic operations remain incomplete | Remaining I13–I16 obligations |
| M7 | TODO | Capture syntax and Callable identities exist; Closure/function-value execution incomplete | I17–I18 |
| M8 | IN_PROGRESS | Program 7 sequence subset and program 8 scalar Copy fixed-array iteration implemented (see integration checkpoints above); general sequence Core APIs/witnesses and Iterable/Iterator dispatch remain unfinished | I19–I20 |
| M9 | TODO | Collection/Stringify/comparison contracts specified; executable Core incomplete | I21–I23 |
| M10 | TODO | Object/Weak APIs and profile specified; generation/runtime incomplete | I24–I25 |
| M11 | TODO | Import declarations/pointer syntax partially supported; native execution boundary needs completion | I26 |
| M12 | TODO | Project graph/restore/lock support exists; packages/common generation/semantic persistence incomplete | I27–I29 |
| M13 | TODO | Product exclusion exists; internal test verification/discovery can be developed | I30; external runner interface slice blocked by G2 |
| M14 | BLOCKED | Public Mod host interfaces excluded until separately specified | I32 semantic lifecycle audit can proceed; no invented host API |
| M15 | TODO | Full conformance, performance and documentation closure depend on prior milestones | I33–I34 |

Initial states were TODO except I31 (BLOCKED by G2) and I32's public interface slice (BLOCKED by G1). Current item states and completed slices are recorded in Section 7.

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

### Appendix A clause coverage map (I2, 2026-09-17)

Method: every Appendix A section was read against the 190 executed test classes of run `m1-8c49edb` (per-class counts from `full-debug.xml`) and a keyword scan of `xUnitTest/Tests`. "Baseline" means executed, passing classes exercise the section's inspected clauses at the named stages; "Partial" means only some clauses or only earlier stages (P/B) are exercised; "Absent" means no executed test reaches the clause family. A PASS on a listed class is evidence for the clauses that class actually asserts, not for the whole section; the remaining clauses are the coverage obligation of the assigned T IDs. No new stable ID was needed: every uncovered family already maps to a planned T/I item, and no observed discrepancy (failing or contradictory test) exists at this HEAD.

| Appendix A section | Existing executed classes (count) | Coverage | Uncovered or later-stage clauses → assigned IDs |
| --- | --- | --- | --- |
| A.1 Source identity/incremental analysis | SourceEncodingTest (18), SourceDocumentAndDiagnosticTest (13), KotonohaSerializationTest (4), ContainerFragmentBindingTest (29), ModuleBindingTest (206), StartupBindingTest (73) | Baseline for snapshot/context retention and root wrapper; Partial for reuse of verified semantic records | §18.7 correspondence/dependency reuse → T27/I29; no Mod output integration tests → T30/I32 |
| A.2 Directive validation/selection | DirectiveConditionValidationTest (58), CompileTimeSwitchParseTest (41), CompilationSpecificationTest (74), DeferredEmissionTest (47), DependencyConfigurationTest (38) | Baseline | Eager/deferred/cached parsing agreement on mandatory categories only through reload tests → T2/T27 |
| A.3 Binding, caches, incremental validity | BindingTest (55), ApiAccessBindingTest (35), InheritedNameBindingTest (19), DefaultBindingTest (14), FunctionParameterIdentityBindingTest (22), 60+ `*BindingTest` classes for Types/constraints/projections/access, ModuleBindingTest (206), IdentifierIdentityTest (21), ReceiverShorthandTest (23) | Baseline for lookup/access/candidate rules and certificates; Partial for effects and stale-artifact invalidation | Effect-family fixed points and completed public `ObjectCompatible` status (existing InheritedReceiverBindingTest verifies only internal Unknown rejection) → T7/I7, T23; default Loans and partial-call cleanup at execution → T4/I4; §21.3.4 stale-artifact rejection → T27/I29 |
| A.4 Property requirements | PropertyBindingTest (69), PropertyCompletionBindingTest (11), PropertyApiAccessBindingTest (28), PropertyProjectionApiAccessBindingTest (37), AccessorReceiverBindingTest (30), InferredPropertyAccessBindingTest (13), PropertyParseTest (8), PropertyRevisionParseTest (102) | Partial: declaration/B stage only; `OwnershipAnalysis.Collector` still reports Unsupported for accessors | Accessor ownership/lowering, custom getter/setter execution, static accessor effects → T10/I10; first-access static initialization/shutdown (0 hits) → T11/I11 |
| A.5 Raw pointer backend | LayoutAttributeBindingTest (42), LibraryImportTargetBindingTest (8); `unsafe` appears in 34 classes as syntax/Binding context | Partial: P/B only | §21.5.3 instruction mapping, provenance and null/one-past arithmetic → T24/I26 |
| A.6 Literal representation | NumberLiteralParseTest (70), NumberLiteralScanTest (130), CharLiteralParseTest (76), StringLiteralHelperTest (29), KotoHelperStringLiteralTest (37), StringLiteralIntegrationTest (7), KotonohaSerializationTest (4), FloatEmissionTest (63) | Baseline | Interpolation (parse-only hits) → T21/I23 |
| A.7 Cleanup analysis/lowering | DeferredEmissionTest (47), AbortEmissionTest (38), StructEmissionTest (39), BorrowStructEmissionTest (31), ElementMoveEmissionTest (69), ElementParameterMoveEmissionTest (54), AggregateResultEmissionTest (42), UnreachableOwnershipTest (63), OwnershipAnalysisTest (64) | Baseline for concrete scalar/string/tuple/array/root-struct paths | Base/derived per-layer completion and destruction order, base slicing rejection, final-reference object cleanup → T8/I9, T22/I25 |
| A.8 Callable/object verification | FunctionTypeConstraintBindingTest (23), RuntimeTypeTest (56), RuntimeTestFormationBindingTest (15), InheritedReceiverBindingTest (57), OwnershipAnalysisTest/FunctionEmissionTest capture rejections | Partial: identities and rejection paths only; no Closure/function-value execution (0 `closure` hits, `anonymous` only in parse tests) | Captures, three receiver kinds, erasure, per-call Origins → T16/I17–I18; object creation/strong count/Weak (no Core Weak entry, G4) → T22/I24–I25; multi-level generic base projection → T23/I13 |
| A.9 Refinement/require | ControlFlowConformanceTest (101), CurrentControlFlowTest (83), GuardEmissionTest (57), StringGuardEmissionTest (36), RefinementNameBindingTest (19), DeepRefinementPremiseBindingTest (9), UnresolvedRefinementBindingTest (18), PendingRefinementDeclarationBindingTest (13), AssociatedRefinementCapabilityBindingTest (14) | Baseline for statement-form require and static refinement | Runtime `is` refinement with objects and inherited receivers → T23/I24 |
| A.10 Enum/Pattern | EnumBindingTest (74), EnumOwnershipTest (60), PatternBindingTest (89), PatternTypeFormationBindingTest (15), PatternCoverageCompletionTest (4), PatternWarningCompletionTest (7), MatchEmissionTest (73), MatchOwnershipTest (59), EnumGenericConstraintBindingTest (11), EnumContextBindingTest (10) | Baseline for B/A coverage/warnings and scalar/string match G | Enum tag/payload layout and owned/shared payload lowering (`AggregateLayoutPool.Get` admits no general enum) → T12/I12 |
| A.11 Static Contract | ContractBindingTest (73), ConditionalConformanceBindingTest (48), ConditionalMember*/ConditionalPremise*/ConditionalProjection* (127), Associated* (108), Closed* (93), NormalizedConstraintProofBindingTest (24), ProvisionalContractPremiseBindingTest (13), StructuredConstraintSubjectBindingTest (34) | Baseline | Artifact/cache invalidation of proof dependencies → T27/I29; executable witness use (Stringify/Equatable/Comparable have 0 execution hits) → T21/I23 |
| A.12 Generic schemas/specialization | GenericCallFormationBindingTest (16), GenericConstraintCertificateBindingTest (13), GenericTypeArgumentBindingTest (25), TypeArityBindingTest (28), PairAnnotationBindingTest (24), PairGroupingBindingTest (23), ConstantLengthBindingTest (48), ArrayInferenceEmissionTest (30), ArrayArgumentEmissionTest (31) | Partial: schema/B and fixed-array slots; `specialize` appears only in parse/startup tests; `ValidLength` has 0 hits | Universal body checking, public ValidLength obligations, specialization closure/selection, symbolic plans → T13–T15/I13–I16 |
| A.13 Sequence access | ElementEmissionTest (57), ElementPathEmissionTest (50), ElementAssignmentEmissionTest (78), ElementUpdateEmissionTest (114), ElementBorrowEmissionTest (76), ElementBorrowOwnerEmissionTest (60), ElementReplacementEmissionTest (53), RangeIndexParseTest (5), ForParseTest (4), CollectionLiteralParseTest (4) | Baseline for fixed-array isize element paths; Absent beyond parsing for Index/Range/Slice/iteration/dynamic collections (`Slice`, `Iterable`, `Dictionary` hits are parse/Type tests only) | Index/Range/ResolvedRange/Slice → T17/I19; iteration protocol and program 7 → T18/I20; Array/Dictionary mutation, capacity, cost → T19–T20/I21–I22 |
| A.14 Layout/LLVM/runtime | MinimalEmissionTest (23), ScalarEmissionTest (31), IntegerEmissionTest (57), WideIntegerEmissionTest (86), ConversionEmissionTest (192), NumericConversionEmissionTest (65), FloatConversionEmissionTest (31), DivisionEmissionTest (52), BitwiseEmissionTest (65), CharEmissionTest (75), StringEmissionTest (43), StringComparisonEmissionTest (34), StringFunctionEmissionTest (40), StringResultEmissionTest (45), ResultEmissionTest (34), AggregateEmissionTest (34), AggregateFunctionEmissionTest (44), IdentityAcquisitionEmissionTest (34), ReferenceEmissionTest (67), EmissionArtifactsTest (23), EmissionPlanTest (20), NativeToolchainTest (15), Kernel32ImportsTest (8), ToolchainResolverTest (7), LayoutCertificateBindingTest (10), StartupBindingTest (73) plus `backend/windows-x64/test-*.ps1` | Baseline for startup, scalar/string/aggregate ABI, instructions, constants/FP, runtime failures, backend supply and profile | Callable storage, metadata/sharing, generic entry/context/frames/lengths/budgets → T14–T16/I15–I18; Weak/objects → T22/I24–I25; FFI execution → T24/I26; enum match execution → T12/I12 |
| A.15 Control flow | ControlFlowAnalysisTest (76), ControlFlowConformanceTest (101), ControlFlowRevisionParseTest (20), ControlTransferParseTest (4), CurrentControlFlowTest (83), BlockSyntaxParseTest (70), FunctionBodyParseTest (9), GuardEmissionTest (57), SemicolonParseTest (29), FrontEndSyntaxTest (110) | Baseline; program 6 native evidence is historical (V11 NOT_RUN this run) | Anonymous-function expectations at execution → T16/I17; formatter/refactoring tool rows are out of scope (Section 12) |
| A.16 Dependencies/artifacts/reuse | DependencyLockTest (20), DependencyResolutionTest (22), DependencyConfigurationTest (38), SolutionInputTest (21), ModuleBindingTest (206), EmissionArtifactsTest (23), CommandOptionsTest (7), AllocationMeasurementTest (2) | Partial: identity/locks/native artifacts; Absent for packages, storage, semantic reuse and measurement rows | Pack/publication/storage → T28/I27; semantic reuse/cold-warm equivalence → T27/I29; common generation and native supply validation → T28–T29/I28; measurements → V9/I16, I33 |
| A.17 Test verification/runner | ProductTestMembershipTest (35), TestAttributeCertificateBindingTest (10) | Partial: membership/exclusion only; `$require`/`$expect` have 0 hits | Verification operations, cleanup, identity/protocol → T25/I30; bounds/recovery, CLI/artifacts → T26/I31 (BLOCKED by G2) |

Genuine gaps confirmed by this audit (all already carried by the checklist, so no requirement or item changes): executable Properties/statics (A.4), pointer/FFI execution (A.5, A.14), Closures/function values (A.8, A.14, A.15), object/Weak runtime (A.8, A.14; G4), enum layout/lowering (A.10, A.14), universal generic bodies/specialization/sharing (A.12, A.14), Index/Range/Slice/iteration/dynamic collections (A.13), Stringify/interpolation/comparison witnesses (A.6, A.11), packages/semantic reuse/measurements (A.16), and `$require`/`$expect`/runner (A.17). Everything else in A.1–A.3, A.6–A.7, A.9–A.11 and A.15 has an executed passing baseline at this HEAD that must be preserved by later milestones (regression gates, not reimplementation tasks).

## 5. Gaps and Decisions

| ID / classification | Evidence and affected requirements/milestones | Resolution / effect |
| --- | --- | --- |
| G1 `SPEC_GAP` | §20.7.7 and D list concrete Mod query/marker APIs, packaging/compatibility, settings and host limits as design work. R26/M14 | Separate public API/host design must define these before external implementation. Retain existing append/rebind infrastructure and verify settled lifecycle rules internally. Do not treat illustrative `IMod`/`ModContext` as adopted interfaces. |
| G2 `SPEC_GAP` | D.4 explicitly defers temp-directory API, deadlines/recovery defaults/options, budgets, transport/report schemas, empty-set option, exits and ID encodings. R25/M13 | Separate consolidated profile decision required before I31: settle all listed interfaces and bounds together. I30 semantic membership/discovery/verification work is independent. Do not publish a supposedly complete runner using invented public contracts. |
| G3 `INVESTIGATION_GAP` | D's collection storage entry versus settled §4.6.8/§4.7 complexity/storage freedom. R16–R18/M8–M9 | Public storage ABI remains excluded. Proposed internal Slice location/length and Array contiguous storage are implementation choices, subject to layout/Loan/zero-size validation. Proposed Dictionary indexed slots/free-list/order links allow allocation-free churn; benchmark before optimizing search. No user approval is needed for ordinary private layout choices within these contracts. |
| G4 `IMPLEMENTATION_MISMATCH` | Core catalog omits required Weak and leaves object operations missing with a stale deferral comment, despite §22.1 and §13.5.8–9. R19–R20/M2, M10 | Add canonical declarations/identities with their implementation milestones; update catalog tests intentionally. Do not make `IsCompleteLibrary` true just by changing a count or weakening required shapes. |
| G5 `INVESTIGATION_GAP` — **RESOLVED 2026-09-17** | `dotnet test --help` printed MTP help then attempted extension discovery/build, reporting `Access to the path 'C:\Users\bwff1\AppData\Roaming\NuGet\NuGet.Config' is denied.` SDK wrapper exited 0 despite the nested failure. All build/test evidence | Resolution: on the implementation run, `dotnet restore Kimigayo.slnx` completed ("all projects up to date", exit 0) and both configurations built and tested without any NuGet configuration error; the earlier denial was a sandboxed-probe environment condition, not a repository or user-configuration defect. No NuGet configuration was edited. Keep using the direct xUnit runner DLL rather than `dotnet test --help` style probes. |
| G6 `INVESTIGATION_GAP` | Historical success is not current evidence; only selected hot paths/guards were inspected. R1–R28/M1, M15 | Reproduce focused baselines, expand clause coverage using Appendix A before each milestone. Unknown support needs a targeted source reproducer/code trace, not “not implemented” from a failed search. |
| G7 `INVESTIGATION_GAP` | `AggregateLayoutPool` uses int offsets/counts, depth 64 and rejects size > int.MaxValue; §21 defines broader checked layout/resource rules. R10/R14/R16/M4, M6 | Determine which are documented resource limits versus unnecessarily restrictive representations. Use checked wide arithmetic internally where needed; keep finite limits explicit and distinguish semantic, resource and representation diagnostics. Do not allocate giant fixtures to test overflow. |
| G8 `IMPLEMENTATION_MISMATCH` | STATUS historical summaries omit newer struct/borrow/module support and current CI test selection. R27/M15 | Reconcile support descriptions against current evidence in I34; no STATUS edit during planning. |
| G9 `IMPLEMENTATION_MISMATCH` — **RESOLVED 2026-09-17 (T5a)** | `struct Reading` with `Self is Copy`, `Self is Comparable` (unresolvable) and `public let value: i32` reports five diagnostics: the legitimate `UnresolvedBinding_Kd` at `Comparable` and `InvalidConstraint_Kd` at that clause, plus `InvalidConstraint_Kd` on the struct header (`ValidateConstraintEnvironments`), on the valid `Self is Copy` clause (`ValidateCopyDeclarations`) and on the field (`ValidateProperties`), because the invalid struct makes `Self` an invalid Constraint subject. `Self is Copy` alone passes and executes. R5/R27, A.3 ("preserve ordinary errors separately"), A.11 (identify the failed requirement and cause) | T5a DONE: retain all internal failures, invalid certificates and proof Error; publish a missing-name cause once and suppress only established derived diagnostics. Multiple independent facts conservatively retain the header. Unit 3 records clause-order, invalid-use, fragment, rebind/reload, full-suite, CLI and allocation evidence. No language rule changed. |

No unresolved **SPEC_CONFLICT** was established by this investigation. Contradictory status text or stale code comments are not specification conflicts. If two applicable normative rules remain contradictory after owning-section/supersession review, add a SPEC_CONFLICT row with both clauses and block only dependent work.

Decisions/proposals:

- D1: Extend Koto semantic fields and existing reusable Binding/ownership/physical plans. No new Bound Tree or mandatory intermediate compiler stage.
- D2: First complete correctness and a valid baseline sharing plan; optional specialization/tuning cannot change selected implementations, caller ABI, checks or acceptance.
- D3: Start persistent reuse with conservative module invalidation; refine only with complete dependency evidence and measured benefit. Source reparse/rebind is the correctness oracle.
- D4: Internal resource/budget defaults and float Stringify spellings are documented implementation choices where explicitly permitted. Record chosen values and measurements at M6/M9; they are not missing language rules.
- D5: Programs `milestones/Milestone7.kimi`–`Milestone9.kimi` are integration targets, not the definition of compiler completion. They depend respectively on M8; M6+M8; and M5–M8 plus general borrowed storage. Preserve their source and expected behavior.

No user decision is needed to create this plan. Before implementing excluded interfaces, request G1 and G2 as consolidated, concrete design decisions in their separate effort; silence never resolves them.

## 6. Milestones

The milestone designs below define the authorized scope; their current execution states are in Sections 2 and 7. Directory names refer to existing directories. Existing files/symbols are reused unless explicitly marked **new**. A family is complete only when its remaining finalized input range is supported and its mandatory negative boundaries still reject. Tests of known behavior are regression gates, not redundant implementation tasks.

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

Boxes track the entire I item. Verified sub-units do not close a parent while its required scope remains unfinished.

| ID | Milestone / requirements | Implementation item and required outcome |
| --- | --- | --- |
| I1 | M1 / R1–R28 | [x] DONE 2026-09-17: baseline hashes, focused/full results and G5 resolution recorded under "Verification records (M1 baseline)". |
| I2 | M1 / R1–R28 | [x] DONE 2026-09-17: Appendix A.1–A.17 mapped to executed classes/counts in Section 4; genuine gaps confirmed and assigned; passing baseline slices recorded as regression gates. |
| I3 | M2 / R1–R3, R27 | [ ] IN_PROGRESS 2026-09-17. Audit: all 250 ```kimi blocks under `spec/` parse except intentional errors, standalone Type fragments, bodyless signatures and root-level Property accessor fragments (Chapter 11 confines Properties to struct/group/rootgroup); no lexical/type-syntax defect found; the only parser gap is `$expect`/`$require` (R25, recorded under I30). `check` on programs 1–6 passes; 7–9 fail only at planned later-milestone boundaries (see V11 record). SpecTour `check` gives 544 sites, 381 `UnsupportedBinding_Kd`; non-Unsupported codes reviewed, one M2 defect found. **T3a DONE:** merged containers now retain the first declaring fragment's CodeContext (`DeclarationContainerKoto.GetOrAddChild` / `GetOrAddDeclarationContainer` accept the parsing context; `TryParseDeclarationContainer` and rootgroup parsing pass `reader.CodeContext`), so container-level Binding failures report `file:line:col` instead of `Project:@0`. **T1a DONE 2026-09-17:** unavailable modifiers now report `UnavailableFeature_Kd` and recover across all planned declaration/accessor forms without reserving ordinary Names; 40 new cases, 275 focused cases and full Debug/Release suites (7,027 each) pass. Original parser fails all 28 negative cases; V9 comparison and CLI rejection recorded in unit 2. T27a/T27b DONE: append/reload invalidation and persistent source-error state verified. T7a DONE: effect-diagnostic correction and retained rejection/no-reselection verified; effect-family implementation remains I7 and persistent semantic artifact reuse remains I29. G9 resolved under I5. I3 remains IN_PROGRESS for final R1–R3 clause-coverage closure; the concrete effect and in-memory stale-analysis reproducers are now recorded and resolved or assigned to their owning later milestones. |
| I4 | M2 / R4 | [ ] IN_PROGRESS. Selected omitted-default plans, independent declaration checking, scalar execution and selected default completion are verified through T4n-a–g. General inference/call closure, recursive-default completion, effectful/borrowed/owned defaults and pending-slot cleanup remain unfinished. Evidence and exact resumption are in Section 2 and unit 20. |
| I5 | M2 / R5, R19 | [ ] IN_PROGRESS. T5a/G9 DONE: isolated unresolved conformance causes no longer publish derived container/Copy/Property cascades; other facts/errors remain visible. Complete the remaining retained Contract/projection/certificate obligations and canonical Core identities. |
| I6 | M3 / R6–R7 | [ ] IN_PROGRESS. T4n-a–g add nonreturning-call/local divergence and operand checking; T4n-h–p add scoped/default/partial terminal joins; T4n-q retains transfer targets through completing loops. T4n-r–s preserve constituent targets across unchanged mixed joins and bare transfers. Per-target subsequent effects, general places/Loans/Origins, unequal Loan joins and retained result/storage dependencies remain. |
| I7 | M3 / R7, R20 | [ ] Compute effect-family fixed points and public guarantees; complete dependent usage checks. |
| I8 | M3 / R8–R9, R27 | [ ] IN_PROGRESS. T4c–T4m implement bounded scalar/Unit preparation and subplace inspection; T4g/T4h diagnose definite prepared Moves. T4n-a–g independently check scalar default initialization and preserve no-arrival behavior through calls/operators/state-neutral wrappers. General escaping-Borrow/capture/Copy proofs, owned default cleanup, effectful-divergence/deferred-cleanup continuation joins and refinement remain incomplete; partial scoped terminal joins and target-aware completing-loop continuations are verified by T4n-p–q. |
| I9 | M4 / R10 | [ ] Extend existing struct/base layout/construction/destruction and inherited receiver support. |
| I10 | M4 / R11 | [ ] Connect standard/custom/computed/Contract Property operations through ownership and generation. |
| I11 | M4 / R11, R24 | [ ] Implement first-access static states, effects, cycle checks and ordered shutdown. |
| I12 | M5 / R12 | [ ] Complete enum/tag/payload ABI and owned/shared Pattern lowering. |
| I13 | M6 / R5–R7, R13 | [ ] Program 8 verifies conditional stored-field acquisition and cleanup in the existing ownership CFG; broader universal operations/effects remain. |
| I14 | M6 / R13 | [ ] Close and validate explicit specialization sets; retain selected implementation at every call/reference. |
| I15 | M6 / R14 | [ ] Program 8 supplies shared storage/selection bodies, deduplicated policies, concrete entry adapters and exact fixed scratch frames; general calls/compound fields/lengths remain. |
| I16 | M6 / R14, R28 | [ ] Add deterministic finite resource limits and bounded optional optimization; measure effects. |
| I17 | M7 / R15 | [ ] Implement captures/concrete environments and receiver-specific callable analysis/lowering. |
| I18 | M7 / R7, R14–R15 | [ ] Implement function items, common Function values, erasure and indirect-call adapters. |
| I19 | M8 / R16, R19 | [ ] Program 7 subset: inferred full Slice handles, scalar reads and sequence metadata implemented. General Index/Range/Slice APIs and prerequisite Core witnesses remain open. |
| I20 | M8 / R17 | [ ] Program 7 subset: built-in ResolvedRange iteration and transfer cleanup implemented. General recognized protocol dispatch and other iterable adapters remain open. |
| I21 | M9 / R18 | [ ] Implement Array capacity/mutation/owning iteration with mandatory cost bounds. |
| I22 | M9 / R18–R19 | [ ] Implement Dictionary literal/mutation/equality/order/borrow contracts and allocation-free churn. |
| I23 | M9 / R19 | [ ] Complete comparison/Stringify witnesses and interpolation, documenting float formatting. |
| I24 | M10 / R20 | [ ] Implement object metadata/views/creation/strong ownership and refinement. |
| I25 | M10 / R20 | [ ] Implement Weak migration/upgrade/release/cyclic construction and atomic ordering proof. |
| I26 | M11 / R21, R24 | [ ] Complete specified pointers/C layout/direct foreign calls and ABI validation. |
| I27 | M12 / R22 | [ ] Implement package integrity/loading/packing/local publication/store lifecycle using fixed bytes. |
| I28 | M12 / R22, R24 | [ ] Complete common module/Library generation and native input requirement/supply validation. |
| I29 | M12 / R23, R28 | [ ] Implement verified semantic records/correspondence/invalidation with cold/warm equivalence. |
| I30 | M13 / R25 | [ ] Complete test-mode membership, all-body discovery/checking, verification semantics and plan isolation. Evidence 2026-09-17: `$expect(...)`/`$require(...)` statements do not parse (`Token $abort(Expression) is not expected at this position`, plus `Unmatched token require`), so the §17.5 operations start at the Parser stage. |
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
| T1a / R1, R27 (§2.5.1, A.3) | Positive: `struct abstract`, `let override: i32`, `func virtual(x: i32)`, `self.override`, standalone `abstract` expression followed by `let next: i32 = 2` all accepted. Negative: `abstract open struct S`, `virtual func f(...)`, `virtual init() => ()`, `override deinit => ()`, `abstract get` under a stored Property, `virtual` before a Contract requirement, and root-level `virtual func g()` each produce exactly one unavailable-feature diagnostic at the modifier, no cascade, and the following line still parses as an independent declaration | P diagnostics; DONE via unit 2: 40 new cases, baseline reproducer failures and full Debug/Release PASS | SpecReviewTest or FrontEndSyntaxTest (existing files); no new class needed |
| T3a / R3, R27 (A.1) | Two documents declare fragments of one struct; the first carries contradictory `T is i32` / `T is not i32` inputs. The container keeps its first fragment's SourceDocument and members retain their own. The independently required container diagnostic retains its original header span | DONE; source fixture updated in T5a because a missing-name cascade is no longer independently required | SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument |
| T5a / R5, R27 | Missing conformance Name, sibling Copy/Property checks, independent constraints/uses, both clause orders, fragment locations and rebind/reload | DONE; nine new cases and unit 3 evidence; diagnostic suppression never certifies a failed proof | UnresolvedConstraintBindingTest |
| T7a / R7, R27 | Projected ref/ref, uniq/uniq and uniq/ref calls retain Unknown/no-reselection and reject emission while missing effect verification reports Unsupported | DONE; three strengthened cases, CLI rejection and unit 6 evidence; public effect families remain T7/I7 | InheritedReceiverBindingTest |
| T27a / R1, R5, R23 | Append valid/invalid source through both entry points; replace with empty/nonempty snapshot; revoke retained certificates and require fresh analysis | DONE; seven new cases, full managed and native smoke evidence in unit 4 | MinimalEmissionTest, ContractBindingTest |
| T27b / R1, R23, R27 | Parse errors survive display clearing and duplicate-location reports with default/custom destinations; prior errors and warnings alone do not poison source | DONE; seven new cases, full managed and native evidence in unit 5 | MinimalEmissionTest |
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

### Verification records (M1 baseline, 2026-09-17)

Run ID `m1-8c49edb`; working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`; HEAD `8c49edb09893154d74aefd7ec5849880e5583151`, working tree clean except `PLAN.md`. Logs, xUnit XML results and fixture inventories are under `bin/plan-baseline/m1-8c49edb/` (git-ignored). Tool identity: SDK 10.0.401; `toolchain/opt.exe` and `clang.exe` report LLVM 22.1.8 (`x86_64-pc-windows-msvc`). Built compiler/test identities (SHA-256): Debug `Kimi.dll` `3e045902c8086d2b554f622445f598f27cfb96efbc7d75baeaa9b7b5219bc54c`, Debug `xUnitTest.dll` `496b5065e9ec84fd2665d285046276455d2ec9620a472c51af9373829a871bdf`, Release `Kimi.dll` `4f4cc2f9b59224685a745a1a9d580787821f6d268911599a870c8a0cafcfcb50`, Release `xUnitTest.dll` `fb8580d4d57b74ae0a90bf9ca185998c33817381ac3ade34aae3046ea8bf9958`. No NativeAOT, publish, draft edit or source change occurred.

| Verification ID | Target/configuration | Command (working directory above) | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Repository identity, SDK, stray processes | `git rev-parse HEAD`; `git status --short`; `dotnet --version`; `dotnet --list-sdks`; `tasklist` filtered for `xUnitTest`/`dotnet`/`Kimi` | PASS | HEAD above; clean tree; SDK `10.0.401` (also `10.0.100-rc.1.25451.107` installed); `global.json` selects only the MTP runner; no leftover test processes before the run | At each resumption |
| V1 | Backend/tool integrity against `backend/windows-x64/profile.json` | `sha256sum toolchain/windows_x64/kimi_backend_windows_x64_v1.lib toolchain/llvm-dlltool.exe backend/windows-x64/kernel32.def` | PASS | Archive `4ef5b90f…52d6`, dlltool `3733b6d4…fdfc`, kernel32.def `d08872d0…494e` all equal the profile's `artifactSha256`, `dlltoolSha256` and `definitionSha256` | If toolchain or profile changes |
| V2 | Restore (G5) | `dotnet restore Kimigayo.slnx` | PASS | Exit 0, all projects up to date, no NuGet.Config error | On package changes |
| V2 | Debug solution build | `dotnet build Kimigayo.slnx -c Debug --no-restore` | PASS | Exit 0, 0 warnings, 0 errors, 32.3 s; `build-debug.log` | After any source change |
| V2 | Release solution build | `dotnet build Kimigayo.slnx -c Release --no-restore` | PASS | Exit 0, 0 warnings, 0 errors, 15.5 s; `build-release.log` | After any source change |
| V3 | Focused Debug check | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.StructEmissionTest -parallelMode none -failSkips` | PASS | Total 39, Failed 0, Skipped 0; `focused-debug.log` | After struct/emission changes |
| V3 | Full Debug managed suite and fixture generation | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml bin/plan-baseline/m1-8c49edb/full-debug.xml` | PASS | Total 6,986, Errors 0, Failed 0, Skipped 0, Not Run 0, 20.7 s; regenerated all 10,215 files (2,043 `.ll`) in `bin/scalar-fixtures`; inventory `fixtures-debug-inventory.txt` (SHA-256 `99b51886…3455`) | After any source change |
| V3 | Full Release managed suite and fixture generation | `dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml bin/plan-baseline/m1-8c49edb/full-release.xml` | PASS | Total 6,986, Errors 0, Failed 0, Skipped 0, Not Run 0, 17.2 s; regenerated all 10,215 fixture files; `fixtures-release-inventory.txt` is byte-identical to the Debug inventory (same SHA-256, zero `diff` lines), so Debug/Release compilers emit identical IR and expectations at this HEAD | After any source change |
| V4 | LLVM verifier on a fresh fixture | `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/StructLocal.ll` (run after the Debug and again after the Release generation) | PASS | Exit 0 both times; `StructLocal.ll` SHA-256 `087234fb…c292` in both configurations | After emitter changes |
| V5 | Native Struct family, Debug-generated then Release-generated fixtures | `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'` | PASS | 18 fixtures; "Passed 36 native scalar executions (O0/O2)" per configuration, exit 0; `scalars-struct-debug.log`, `scalars-struct-release.log` | After emitter/backend changes or new Struct fixtures |
| V6–V9, V11 | Backend build, CLI, reuse, benchmarks, programs 1–6 | Not executed in this unit | NOT_RUN | Outside M1's required set (V1–V3 plus selected V4–V5); historical STATUS results remain historical | Run at the owning milestone or on a justified concern |
| V10 | Language runner | Unimplemented public interface | BLOCKED | G2 unchanged | I30, then G2/I31 |
| V12 | Plan-only diff gate | `git diff --check`; `git status --short` | PASS | Only `PLAN.md` differs; whitespace check clean | After each plan edit |

### Verification records (M2 unit 1, 2026-09-17)

Change set: `Kimi/Compiler/Parsing/Koto/Declarations/DeclarationContainers/DeclarationContainerKoto.cs`, `GroupKoto.cs`, `xUnitTest/Tests/SourceDocumentAndDiagnosticTest.cs`, `STATUS.md`, `PLAN.md` on top of HEAD `8c49edb` (uncommitted). Post-change identities (SHA-256): Debug `Kimi.dll` `1ec4b8a659032663240e4e7f5c9cb25245bc46a29ee8bd38862f223f39f85a48`, Debug `xUnitTest.dll` `1b6e99736b801e289a0152f3e3c418fe93adef61fd79c1f51c7841aa214f42ac`, Release `Kimi.dll` `f9ae59c289d6ab1214e5fb0f63d302d9ea76a214a888bae5bb99d428fcf3e30f`, Release `xUnitTest.dll` `f6bdf74632d5c0d0b8c15332de06469abb71babfacf00a702966f6438ff335af`. Logs: `bin/plan-baseline/m1-8c49edb/*-fix.log`, `*-fix.xml`, `check-milestone1..9.log`, `check-spectour.log`, `spec-probe/`.

| Verification ID | Target/configuration | Command (working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`) | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Spec example parse probe (I3 audit) | Scratch console app (copy in `spec-probe/`) parsing each ```kimi block of `spec/*.md` through `CreateCodeContext().Parse(RootKoto, source)` after `Prepare("x86_64-pc-windows-msvc")` | PASS (audit) | 250 blocks, 39 with errors, all intentional/fragments; targeted reproducers confirmed parenthesized reference Types, nested `ref/uniq/unsafe` layers and struct Property accessors parse; `$expect`/`$require` do not (I30) | After grammar or spec example changes |
| V11 (check only) | `dotnet Kimi/bin/Release/net10.0/Kimi.dll check milestones/MilestoneN.kimi`, N = 1..9, pre-change Release compiler | PASS 1–6; FAIL 7–9 as expected by D5 | 1–6 exit 0 with no diagnostics. 7: 22 sites (`UnsupportedBinding_Kd` at `for row in matrix.indices`, M8). 8: 22 sites (`UnresolvedBinding_Kd` at `Toolkit.Selection.choose(...)` with generic `Box<string>.init`, M6). 9: 75 sites (`MissingOriginBinding_Kd` at `return self.value@ref/T` inside generic `Batch<T>.view`, R7/M3–M6; `InvalidCaptureBinding_Kd`, `NotCallable_Kd` for the callback, M7; iteration, M8) | When M6–M8 land; native V11 scripts for 1–6 remain NOT_RUN this execution |
| V7 (check only) | `dotnet Kimi/bin/Release/net10.0/Kimi.dll check examples/SpecTour/SpecTour.kimiproj` | FAIL as expected (beyond support) | 544 sites: `UnsupportedBinding_Kd` 381, `UnresolvedBinding_Kd` 105, `InvalidConstraint_Kd` 27, `NotCallable_Kd` 12, `InvalidCaptureBinding_Kd` 7, `ControlFlow_Kd` 4, `UnprovenConstraint_Kd` 3, `InvalidAssociatedType_Kd` 3, `TypeMismatch_Kd` 1, `MissingOriginBinding_Kd` 1; per file Sequences 184, Callables 96, ControlFlow 76, Lifetimes 64, Generics 46, Objects 33, Native 22, Basics 19. Basics.kimi review produced G9/T3a; other codes map to M6–M11 boundaries | Rerun as milestones land to shrink the inventory |
| V3 | Reproducer before fix | `git stash push` of the two parser files, Release build, `xUnitTest.dll -method XunitTest.SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument`, then `git stash pop` | PASS (expected FAIL observed) | `Assert.Same() Failure: Values are not the same instance`; sources restored, `git diff --stat` unchanged | None |
| V2 | Debug and Release solution builds with T3a | `dotnet build Kimigayo.slnx -c Debug --no-restore`; `-c Release` | PASS | 0 warnings, 0 errors each; `build-release-fix.log` | After further source changes |
| V3 | Focused Debug classes | `xUnitTest.dll -class` SourceDocumentAndDiagnosticTest, ContainerFragmentBindingTest, KotoHierarchyTest, ParserRegressionTest, KotonohaSerializationTest, ModuleBindingTest, ParserOptimizationTest `-parallelMode none -failSkips` | PASS | Total 340, Failed 0, Skipped 0 | After parser/container changes |
| V3 | Full Debug suite with T3a | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml .../full-debug-fix.xml` | PASS | Total 6,987, Failed 0, Skipped 0, 20.6 s | After further source changes |
| V3 | Full Release suite with T3a | `dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml .../full-release-fix.xml` | PASS | Total 6,987, Failed 0, Skipped 0, 15.6 s | After further source changes |
| V4–V5 | Native fixtures after T3a | Not rerun | NOT_RUN (justified) | `fixtures-release-fix-inventory.txt` is byte-identical to the M1 Debug inventory (zero `diff` lines), so the M1 V4/V5 PASS records still describe the generated IR | Rerun when emission changes |
| V7 | CLI location after fix | `dotnet Kimi/bin/Debug/net10.0/Kimi.dll check <scratch>/repro/CopyComparable.kimi` | PASS | Container diagnostic now `CopyComparable.kimi:1:1` (was `CopyComparable:@0`); remaining four diagnostics unchanged (G9) | With G9 |
| V11 | Native program 6 with T3a compiler | `./backend/windows-x64/test-milestone6.ps1 -ToolchainRoot ./toolchain -Configuration Debug`, then `Release` | PASS | 63 checks each; run IDs Debug `99b46dee8dbd4fbeba7dcda080bc766c`, Release `f9462400eb0f4ee488fab5ab0097c9b8` under `bin/milestone6/<configuration>/`; logs `milestone6-{debug,release}-fix.log` | After emitter/runtime changes |
| V11 | Native programs 1–5 with T3a compiler | `./backend/windows-x64/test-milestoneN.ps1 -ToolchainRoot ./toolchain -Configuration Debug` then `Release`, N = 1..5 | PASS: 25, 21, 37, 33 and 47 checks in Debug and again in Release; run IDs recorded in each verification.json under bin/milestoneN/<configuration>/ | Logs `milestoneN-{debug,release}-fix.log` under `bin/plan-baseline/m1-8c49edb/` | After emitter/runtime changes |
| V12 | Diff gate | `git diff --check`; `git status --short` | PASS | Whitespace clean; five modified tracked files plus the untracked user draft file, which is untouched | After each edit |

### Verification records (M2 unit 2 / T1a, execution started 2026-09-17 01:42:22 JST)

Base: `826f6077a2654a6d2be8aa3865b655224bf615ae`. Working directory: `C:\Users\bwff1\repos\Kimigayo`. Evidence: `bin/plan-execution/20260917-014222/`. The prior run's `bin/plan-baseline/` logs are absent from this checkout; their recorded results remain historical, not newly inspected artifacts. No draft file was read or edited.

| Verification ID | Target/configuration and command | Result | Evidence / remaining check |
| --- | --- | --- | --- |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore` | PASS | 0 warnings/errors; `t1a-build-debug.log`. Initial member-order warning was corrected before this build. |
| V3 | Debug focused: FrontEndSyntaxTest, ParserRegressionTest, SourceDocumentAndDiagnosticTest, ContainerFragmentBindingTest, ParserOptimizationTest, KotonohaSerializationTest; direct DLL runner with `-parallelMode none -failSkips -result-xml` | PASS | 275 cases, zero failures/skips; `t1a-focused.log/xml`. |
| V3 | Original HEAD parser with new negative tests, Release | PASS (expected FAIL observed) | Temporarily restored the three original parser/reader/container files, built Release, ran `-method XunitTest.FrontEndSyntaxTest.UnavailableModifiersReportOneCauseAndRecover`: all 28 failed, exit 1, for missing dedicated diagnostic/recovery; `t1a-baseline-repro.log/xml`. Current files restored in `finally`. |
| V2–V3 | Current Release `dotnet build Kimigayo.slnx -c Release --no-restore -t:Rebuild`; full Debug/Release DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | Release build: 0 warnings/errors. Both full suites: 7,027 cases, zero failures/skips (`t1a-full-debug.log/xml`, `t1a-full-release.log/xml`). Forced rebuild avoids timestamp reuse after restoring baseline source backups. Compiler/test DLL hashes: `t1a-artifact-hashes.txt`. |
| V7 | Release CLI `check bin/plan-execution/20260917-014222/unavailable.kimi`, with a valid independent startup expression | PASS (required rejection) | Exit 1; exactly `UnavailableFeature_Kd` at `unavailable.kimi:1:1`, highlighting `abstract`; `t1a-cli-check.log`. |
| V9 | Existing FrontEndBenchmark common workload; local harness, Release, tiering disabled, 2,000 warmups then seven samples of 5,000 parses | PASS (measurement) | Median baseline 5,995.7 ns/parse, current 6,206.7 ns/parse (+3.5% in this local sample); all seven allocation samples identical (8,896–9,000.9 B/parse, including buffer growth). No speedup or universal allocation claim. `t1a-perf-common-{baseline,current}.log`, baseline compiler hash and harness under `perf/`. |
| V4–V6 / V11 | Native/LLVM checks | NOT_RUN | This unit changes rejection/recovery and has full parser/emission managed regression coverage; no lowering/runtime/ABI changes or new executable feature. Historical native results remain historical. |
| V12 | `git diff --check` and source review | PASS | Only planned parser/reader/diagnostic/tests and documentation changes; no draft changes. |

Environment notes: the scratch harness restore hit the existing sandbox denial reading user NuGet.Config. An approved `dotnet restore` using the harness's empty package-feed configuration succeeded; the harness references built local assemblies and adds no package dependency. NativeAOT was not run.

Out-of-scope finding from V9: `FrontEndBenchmark.ExtendedSource` fails `Setup()` on the original parser (`Benchmark source must parse without diagnostics`). The common workload was measured with the same existing benchmark method; the invalid extended fixture was not edited. Its failed run is retained as `t1a-perf-baseline.log`; Ctrl+C ended the lingering failed process. Repairing this pre-existing benchmark input is separate from T1a.

### Verification records (M2 unit 3 / T5a, 2026-09-17)

Same execution root and baseline as unit 2, with T1a retained. Source changes: `Binding.Diagnostics.cs` (new failure-only cause storage), four constraint/Property validation call sites, reset/report hooks in `Binding.cs`, and the two existing test classes. No proof result or internal issue is removed; only publication of established derived diagnostics is suppressed. Other environment facts conservatively retain their header diagnostics, since they may contain independent contradictions.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: Debug UnresolvedConstraintBindingTest + SourceDocumentAndDiagnosticTest | PASS (expected FAIL observed) | 47 tests, three intended failures: both clause orders reported five diagnostics and the interaction case retained the redundant header. The revised T3a contradictory-premise fixture passed. `t5a-baseline.log/xml`. |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore`; Release equivalent | PASS | Zero warnings/errors in each; `t5a-build-{debug,release}.log`. |
| V3 | Focused Debug: UnresolvedConstraintBindingTest, SourceDocumentAndDiagnosticTest, ConstraintBindingTest, ConstraintContextBindingTest, LateConstraintEnvironmentBindingTest, ContractBindingTest | PASS | 199 cases, zero failures/skips; `t5a-focused.log/xml`. Nine new cases cover diagnostic scope, invalid certificates/uses, independent input contradictions, fragment locations, multiple Names and rebind/reload. |
| V3 | Full Debug and Release DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | 7,036 cases each, zero failures/skips; `t5a-full-{debug,release}.log/xml`; hashes in `t5a-artifact-hashes.txt`. |
| V7 | Release `check .../t5a-unresolved.kimi` with an independent valid startup expression | PASS (required rejection) | Exit 1, one `UnresolvedBinding_Kd` at `Comparable`, line 3 column 13; `t5a-cli-check.log`. |
| V9 | `WarmMissingConformanceBindingReusesDiagnosticCauseStorage` and existing warm Binding tests | PASS | Zero measured bytes after warmup in Debug/Release; lazy cause storage is cleared/reused on rebind. No throughput claim. |
| V4–V6 / V11 | Native/LLVM checks | NOT_RUN | Reporting-only behavior; semantic states are retained and managed emission regression passes. |
| V12 | Diff review and `git diff --check` | PASS | Intended source/tests/docs only; no draft edits. |

### Verification records (M2 unit 4 / T27a, 2026-09-17)

Changes: `Compilation.BeginSourceParsing` and snapshot reload invalidate existing analysis without constructing lazy Binding/ownership services; `Binding.Invalidate` revokes summaries, startup, conformance/Property certificates and cached capability results. Existing ownership invalidation revokes body verification. Rebinding reuses the retained storage. No persistent-cache behavior is claimed.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before source change: Debug MinimalEmissionTest | PASS (expected FAIL observed) | Six new append/reload cases all wrote stale IR; 29 total, six intended failures. `t27a-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t27a-build-{debug,release}.log`. |
| V3 | Focused Debug: MinimalEmissionTest, ContractBindingTest, UnresolvedConstraintBindingTest, KotonohaSerializationTest, ModuleBindingTest | PASS | 351 cases, zero failures/skips; seven new cases include retained certificate revocation and successful recertification. `t27a-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,043 tests each, zero failures/skips; `t27a-full-{debug,release}.log/xml`; compiler/test identities in `t27a-artifact-hashes.txt`. |
| V4–V5 / V7 / V11 | `./backend/windows-x64/test-milestone1.ps1 -ToolchainRoot ./toolchain -Configuration Debug`, then Release | PASS | 25 checks each, including newly generated LLVM/O0/O2/native/CLI paths; logs `t27a-native-{debug,release}.log`, run manifests under `bin/milestone1/<configuration>/`. Initial sandboxed `opt.exe` version probe denied; approved native execution succeeded. NativeAOT NOT_RUN. |
| V12 | `git diff --check` and focused diff review | PASS | Source/reset hooks, seven cases and documentation; no draft changes. |

### Verification records (M2 unit 5 / T27b, 2026-09-17)

Both parsing entry points capture a monotonic Error report version before lexing/parsing and latch newly reported source errors on the owning Kotonoha. The version counts attempted reports before location deduplication and survives diagnostic clearing. Existing errors and warnings alone do not poison a valid parse. This adds one integer field per diagnostic collection and no per-report allocation.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: Debug MinimalEmissionTest | PASS (expected FAIL observed) | Four direct/custom parsing cases emitted IR after source errors were cleared; 35 tests, four intended failures. `t27b-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t27b-build-{debug,release}.log`. |
| V3 | Focused source/diagnostic/emission tests | PASS | 204 cases, including seven new cases for custom destinations, duplicate locations, existing diagnostics and warning-only parsing; `t27b-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,050 cases each, zero failures/skips; `t27b-full-{debug,release}.log/xml`; identities in `t27b-artifact-hashes.txt`. |
| V4–V5 / V7 / V11 | Existing Milestone 1 script, `-ToolchainRoot ./toolchain -Configuration Debug`, then Release | PASS | 25 checks each; `t27b-native-{debug,release}.log`. Run manifests: Debug `339177f75de748149c5774d70680f746`, Release `171779dbe82741c38e9846e77754e0d1` under `bin/milestone1/<configuration>/`. NativeAOT NOT_RUN. |
| V12 | Diff review and `git diff --check` | PASS | No draft or specification changes. Existing normative rules already require current-source validity. |

### Verification records (M2 unit 6 / T7a, 2026-09-17)

The A.3 audit inspected `Binding.ArgumentOperations.ProjectedReceiverProof`, method selection, Property accesses and inherited Contract witnesses against §12.4.4. The implementation has only internal Error/Unknown receiver proof, with no effect-family fixed point or public Proven/NotProven summary. Standard storage projections are independent Place operations and remain supported. Existing tests already retain base paths, Origins, no-reselection behavior and unverified witnesses; adding duplicate tests would not supply the missing verifier.

T7a corrects the method-call diagnostic for pending effect verification from `UnprovenConstraint_Kd` to `UnsupportedBinding_Kd`, matching the existing custom-accessor boundary. The prior three adaptation-case assertions were inconsistent with §12.4.4.1's prohibition on masking unimplemented verification as a completed negative guarantee; they now also assert no Unproven diagnostic and rejected emission. Internal Unknown and selected operations are unchanged. Full effect-family computation and dependent witness/use completion remain I7, including ordinary generic bodies and every explicit specialization; artifact summary validation remains I29. These are implementation work, not specification gaps or permission requests.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: `-method XunitTest.InheritedReceiverBindingTest.ProjectedCallsRetainTheirPlanButRequireEffectProof` | PASS (expected FAIL observed) | All three adaptation cases failed for the intended Unproven-vs-Unsupported mismatch; `t7a-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t7a-build-{debug,release}.log`. |
| V3 | Debug InheritedReceiverBindingTest, ReceiverShorthandTest, DefaultBindingTest, ContractBindingTest and MinimalEmissionTest | PASS | 204 cases, zero failures/skips, including direct receivers, standard storage, inherited witness rejection and no overload reselection; `t7a-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,050 cases each, zero failures/skips; `t7a-full-{debug,release}.log/xml`; final identities in `t7a-artifact-hashes.txt`. |
| V7 | Release CLI `check .../t7a-projected.kimi` | PASS (required rejection) | Exit 1; one `UnsupportedBinding_Kd` at the selected call, line 4 column 30; `t7a-cli-check.log`. |
| V4–V5 / V11 | Additional native run after T7a | NOT_RUN | Only a rejected projected-call diagnostic changed; T27b's 25-check native runs per configuration cover the unchanged valid scalar path. No effect proof or generation guard was relaxed. |
| V9 | Final common FrontEndBenchmark, same harness/settings as T1a, no concurrent build/test | PASS (measurement) | Median 6,145.3 ns/parse versus original 5,995.7 (+2.5% local sample); all seven allocation samples remain identical. `final-perf-common-isolated.log`. An earlier final sample overlapped the full suite and is retained in `final-perf-common.log` but excluded from timing comparison. No speedup claim. |
| V12 | Complete source/test diff review and `git diff --check` | PASS | Five coherent fixes; 63 new cases overall and three strengthened existing effect-boundary cases. New failure-only cause storage is the only new source file. |

### I4 next-unit audit checkpoint (2026-09-17, about 57 minutes elapsed)

`BoundCall.Set` retains explicit argument operations, receiver operation, parameter mappings, Types and Origins, but no ordered omitted-default operation list. `EvaluateCandidate` correctly permits omission only for `IsOptional` and counts defaults after mapping; default expressions are type-checked in the declaration's scope in `Binding.Expressions`. `OwnershipAnalysis` rejects every optional/default parameter before body analysis. Existing DefaultBindingTest (14 cases) covers declaration lookup, invalid Types/Names, rebind/reload and warm allocation, but does not prove pending-slot ownership semantics.

Two current Release CLI probes, each with an independent Unit startup, confirm the boundary: `func f(x: i32, y?: i32 = x) => ()` and `func f(x: string, y?: string = x) => ()` both exit 1 with `UnsupportedOwnership_Kd` on the default. The first is valid per §7.2; the second must eventually fail for moving a preceding prepared argument. Inputs and logs are retained under `default-audit/`. This is a verified support-boundary audit, not completed default implementation or evidence that the invalid program was semantically classified correctly.

Next implementation: extend the winner's reusable call-plan storage with omitted defaults in parameter order, retaining declaration-bound expression identity, substituted Types/Origins and prepared preceding parameter slots. Keep explicit receiver/argument acquisition in source order and prohibit default-based inference/reselection. First tests should inspect named/positional/receiver interleaving, chained defaults and rebind/reload storage reuse. Follow with I8's declaration-time checking of every default, no Move/write/new escaping Borrow from preceding slots, normal-transfer reverse cleanup and Abort behavior before removing the existing ownership/emission guard. This implementation was not started in the current timed execution.

### Verification records (M2 unit 7 / T4a, execution started 2026-09-17 02:42:36 JST)

Fresh inspection: HEAD `826f6077a2654a6d2be8aa3865b655224bf615ae` plus prior uncommitted implementation; all 22 prior source/test hashes matched before editing. Evidence root: `bin/plan-execution/20260917-024236/`. T4a adds selected omitted-default metadata to existing reusable BoundCall storage; it changes no ownership or emission guard.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Existing DefaultBindingTest, InheritedReceiverBindingTest and FunctionParameterIdentityBindingTest, Debug | PASS | 93 baseline cases, zero failures/skips; `t4a-baseline.log/xml`. Inspection established absent default-plan storage; this is a new contract representation, not a changed previous behavior assertion. |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore`, Release equivalent | PASS | Zero warnings/errors; `t4a-build-{debug,release}.log`. |
| V3 | Same focused classes with twelve new cases | PASS | 105 cases, zero failures/skips; `t4a-focused.log/xml`. |
| V3 | Debug/Release direct DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,062 each, zero failures/skips; `t4a-full-{debug,release}.log/xml`. |
| V9 | WarmOmittedDefaultPlansReuseTheirStorage plus prior warm Binding tests | PASS | Zero measured allocation after warmup in both configurations. One empty-default array reference per call and reusable failure-safe scratch; omitted defaults allocate their array on first use. No throughput claim. |
| V4–V6 / V11 | Additional native checks | NOT_RUN | This metadata-only unit retains all prior runtime guards; managed fixture generation and unchanged explicit-call regression pass. No new executable feature or ABI change. |
| V12 | Source/test review and `git diff --check` | PASS | Only Binding.Calls.cs, DefaultBindingTest.cs and English documentation changed in this unit. SPEC already owns these rules; drafts untouched. |

### Verification records (M2 unit 8 / T4b, 2026-09-17)

Same fresh baseline/evidence root as T4a. Changed ControlFlowAnalysis's declaration traversal and ControlFlowContext's value/transfer rules; no runtime default guard changed. Defaults are visited independently even when there is no body, with their parameter expected Type. Transfer lookup stops when crossing a default root. Their flow does not contribute to the callee body.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production fix: Debug DefaultBindingTest | PASS (expected FAIL observed) | Ten intended failures among 36 tests: missing flow, discarded values and escaping lexical targets; `t4b-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors after member-order correction; `t4b-build-{debug,release}.log`. |
| V3 | DefaultBindingTest, CurrentControlFlowTest and ControlFlowConformanceTest, Debug | PASS | 222 cases including twelve new regressions; `t4b-focused.log/xml`. |
| V3 | Full Debug/Release direct DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | 7,074 each, zero failures/skips; `t4b-full-{debug,release}.log/xml`. |
| V9 | WarmDefaultFlowAnalysisReusesStorage | PASS | Zero measured bytes after warmup in Debug/Release. |
| V4–V6 / V11 | Native default execution | NOT_RUN | Still rejected by ownership; T4c owns the first executable default slice. Existing managed flow/emission regressions pass. |
| V12 | Source review / `git diff --check` | PASS | Existing specification already requires independent defaults, value contexts and contained transfers. No specification or draft edits. |

### Execution unit 9 — T4c scalar default acquisition (DONE)

At about 33 minutes elapsed, scalar defaults are acquired after explicit arguments in declaration order, using copies in prepared slots. Literal, preceding scalar parameter, arithmetic/bit/comparison/unary and short-circuit expressions use existing ownership/SSA and checked arithmetic lowering. Generic, owned/borrowed and effectful default bodies retain rejection. Initial emission reproducers failed before implementation (10 of 13). A short-circuit case exposed branch-local reads overwriting the acquired SSA value; these reads now preserve the prepared value. Malformed tests also exposed missing default-order validation, now checked. The new temporary/result read path rejects aliases to another acquired place. Required-initializer/unused scalar-default expectations were updated because this unit intentionally enables those declarations. Receiver, nested, named, snapshot, repeated, chained and noncompleting cases are covered. Initial native Debug verification passed 24 O0/O2 executions before the receiver case; final native and full Debug/Release verification remain pending. Native LLVM probing was sandbox-denied and passed under authorized escalation. No NativeAOT run.

T4c completed at 03:22 JST, about 40 minutes elapsed. Final serial full suites pass 7,095 cases each, zero failures/skips. Both builds have zero warnings/errors. The 21 new cases include four malformed-plan/read checks with recovery; declaration guards remain for unsupported forms even when supplied. A proposed 128-bit division guard was unnecessary: the probe already fails Binding, so no duplicate guard or misleading ownership-stage test remains. An intermediate pair of full runners overlapped; their failed probe results were superseded by the final serial runs. No blanket I4/I8 completion is claimed.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V2/V3 | Debug/Release clean builds and serial full suites | PASS | `t4c-build-{debug,release}.log`, `t4c-full-{debug,release}.log/xml`; 7,095 each. |
| V4–V6 | Thirteen default fixtures per configuration, O0/O2 | PASS | `t4c-native-{debug,release}.log`; 26 executions each, including checked overflow, nontermination, snapshots, receiver placement and short-circuiting. |
| V4–V6 | Existing Function fixtures generated by Release | PASS | `t4c-native-functions-release.log`; 56 O0/O2 executions. |
| V4–V7 | Milestone 1 Debug/Release | PASS | `t4c-milestone1-{debug,release}.log`; 25 checks each, report paths in logs. |
| V9 | WarmDefaultOwnershipAndEmissionReuseStorage | PASS | Zero bytes after warmup in both full suites. Retained integer slot buffer; no per-call managed allocation after capacity stabilizes. |
| V12 | Diff/specification review and evidence identities | PASS | `git diff --check`; `t4c-source-hashes.txt`, `t4c-artifact-hashes.txt`, `t4c-fixture-hashes.txt`. Existing §7.2–3 rules unchanged; draft untouched; NativeAOT NOT_RUN. |

### Execution unit 10 — T4d scalar conversions in defaults (DONE)

Started 03:23 JST, about 41 minutes elapsed, after T4c verification and documentation. §7.2 admits scalar Copies; §13.5.4 defines existing numeric conversion checks and rounding. The default whitelist currently rejects ConversionKoto even when Binding has a supported scalar conversion plan. Reuse that plan and existing ownership/conversion lowering, retaining rejection for Borrow/Reborrow and nonscalar results. Required evidence: original rejection cases, narrowing/widening/identity/literal/float and supplied-default behavior, Abort source locations, full Debug/Release regressions and O0/O2 native execution. No new conversion semantics or I4/I8 completion claim.

Completed 03:27 JST, about 45 minutes elapsed. The nine new original-code cases all failed at the unsupported-default boundary, then passed after admitting existing Identity/Literal/Integer/Floating/Numeric plans with scalar operands. No Borrow/Reborrow plan is admitted. Existing conversion lowering and its validation were unchanged. Warm ownership/emission coverage now includes a widening/arithmetic/narrowing chain.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/fixed ScalarDefaultEmissionTest | PASS | `t4d-baseline.log/xml`: nine expected failures among 30; `t4d-focused.log/xml`: 30 pass. |
| V2/V3 | Debug/Release builds and serial full suites | PASS | Zero warnings/errors; 7,104 cases each, zero failures/skips; `t4d-build-{debug,release}.log`, `t4d-full-{debug,release}.log/xml`. |
| V4–V6 | Nine conversion-default fixtures per configuration | PASS | 18 O0/O2 executions each; `t4d-native-{debug,release}.log`. Widen/narrow/identity/literal/float truncation/ties-to-even/signed-zero/supplied/Abort behavior. |
| V9/V12 | Warm storage, source/spec review and identities | PASS | Zero measured warm allocation; `git diff --check`; `t4d-{source,artifact,fixture}-hashes.txt`. Existing normative conversion rules unchanged. NativeAOT NOT_RUN. |

### Execution unit 11 — T4e scalar selection defaults (DONE)

Started 03:27 JST, about 45 minutes elapsed. After T4a–T4d, extend the same scalar default subset to value-producing if/else expressions with scalar-only conditions and single-expression arms. Check every arm, including supplied/unused defaults and runtime-unreached branches. Reuse existing result joins and cleanup; no effectful arm, external transfer, generic or borrowed default support is inferred. Required checks: baseline rejection, selected-arm overflow avoidance, repeated/nested/default-chain selection, negative hidden effects, full Debug/Release suites and O0/O2 execution.

Completed 03:32 JST, about 50 minutes elapsed. Five original selection emission cases failed before support; the hidden-effect rejection already passed. The initial nested fixture needed parentheses for the existing clause-grouping syntax (§2.2), then exercised the intended nested result joins. The former scalar-if unsupported expectation now checks unsupported do instead; DefaultBindingTest correctly admits the now-supported supplied scalar selection. Every arm is checked by indexed traversal with no iterator allocation.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/fixed default tests | PASS | `t4e-baseline.log/xml`: five failures among 36; `t4e-focused.log/xml`: 74 pass. |
| V2/V3 | Debug/Release builds and serial full suites | PASS | Zero warnings/errors; 7,110 each, zero failures/skips; `t4e-build-{debug,release}.log`, `t4e-full-{debug,release}.log/xml`. |
| V4–V6 | Five selection-default fixtures per configuration | PASS | Ten O0/O2 executions each; `t4e-native-{debug,release}.log`; selected-arm overflow avoidance, nesting/chaining, repetition and conversions. |
| V9/V12 | Existing warm default regression and source review | PASS | Existing conversion-default zero-allocation test passes in both suites; no separate selection throughput claim. `git diff --check`; `t4e-{source,artifact,fixture}-hashes.txt`. No specification change or NativeAOT. |

### Execution unit 12 — T4f scalar do defaults (DONE)

Started 03:33 JST, about 50 minutes elapsed. §14.2 single-expression do bodies already have flow/result plans, including T4b's default value context. Admit only a do whose one trailing expression belongs to the supported scalar subset; retain unsupported jumps, loops, declarations and effectful bodies. Required checks: original-code failure, nested/chained/default snapshot cases, warm reuse including do/selection/conversion, full Debug/Release suites and O0/O2 execution. This does not complete general default transfers or cleanup.

Completed at 03:41 JST, about 58 minutes elapsed. Five original do emission cases failed, then passed using the existing scoped-result path. A sixth test verifies rebind/reload invalidation and identical regenerated IR. The existing allocation test now combines do, if and conversion defaults. Unsupported expectations were advanced from simple do to loops; internal jump/loop support remains open. No source implementation work is left unverified in this unit.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/final focused defaults | PASS | `t4f-baseline.log/xml`: five failures among 41; final `t4f-focused.log/xml`: 80 pass. |
| V2/V3 | Final Debug/Release builds and serial full suites | PASS | Zero warnings/errors, 7,116 each, zero failures/skips; `t4f-build-{debug,release}.log`, `t4f-full-{debug,release}.log/xml`. |
| V4–V6 | Five new do fixtures; final replay of every default fixture | PASS | Initial `t4f-native-debug.log`: ten executions. `final-defaults-{debug,release}.log`: 64 O0/O2 executions each across 32 fixtures, including all previous default units. |
| V4–V7 | Final Milestone 1 Debug/Release | PASS | `final-milestone1-{debug,release}.log`: 25 checks each; machine-readable reports and exact hashes at their logged paths. |
| V8/V9 | Default plan rebind/reload and warmed compound default reuse | PASS | Rebind/reload rejects premature emission, then reproduces identical IR. Warm do/selection/conversion ownership and IR writing allocate zero measured bytes in both suites. |
| V12 | Final source/doc review, identities and scope | PASS | `git diff --check`; `t4f-{source,artifact}-hashes.txt`, final source/fixture/document manifest. SPEC already defines these rules, so no normative change; draft untouched and NativeAOT NOT_RUN. |

### Execution unit 13 — T4g definite default Move diagnostics (DONE)

Started 03:43:10 JST with a fresh matching prior manifest. §7.2 requires declaration-time rejection even for fully supplied defaults. Nine original tests failed because direct non-Copy acquisitions produced only Unsupported or, for bodyless requirements, no ownership issue. A reusable declaration visitor now recognizes acquired preceding parameters in result/initializer/transfer/committed-call contexts and reports `DefaultArgumentMove_Kd` for Refuted Copy proof. Scalar operators/shared inspection remain reads; generic Unknown proof, field paths, mutation, escaping Loans and captures remain separate unfinished cases. The visitor drops its declaration reference after checking and does not certify runtime support. Eighteen new cases and 98 focused cases pass, including diagnostic severity/span, owned receiver/identity conversion, shared-origin Copy and rebind/reload. The shared-reference control originally used independent elided Origins and was corrected to a common declared Origin. Final builds/full suites remain pending; execution is still guarded for unsupported forms.

Completed 03:54 JST, about 11m30s elapsed. Final clean builds have zero warnings/errors; both serial full suites pass 7,134 cases without failures/skips. Evidence in the current root: `t4g-build-{debug,release}.log`, `t4g-full-{debug,release}.log/xml`, `t4g-focused.log/xml` (98), and `t4g-baseline.log/xml` (nine intended failures among 13). Both `check default-move.kimi` CLI probes exit 1 with `DefaultArgumentMove_Kd` at the declaration (`t4g-cli-{debug,release}.log`). Existing warmed default Binding/flow/ownership/emission tests pass with zero allocation. Source/artifact hashes are `t4g-{source,artifact}-hashes.txt`; `git diff --check` passes. Native execution NOT_RUN for this diagnostic-only unit: no execution boundary was widened; existing managed generation regressions pass. NativeAOT NOT_RUN. A member-order warning was fixed before final verification.

### Execution unit 14 — T4h prepared subplace/aggregate acquisitions (DONE)

Started 03:54 JST, about 11m30s elapsed. Extend the definite-Move declaration check through committed owned field/tuple/array paths and aggregate value construction, preserving Copy fields and shared inspection. This closes missed acquisition contexts rather than enabling default execution. Check bound types/storage identities; do not classify arbitrary property getters or borrowed referents as owned subplaces. Required verification: original-code failures at the ownership stage, positive Copy/inspection controls, full Debug/Release and diagnostic/source review.

Checkpoint at about 16 minutes elapsed: committed owned storage paths now propagate prepared-root identity, while aggregate and enum payload acquisitions and assignment RHS use value acquisition context. Borrowed referents and arbitrary property getters are not treated as owned paths. Fifteen new cases and 189 focused cases pass. Baseline had seven intended ownership failures plus one early index-Type mismatch; changing that index from i32 to the specified isize reaches the intended check. Copy fields, shared inspection, Copy payloads and the borrowed-referent boundary remain covered. Clean builds/full suites are pending.

Completed 04:01 JST, about 18m20s elapsed. Both builds have zero warnings/errors and serial full suites pass 7,149 cases each without failures/skips. Evidence: `t4h-build-{debug,release}.log`, `t4h-full-{debug,release}.log/xml`, `t4h-focused.log/xml` (189). Existing warmed default tests pass. Native execution NOT_RUN for this diagnostic-only change; no execution boundary widened. `git diff --check` passes; source/artifact identities are in `t4h-{source,artifact}-hashes.txt`. No SPEC change, draft edit or NativeAOT.

### Execution unit 15 — T4i contained scalar default transfers (DONE)

Started 04:04 JST, about 21 minutes elapsed. Capture acquisition currently fails earlier Binding and has no committed capture plan; implementing it belongs with M7 rather than inventing a second lookup here. Unknown generic Copy proof remains an explicit unfinished definition-checking obligation. The next dependency-ready I4/I8 execution slice reuses T4b's contained targets and established loop/do result joins: scalar defaults with internal exit/yield/continue and loop expressions. Keep effectful calls, local declarations and general owned/borrowed cleanup unsupported. Required checks: baseline failures, snapshots/chaining/repetition, noncompletion skipping later defaults/callee, Abort source location, full Debug/Release, native O0/O2 and warm reuse.

Checkpoint at about 23 minutes elapsed: eleven new execution cases now pass; 158 focused cases pass including existing result lowering. A yield-to-do reproducer was corrected to the specified yield-to-selection target (the original also had an earlier flow error). Prior supplied-loop rejection expectations now test a loop containing an unsupported call. Warm reuse and rebind/reload tests now include an internal loop transfer. Every transfer's resolved target must be inside its owning default expression; Never is admitted only through the checked expression forms. Final full/native verification remains pending.

Completed 04:08 JST, about 25 minutes elapsed. Both builds have zero warnings/errors; serial full suites pass 7,160 cases each without failures/skips. Eleven new fixtures pass 22 native O0/O2 executions per configuration. Evidence: `t4i-build-{debug,release}.log`, `t4i-full-{debug,release}.log/xml`, `t4i-native-{debug,release}.log`, `t4i-focused.log/xml` (158), baseline (11 failures among 53). Warm compound default analysis/emission still measures zero allocation; rebind/reload reconstruct identical IR. `git diff --check` passes; source/artifact/fixture hashes retained. NativeAOT NOT_RUN; specification unchanged.

### Execution unit 16 — T4j immutable scalar default locals (DONE)

Started 04:10 JST, about 27 minutes elapsed. Extend checked default bodies to sequential expressions and initialized immutable scalar let bindings. Preserve their separate local storage and restrict every local reference to declarations inside the same default. Mutable/uninitialized locals and effectful/owned bodies remain unsupported until declaration-time ownership checking can certify them independently of call omission. Required baseline, scoped/repeated/chained/branch/snapshot cases, supplied negative controls, full Debug/Release, native O0/O2 and warm reuse. No new language rule.

Checkpoint at about 30 minutes elapsed: seven initial failures reproduced the missing local path; admitting shape alone then failed value-flow validation in all seven. Default local reads now use ordinary storage while prepared argument reads retain their SSA snapshots. Thirteen new cases (eight executable fixtures and five guards) include initializer Abort, scoped/repeated/chained/branch reads, supplied rejection and warm/reload coverage. A noncompleting local initializer reaches the existing unsupported checking-continuation state; its admission is explicitly guarded even when supplied, with two regression cases. This is a remaining I6/I8 obligation, not a language restriction. Full/native evidence pending.

Completed 04:16 JST, about 33 minutes elapsed. Clean Debug/Release builds and serial full suites pass 7,173 each without failures/skips. Eight fixtures pass 16 native O0/O2 executions per configuration; 171 focused cases pass. The warmed compound-default case now includes a local binding and measures zero bytes; rebind/reload rebuild identical IR. Evidence: `t4j-build-{debug,release}.log`, `t4j-full-{debug,release}.log/xml`, `t4j-native-{debug,release}.log`, `t4j-focused.log/xml`, initial and local-read baseline logs, source/artifact/fixture hashes. `git diff --check` passes; no SPEC/draft edit or NativeAOT. Noncompleting local-initializer checking and mutable locals remain unfinished.

### Execution unit 17 — T4k Unit defaults (DONE)

Started 04:17 JST, about 34 minutes elapsed. Unit is a Copy value with an existing zero-sized argument path; the default whitelist currently excludes it. Admit Unit literals/copies/results and initialized Unit locals through the same checked plans, without admitting effectful calls or mutable storage. Required baseline, chaining/selection/transfer/local/supplied/noncompletion cases, full Debug/Release and native O0/O2. Preserve scalar rules and established warm checks.

Checkpoint at about 37 minutes elapsed: eleven new tests pass (ten native fixtures plus warm Unit reuse). The initial nine failed before admission; call lowering also needed its matching Unit-Type validation rather than the former scalar-only check. No physical Unit operand is invented. Unit selections without else follow the existing Unit result rule. 182 focused cases and 20 Debug O0/O2 executions pass. Warm Unit default analysis/emission measures zero bytes. Full/Release native verification pending.

Completed 04:22 JST, about 39 minutes elapsed. Debug/Release builds have zero warnings/errors; serial full suites pass 7,184 each without failures/skips. Ten Unit fixtures pass 20 native O0/O2 executions per configuration; 182 focused cases and zero-byte warm Unit reuse pass. Evidence: `t4k-build-{debug,release}.log`, `t4k-full-{debug,release}.log/xml`, `t4k-native-{debug,release}.log`, `t4k-focused.log/xml`, baseline, source/artifact/fixture hashes. Forward/self local references were separately probed and rejected by Binding even when supplied (`default-{forward,self}-local.log`). No SPEC/draft edit or NativeAOT.

### Execution unit 18 — T4l initialized mutable scalar default locals (DONE)

Started 04:23 JST, about 40 minutes elapsed. Reuse T4j's corrected local storage and T4k Unit statement results for initialized scalar/Unit var locals, local assignments/updates, and while loops. Restrict every write target to a var declared inside the same default; prepared slots and owned contents remain immutable. Uninitialized/noncompleting initializers, effects, borrows and owned storage remain guarded. Required baseline, repeated finite loops, snapshot/read/update/float/bit behavior, supplied prepared-slot write rejection, Abort locations, full Debug/Release, native O0/O2 and warm reuse.

Checkpoint at about 42 minutes elapsed: twelve new cases and 194 focused cases pass. Eight original execution cases failed before support; three prepared-parameter write cases already failed Binding as required. Assignment/update targets must be mutable locals inside this default, with supported initializers; while and loop bodies retain existing scalar/Unit control flow. Noncompleting/uninitialized local initializers retain guards. Added update Abort location and extended warm/reload coverage to mutable local updates; zero warmed allocation remains. Full/native evidence pending.

Completed 04:29 JST, about 46 minutes elapsed. Final clean Debug/Release builds have zero warnings/errors; serial full suites pass 7,196 each without failures/skips. Nine mutable-local fixtures pass 18 native O0/O2 executions per configuration. Warm update analysis/emission measures zero bytes and rebind/reload preserve IR. Evidence: `t4l-build-{debug,release}.log`, `t4l-full-{debug,release}.log/xml`, `t4l-native-{debug,release}.log`, focused (194), baseline (eight expected failures), source/artifact/fixture hashes. A formatting warning was corrected before final rebuild/full suites. No SPEC/draft edit or NativeAOT.

### Execution unit 19 — T4m scalar prepared-subplace reads (DONE)

Started 04:30 JST, about 47 minutes elapsed. Admit only Copy scalar/Unit results from bound owned fields, tuple elements and fixed-array elements rooted in preceding prepared arguments. Reuse existing projection/bounds/inspection Loan plans and finish the temporary Loan before callee entry. Do not admit non-Copy acquisitions, borrowed-referent reads, writes or arbitrary property getters. Required baseline, nested/dynamic/repeated/index/Unit reads, owner destruction order, bounds Abort, full Debug/Release, O0/O2 and warm reuse. Time is checked before each substantial operation; no new unit after the deadline.

Checkpoint at about 53 minutes elapsed: eleven new cases and 225 focused cases pass. The eight original cases included seven default-support failures and an earlier nested tuple/array Binding failure. Nested tuple reads now have a correct-stage reproducer; the mixed nested-array Binding limit remains open. Lowering caches the following call once, then verifies default receiver identities against that call's actual acquired explicit argument slot and omitted default source. Wrong-parameter alias corruption is rejected with recovery. Index plans retain declaration-side read identity while preserving prepared SSA values. Nine native fixtures include owner destruction order and bounds Abort; warm prepared-element analysis/emission measures zero allocation. Final full/native evidence pending.

Completed 04:39 JST, about 56 minutes elapsed. Clean Debug/Release builds and serial full suites pass 7,207 each without failures/skips. Nine new Debug fixtures pass 18 O0/O2 executions; the final Release replay passes all 79 default fixtures (158 executions), covering the new reads, bounds Abort and owner destruction. Evidence: `t4m-build-{debug,release}.log`, `t4m-full-{debug,release}.log/xml`, `t4m-native-debug.log`, `final-defaults-release.log`, focused (225), baseline and source/artifact identities. Wrong prepared argument identity is rejected and recovers; warm element reads allocate zero bytes. Final Debug replay and Milestone 1 checks are the remaining verification checkpoint. No specification change, draft edit or NativeAOT.

### Final checkpoint for the third timed continuation

Execution checkpoint: **2026-09-17T04:43:08+09:00**, elapsed **59m58s** from 03:43:10 JST. T4g–T4m are DONE, with no unfinished implementation change in the current unit. Final Debug/Release builds have zero warnings/errors and full suites pass **7,207 each**, zero failures/skips. All **79 default fixtures per configuration pass O0/O2 (158 executions each)**. Milestone 1 passes **25 checks per configuration**; the machine-readable report paths are in `final-milestone1-{debug,release}.log`. Both final CLI checks exit 1 with the correct default Move diagnostic. Warm compound scalar/Unit/local/element checks pass at zero measured allocation.

Evidence root: `bin/plan-execution/20260917-034310/`. Final logs: `t4m-build-{debug,release}.log`, `t4m-full-{debug,release}.log/xml`, `final-defaults-{debug,release}.log`, `final-milestone1-{debug,release}.log`, `final-cli-{debug,release}.log`. `final-manifest.txt` records source, diagnostics, documents, compiler/test assemblies and default fixture identities; `final-status.txt` records the uncommitted workspace. Final tested source hashes match. `git diff --check` passes. No SPEC changes were needed because the owning rules are unchanged; draft untouched, NativeAOT NOT_RUN.

M2/M3 and the compiler-completion plan remain IN_PROGRESS. The exact resumption action is T4n in Section 2. Preserve all existing changes and re-read actual HEAD/diff/PLAN before continuing. This checkpoint closes the current verified unit near the soft deadline; no additional work unit is started.

### Execution unit 20 — T4n checking after noncompleting initializers (IN_PROGRESS)

Started 04:43:26 JST after fresh repository/manifest/instruction inspection. Investigate §14.10.3/§15.1 source-checking states after Never initializers and expressions, first in ordinary function bodies, then in every default declaration independently of omission. Preserve initialization/Move/Loan facts without inventing runtime successors or a value that never arrived. Required failing reproducers, ordinary ownership diagnostics, supported controls, reanalysis/reload, full Debug/Release, appropriate native and warm checks before removing any default guard. Keep separate sub-units if the ordinary-state prerequisite is independently verifiable.

**T4n-a IMPLEMENTED_UNVERIFIED, 04:52 JST (about 9 minutes):** fifteen new ordinary nonreturning-call tests fail on the original implementation. Calls now seed checking from acquired arguments, and end their temporary comparison Loans inside that checking region. This also fixes the lost/reintroduced Loan state revealed by local/element borrow controls. No runtime successor or initializer result is added. The former direct-call unsupported case is now a positive regression; loop/selection safety gates remain. Focused Debug checks pass 116 cases, including zero-allocation warm analysis/emission and source reload. Full Debug/Release and seven O0/O2 fixtures per configuration are pending. Evidence: `bin/plan-execution/20260917-044326/t4na-*`.

**T4n-a DONE, 04:57:12 JST (13m46s):** clean Debug/Release builds; full suites pass **7,223 each**, zero failures/skips. An older nonreturning-first-argument unsupported expectation now becomes supported under the same checking rule, with a new bounded native regression. Sixteen added tests total. After correcting the test's expected Abort column from 24 to the actual source column 25, rebuilt focused suites pass 16 per configuration and all **32 native O0/O2 executions** pass (eight fixtures/configuration). Milestone 1 passes **25 checks each**. STATUS updated; no specification change needed. Evidence is `t4na-*` in this run's root. Broader T4n remains IN_PROGRESS.

**T4n-b IN_PROGRESS:** support the ordinary-body prerequisite for state-neutral divergence: a loop containing only Unit or a bare continue to itself. Such a loop preserves all entry ownership facts; seed only its checking continuation from that entry. Keep effectful/branching loops unsupported until their continuation joins are modeled. Verify Never local initializers remain uninitialized, earlier Moves stay moved, checking assignments work, and no runtime result/backedge/cleanup is fabricated.

T4n-b checkpoint, 05:00 JST (about 17 minutes): **IMPLEMENTED_UNVERIFIED**. All nine original-code reproducers fail for the intended unsupported boundary; 189 focused cases now pass. Added a moved-value effectful-loop safety regression and extended result-state/rebind/reload/warm checks to the state-neutral loop. Full Debug/Release and native loop checks are pending. The declaration-side default guard remains unchanged until independent default ownership checking exists.

**T4n-b DONE, 05:02:19 JST (18m53s):** clean Debug/Release builds and full suites pass **7,235 each**, zero failures/skips. All **16 bounded native O0/O2 executions** pass (four fixtures/configuration). Twelve additional cases include no fabricated result/placement, preserved Move and assignment history, source reload, and zero measured warm allocation. The explicit effectful-loop regression retains Unsupported. STATUS updated; normative §14.10.3/§15.1 already specify the behavior.

**T4n-c IN_PROGRESS:** check every supported scalar/Unit default body from initialized preceding prepared parameters using the existing ownership CFG builder and solver, independently of omitted calls. Reuse a scratch body separate from executable function bodies. Then admit scalar/Unit locals with missing or Never initializers under ordinary initialization checking, replacing the T4n guard with required diagnostics. Keep general unsupported effects/owned results/captures and divergent continuation joins guarded. Required declaration/omitted/supplied/bodyless-requirement negatives, supported initialization and nontermination controls, invalidation/reload/warm, full suites and O0/O2 fixtures before DONE.

**T4n-c DONE, 05:10 JST (about 27 minutes):** eighteen baseline reproducers fail on the prior implementation. Independent declaration CFG checking now diagnoses initialization at the default's source, including bodyless requirements and every supplied/omitted/unused case. Scalar/Unit locals with missing/Never initializers use ordinary state checks; the original T4n guard test now requires UninitializedUse. The former initialized-before-use supplied guard is preserved as a native positive control. Full clean Debug/Release suites pass **7,253 each**, zero failures/skips, and all **344 native O0/O2 default executions** pass (86 fixtures/configuration). CLI, reload, rebind and zero-allocation warm valid/invalid checks pass. Evidence: `t4nc-*` in the current root. No SPEC text change is required.

**T4n-d IN_PROGRESS:** caller-result initialization after failed argument acquisition. CLI reproducers `nested-never-initializer.kimi` and `default-never-initializer.kimi` currently exit 0: an enclosing call fabricates a checking result after a Never argument/default. Require every argument acquisition to finish before producing the call result; preserve the checking state and normal initialization diagnostics. This is an additional I6/I8 defect beyond the direct Never-call slice. Separately inspect omitted-default completion in the flow analyzer before admitting dependent control-flow paths.

T4n-d checkpoint: **IMPLEMENTED_UNVERIFIED**. All seven new acquisition/result reproducers fail on prior code. Acquisitions now explicitly gate result production, including nested calls, missing defaults, assignments and owned outputs. Borrow-call validation distinguishes an independently proven noncompleting call from a missing normal result; a malformed-result regression preserves rejection. Focused checks pass 276 cases. Full suites and four fixtures/configuration are pending; rebind/reload/warm and runtime-vs-checking state checks now include nested noncompletion.

**T4n-d DONE, 05:20 JST (about 37 minutes):** clean Debug/Release suites pass **7,267 each**, zero failures/skips. Four new fixtures/configuration pass all **16 O0/O2 executions**; five existing string/aggregate/nonreturning argument fixtures pass **10 further executions** from the final Release generation. Full regression exposed stored-result validators requiring a ghost Produce; they now accept absence only for an unreachable, independently proven noncompleting call. Fourteen additional tests include result-state/reload/warm and malformed normal-result plans. Evidence: `t4nd-*`.

**T4n-e IN_PROGRESS:** compose selected omitted-default completion into call flow independently of declaration ordering. Cache each default's declaration-time flow summary without importing transfers into callers; structural completion must inspect selected defaults and bound recursive expansion. Preserve declared call result Types, fully supplied-call completion, callee-body independence and declaration checks. Then verify require/condition/result contexts and recursive-default safety before proceeding.

T4n-e checkpoint, 05:25 JST (about 42 minutes): **IMPLEMENTED_UNVERIFIED**. Five of seven initial flow probes fail on the prior code, while both supplied controls pass. Default summaries now include actual cleanup completion and are cached independently of call/declaration order; structural paths include selected omitted expressions. Recursive expansion stays pending instead of recursing indefinitely. Focused checks pass 211 cases; Debug full suite passes 7,282. Release/native verification is pending. A fresh analyzer run surfaced eleven formatting/analyzer warnings in new tests, including earlier units; all are fixed and the current Debug build has zero warnings/errors. Earlier “clean” wording for T4n-c/d refers to incomplete incremental warning output and is superseded by this explicit full recheck.

**T4n-e DONE, 05:27 JST (about 44 minutes):** Debug/Release builds have zero warnings/errors, full suites pass **7,282 each**, zero failures/skips. All **20 native O0/O2 executions** pass (five new fixtures/configuration). Fifteen added cases include forward declarations, unchanged declared call Type, supplied calls, require failure, cleanup-blocked completion, nested defaults, recursion safety and zero-allocation warmed flow reanalysis. Evidence: `t4ne-*`. Recursive default expansion remains pending and general effectful default execution remains unsupported.

**T4n-f IN_PROGRESS:** apply the same no-arrival rule to ordinary scalar unary/binary operands. Evaluate/check later source operands but produce no operator result if an operand supplies none. Verify local/default initialization diagnostics, checking-side effects, native nontermination/Abort, full suites and warm/reload state. Keep other noncompleting control-flow joins explicit.

T4n-f checkpoint, 05:32 JST (about 49 minutes): **IMPLEMENTED_UNVERIFIED**. Direct `stop() + 1` probes stop earlier in Binding; typed `value(stop())` operands and the admitted `1 << (loop => continue)` default reproduce seven missing ownership diagnostics. The boolean probe used the wrong `!` spelling and was corrected to `not`. Unary/binary analysis now checks operand arrival before creating its result. Focused checks pass all semantic cases after that syntax correction; full Debug/Release and two native fixtures/configuration are pending. No Binding rule was changed.

**T4n-f DONE, 05:33:03 JST (49m37s):** clean Debug/Release builds and full suites pass **7,294 each**, zero failures/skips. All **eight native O0/O2 executions** pass (two fixtures/configuration). Twelve new cases verify scalar/boolean missing operands, unused/supplied default checking, later-operand effects and warmed result-state/reload behavior. Evidence: `t4nf-*`.

**T4n-g IN_PROGRESS:** reuse the state-neutral divergence proof through single-expression do/label/parenthesis wrappers. This bounded wrapper has no ownership effects, so entry-state seeding is sound; general scope effects and branch joins retain Unsupported. Verify ordinary/default initialization and Move history, native wrappers, rebind/reload/warm and full regression before the final checkpoint. Deadline remains 05:43:26 JST.

T4n-g checkpoint, 05:36 JST (about 53 minutes): **IMPLEMENTED_UNVERIFIED**. Five baseline wrapper reproducers fail for the unsupported continuation; 257 focused tests now pass. The existing state-neutral proof is shared by loop and do wrappers, without allocating or adding runtime successors. An explicit scope with a Move retains the unsupported gate; warm/reload/result-state cases include the wrapper. Final full Debug/Release and native replay are in progress.

Exact next action after this unit: **T4n-h**, general scoped-default checking continuation. `bin/plan-execution/20260917-044326/next-scope-continuation.kimi` is a current CLI reproducer: a default do body contains a Never initializer, a checking-only scalar assignment and internal exit; after the omitted call, a read of an already initialized caller local receives `UnsupportedOwnership_Kd` at line 8:9. Carry the correct continuation out of that scope without restoring moved facts or importing nonexistent runtime arrivals; extend to multiple terminal paths only with explicit joins. The current state-neutral entry proof must not be broadened to effectful scopes merely by dropping its shape check.

**T4n-g DONE, 05:40:44 JST (57m18s):** clean Debug/Release builds and full suites pass **7,302 each**, zero failures/skips. Eight additional tests include the effectful-scope guard. Final replay passes all **364 default** and **80 noncompletion** native O0/O2 executions, including four wrapper executions per configuration. Warm analysis/emission/result-state and reload checks pass. No implementation unit remains unfinished; T4n-h and broader M2/M3 remain open.

### Final checkpoint for the 04:43:26 execution

Recorded **2026-09-17T05:43:06+09:00**, elapsed **59m40s**. T4n-a–g are DONE; 95 added cases, clean builds, 7,302 managed tests per Debug/Release configuration, 444 final native O0/O2 executions, and 25 Milestone 1 checks per configuration all PASS. Six final CLI checks reject the formerly accepted missing-initialization cases with UninitializedPlace_Kd. Warm checks PASS with zero measured allocation. Evidence is in bin/plan-execution/20260917-044326/final-*; final-manifest.txt binds the current source, test, documentation, binaries and fixture bytes. git diff --check passes; prior changes are preserved, draft files were not edited, and NativeAOT was NOT_RUN as instructed.

No implementation unit is in progress or unverified. Stop near the soft deadline without starting another substantial unit. The compiler-completion plan and M2/M3 remain IN_PROGRESS; resume T4n-h from the exact scoped-default reproducer above after fresh repository/PLAN inspection. No new user decision or external blocker is required. All changes remain uncommitted.

### Execution unit 21 — I6/I8 T4n-h scoped checking continuation

**IN_PROGRESS.** Execution started 2026-09-17 05:43:33 JST; soft deadline 06:43:33 JST. Fresh HEAD is 826f6077a2654a6d2be8aa3865b655224bf615ae; all 1,166 entries of the preceding final manifest match. Root AGENTS.md, execution instructions, current PLAN, ownership implementation and relevant §14.10.3 rules were reread; no nested AGENTS.md. Prior changes are preserved. Evidence root: `bin/plan-execution/20260917-054333/`.

The recorded scoped-default CLI reproducer still fails with UnsupportedOwnership_Kd at line 8:9. Implement the bounded straight-line scope continuation first: retain the terminal checking state before lexical restoration, including checking-only assignments and Moves. Do not seed from scope entry. Branching scopes and deferred cleanup require separate joins and remain guarded. Required verification: positive/negative state cases, runtime/result isolation, rebind/reload and warm allocation checks, Debug/Release suites, and ordinary O0/O2 native fixtures. SPEC already states the required behavior; no specification change is needed.

T4n-h checkpoint, 05:53 JST (~10 minutes): IMPLEMENTED_UNVERIFIED. Fifteen of seventeen new baseline cases failed for the expected lost checking state. The scope now captures the terminal checking seed before lexical restoration, guarded by a retained allocation-free straight-line visitor. The CLI reproducer passes; focused checks pass except a test helper subsequently corrected to identify the outer local write. Initial full Debug found one former unsupported nested outward-transfer fixture now valid. Its expectation is updated from the specified transfer semantics and a native case verifies that the later argument increment never executes. Required final rebuilds/full suites and native verification remain pending.

**T4n-h DONE, 05:55:08 JST (11m35s):** final clean Debug/Release builds and full suites PASS, **7,323 each**, zero failures/skips. Six new fixtures per configuration PASS O0/O2 (**24 native executions**); result-state, rebind/reload and warmed zero-allocation checks PASS. Twenty-one net additional cases preserve the formerly guarded moved-scope rejection as a precise PossiblyMovedUse test. No specification change or draft edit was needed. Evidence: `h-final2-*`, `h-native-*`, baseline CLI/tests and checkpoint manifest in the current evidence root.

### Execution unit 22 — I6/I8 T4n-i terminal conditional joins

**IN_PROGRESS, 05:55:08 JST (11m35s).** Next bounded change: construct a separate checking join when every if/else branch terminates and each branch supplies a verified continuation seed. Merge initialization guarantees by intersection and possible Move/assignment history by union using the existing state lattice. Do not add runtime edges or results. Missing seeds and unequal active comparison-Loan heads remain guarded. Required verification: both branch orders, initialized/uninitialized/Moved/let-history cases, checking-only writes, nested continuations, no runtime-state changes, rebind/reload/warm allocation, full Debug/Release and native O0/O2 behavior.

T4n-i checkpoint, 06:01 JST (~18 minutes): IMPLEMENTED_UNVERIFIED. All twelve baseline cases reproduced unsupported continuation diagnostics. Sixteen new cases now pass, including both branch orders, nested and already-unreachable joins, required rejection when a branch seed is unavailable, runtime-state isolation and zero-allocation warm/reload checks. Focused suite: 293 PASS. The join uses retained seed storage and the existing Must-intersection/May-history-union lattice; it requires every seed and identical active comparison-Loan heads. Full suites and five native fixtures/configuration are pending. Initial test-helper compilation/startup mistakes were corrected before claiming verification; final Debug build is clean.

**T4n-i DONE, 06:04:21 JST (20m48s):** clean builds and full suites PASS **7,338/configuration** before the final two test-only Borrow additions; all **18 conditional tests/configuration** then PASS against unchanged production code. Six fixtures/configuration PASS all **24 native O0/O2 executions**. Nested checking joins, branch-order symmetry, unavailable seeds, identical/unequal Loan stacks, rebind/reload and zero-allocation warm checks are covered. Seventeen net added cases; the former all-return conditional guard is now covered by a positive native/state test. Evidence: `i-full-*`, `i-loan-*`, `i-native-final.log`.

### Execution unit 23 — I6/I8 T4n-j scoped terminal conditional joins

**IN_PROGRESS, 06:04:21 JST (20m48s).** Extend the scope continuation proof to closed terminal if/else selections whose conditions complete and whose branches all terminate. Reuse T4n-i's checking joins; keep partially terminating branches and deferred cleanup guarded. Verify caller/default state, branch-local checking assignments, required rejection for missing initialization/Move on either path, native divergence and ordinary return behavior, nested scopes, reload/warm reuse, and full Debug/Release suites. This extends the existing §14.10.3 implementation; no specification change is needed.

T4n-j checkpoint, 06:07 JST (~23m27s): IMPLEMENTED_UNVERIFIED. Nine of twelve baseline cases failed for missing outer continuation; all 307 focused cases now PASS. The scope proof admits closed terminal if/else trees and reuses the explicit checking joins. Supplied-default declaration checking, both branch paths, caller initialization/history, nested scope effects and warmed reload checks pass. Full suites and three new fixtures/configuration are pending. Recompilation exposed two extra final newlines and a parameter-formatting warning in the preceding unit's final test addition; these were corrected, and final clean builds are required before DONE.

**T4n-j DONE, 06:08:59 JST (25m26s):** final Debug/Release builds are clean and full suites PASS **7,352 each**, zero failures/skips. Twelve new cases, three fixtures/configuration and all **12 native O0/O2 executions** PASS. Nested scopes, default declaration/caller checking, Move/init/history diagnostics and warmed reload checks PASS. Evidence: `j-final-build-*`, `j-full-*`, `j-native-*`. T4n-h–j are DONE; broader M2/M3 remain incomplete.

### Execution unit 24 — I6/I8 T4n-k completing selections before scope termination

**IN_PROGRESS, 06:08:59 JST (25m26s).** Admit normally completing if branches within a scope's continuation proof only when every contained operation completes and no transfer/deferred/loop path can escape that selection. Use the existing runtime/checking branch join before the later terminal operation; retain guards for partial termination. Verify both branch orders, initialization/Move history and static aggregate paths, native effects and missing successors, default-local branches, rebind/reload/warm reuse and full Debug/Release.

T4n-k checkpoint, 06:12 JST (~28m27s): IMPLEMENTED_UNVERIFIED. Twelve of fifteen baseline cases reproduced the lost continuation; all 322 focused cases PASS. Normally completing selections are admitted only while a nested proof forbids any terminal child or transfer, preventing partial-return paths from being silently dropped. Static tuple Move paths and repair, both initialization branch orders, caller/default state and warmed reload pass. Debug build is clean; full suites and five native fixtures/configuration are pending.

**T4n-k DONE, 06:12:55 JST (29m22s):** clean Debug/Release builds and full suites PASS **7,367 each**, zero failures/skips. Fifteen new cases and five fixtures/configuration PASS (**20 native O0/O2 executions**). Static aggregate paths, required partial-termination guards, default-local branches and warmed reload checks PASS. Evidence: `k-build-*`, `k-full-*`, `k-native.log`.

### Execution unit 25 — I6/I8 T4n-l divergence with scalar loop-local effects

**IN_PROGRESS, 06:12:55 JST (29m22s).** Extend the no-enclosing-state-change divergence proof to scalar/Unit loop-local declarations and mutation, scalar reads/arithmetic and contained control flow. Outer writes, owned acquisitions, calls and deferred cleanup remain excluded. A loop with no normal completion may seed its successor checking from the loop head only after this proof; runtime edges/results remain unchanged. Verify outer initialization/history, guarded outer mutations, default execution, nested local control flow, ordinary O0/O2 nontermination, rebind/reload and warm reuse, plus full Debug/Release.

T4n-l checkpoint, 06:18 JST (~34m27s): IMPLEMENTED_UNVERIFIED. Nine of fourteen original baseline cases reproduced the gap; all 336 initial focused cases PASS. The retained proof accepts only scalar/Unit loop-local effects and contained transfers; calls, owned values, outer writes and deferred effects remain excluded. A former unavailable-seed test used a now-proven local-only loop and has been replaced with an outer-mutating loop to preserve its guard responsibility. Six further cases cover contained exits, nested warm control flow, outward-transfer guards and invalid local use. Final full suites and six native fixtures/configuration are pending.

**T4n-l DONE, 06:19:03 JST (35m30s):** clean Debug/Release builds and full suites PASS **7,387 each**, zero failures/skips. Twenty new cases and six fixtures/configuration PASS (**24 native O0/O2 executions**). Nested proof reuse, contained transfers, ordinary invalid local-use diagnostics, caller/default facts and warmed reload checks PASS. Evidence: `l-final-build-*`, `l-full-*`, `l-native-*`.

### Execution unit 26 — I6/I8 T4n-m noncompleting conditional operands

**IN_PROGRESS, 06:19:03 JST (35m30s).** Address a noncompleting if condition: source branches still require ordinary checking and their joined state must continue after the selection, without a condition value or runtime branch/result. Reuse the existing separate checking join for normally completing source branch tails as well as transfer tails. Verify ownership/history across hypothetical branches, default conditions, absent results, Borrow release, reload/warm state, full Debug/Release and native O0/O2. Scoped proof expansion is separate unless required by the bounded cases.

T4n-m checkpoint, 06:23 JST (~39m27s): IMPLEMENTED_UNVERIFIED. All thirteen baseline cases failed for the intended missing condition continuation. The first 355 focused cases pass; five additional cases now cover absent else, else-if predecessor state and required rejection of a partial-return source branch. Branch checking seeds include only proven completing tails or terminal continuations, and the implicit false path is preserved. Full suites and eight native fixtures/configuration are pending; the Debug production build is clean.

T4n-m verification checkpoint: both clean full suites PASS 7,405/configuration. An early Release native attempt had no fixtures because the managed run had not yet generated that class; it is NOT completion evidence. Both final configurations are now present and are being replayed serially in `m-native-final.log`, superseding the premature attempt and overlapping initial native outputs.

**T4n-m DONE, 06:25:07 JST (41m34s):** clean Debug/Release builds and full suites PASS **7,405 each**, zero failures/skips. Eighteen new cases; final serial native replay PASS **56 O0/O2 executions** (32 new-condition executions plus 24 conditional-join regressions matched by the prefix). Borrow/state/default/implicit-path and zero-allocation reload checks PASS. Evidence: `m-final-build-*`, `m-full-*`, `m-native-final.log`.

### Execution unit 27 — I6/I8 T4n-n scoped noncompleting conditions

**IN_PROGRESS, 06:25:07 JST (41m34s).** Reuse the now-verified condition checking join through do wrappers, checking each condition and each source branch with its appropriate completing/terminal proof. Preserve missing-else paths and partial-termination guards. Verify scoped initialization/Move/Borrow behavior, scalar-default divergence, reload/warm reuse, full Debug/Release and native O0/O2.

T4n-n checkpoint, 06:28 JST (~44m27s): IMPLEMENTED_UNVERIFIED. All eight new scoped cases failed on the baseline; all 368 focused cases now PASS. The retained scope proof checks conditions and independently chooses completing/terminal branch proof, including missing else. Scoped result absence, caller/default state, Borrow release and warmed reload PASS. Full suites and four native fixtures/configuration are pending; Debug build is clean. Elapsed time was checked at 06:27:26 (43m53s) before broad verification.

**T4n-n DONE, 06:29:04 JST (45m31s):** clean Debug/Release builds and full suites PASS **7,413 each**, zero failures/skips. Eight new cases and four fixtures/configuration PASS (**16 native O0/O2 executions**). Scoped default/Borrow/result-state checking and warmed reload PASS. Evidence: `n-build-*`, `n-full-*`, `n-native-*`.

### Execution unit 28 — I6/I8 T4n-o noncompleting while conditions

**IN_PROGRESS, 06:29:04 JST (45m31s).** CLI `while-condition.kimi` currently rejects the initialized read at line 4:9 with UnsupportedOwnership_Kd. Preserve the state after a noncompleting condition when a body-local scalar proof excludes enclosing effects; never seed from before condition acquisition. Body source still requires ordinary checking. Verify condition Moves/Borrow release, body-local errors, default divergence and scoped/reload reuse; finish full Debug/Release plus native evidence and final replay within the soft time rule. General outer-mutating while bodies remain guarded.

T4n-o checkpoint, 06:32 JST (~48m27s): IMPLEMENTED_UNVERIFIED. Ten of thirteen baseline cases reproduced missing continuation; all 381 focused cases PASS. The continuation uses state after condition acquisition and cleanup, never the while head. Body-local proof is shared with local loops; outer body writes/calls remain guarded. Scope wrappers use the same proof. Debug build is clean. Final full suites, five new native fixtures/configuration, full Default/Never native replay and ordinary Milestone 1 checks are in progress. No further implementation unit is planned before this execution's soft deadline.

Final verification checkpoint, 06:34:17 JST (50m44s): both final builds are clean and full suites PASS **7,426/configuration**. Four positive CLI checks pass (original scope and while reproducer in each configuration); six negative checks reject uninitialized/Move cases with precise UninitializedPlace_Kd/MovedPlace_Kd. Two next-scope checks retain UnsupportedOwnership_Kd at 8:15. Serial replay covers all 182 Default plus 126 Never fixture files (616 O0/O2 executions expected); it is still running and is not yet PASS. Milestone 1 is next. No NativeAOT or draft edits.

**T4n-o DONE, 06:39:27 JST (55m54s):** final clean Debug/Release builds and full suites PASS **7,426 each**, zero failures/skips. Thirteen new cases and five new native fixtures/configuration pass. Final serial replay passes all **364 Default + 252 Never = 616 ordinary native O0/O2 executions**. Milestone 1 passes **25/configuration**, with reports `bin/milestone1/Debug/81cd458228a34cf6b733bcc5ab58d0b0/verification.json` and `bin/milestone1/Release/5ff41039b4dc486c8209c1767c56802f/verification.json`. Both reports target the final compiler binaries. Warm/reload and exact CLI diagnostics pass. Zero changed source files are newer than either configuration's verified binaries.

### Final checkpoint for the 05:43:33 execution

Final close recorded **2026-09-17T06:41:28+09:00**, elapsed **57m55s**, before the 06:43:33 soft deadline. All current units and required verification are complete. The next scope-wide tracking unit is not started in the remaining two-minute window. The final 1,675-entry manifest was verified with zero mismatches; native smoke report hashes match the final compiler binaries. The plan remains open for the next execution.

T4n-h–o are DONE: eight coherent units, 124 net added cases, full Debug/Release, native O0/O2, Milestone 1, rebind/reload and zero-allocation warmed verification PASS. The source and tests were not edited after final verification began. STATUS.md summarizes current support and limitations; SPEC.md already defines the required rules and needed no edits. No draft file was changed, no NativeAOT test was run, and no commit was created. No new out-of-scope finding or user decision is required. The premature native attempt recorded above was superseded by a complete serial replay.

The compiler-completion plan and M2/M3 remain IN_PROGRESS. **Exact next action: T4n-p**, begin with `bin/plan-execution/20260917-054333/next-partial-scope.kimi`. Both current CLI configurations report UnsupportedOwnership_Kd at line 8:15; a correct join must preserve the Move on the early-return branch and diagnose MovedPlace_Kd. Add an explicit checking join for every terminal path of the scope, including a partial branch followed by later termination; never substitute the scope entry or add runtime predecessors. Verify the positive initialized-state counterpart, both branch orders, missing initialization, Move/let history, cleanup/Borrow boundaries, reload/warm reuse, and full/native behavior. This requires scope-wide terminal-path tracking and is not started in the short remaining window.

Final evidence is under `bin/plan-execution/20260917-054333/final-*`. `final-manifest.txt` binds the final source/tests/docs, compiler/test binaries, all Default/Never fixture metadata, verification logs and native smoke reports. Re-read current repository state and compare this manifest before resuming; historical checks are not proof about later changes.

### Execution unit 29 — I6/I8 T4n-p partial scoped terminal joins

**DONE at 09:09:00 JST (first verified at 08:53, revised and reverified below); execution started 2026-09-17 08:30:38 JST, soft deadline 09:30:38 JST.** HEAD `fb3ebe5`, clean tree; the 05:43 evidence root is gone, so the reproducer was rebuilt as `bin/plan-execution/20260917-083038/next-partial-scope.kimi` (Stop declaration, `do` with `if c` moving `s` then `return`, later `stop()`, then `writeLine(s)` at 8:15). Baseline with rebuilt Debug binaries: `baseline-next-partial-scope.log` reports UnsupportedOwnership_Kd at 8:15; `baseline-partial-scope-positive.log` (both paths initialize `x`) reports UnsupportedOwnership_Kd at 10:13; `baseline-terminal-branch-partial.log` (function-level terminal if/else whose first branch contains `if d` moving `s` and returning before `stop()`) exits 0, so the dead `writeLine(s)` at 10:15 missed the possibly-moved diagnostic. All three share one root cause: a completing selection discarded the continuation seed of a terminal branch, and terminal selection joins used only each branch's final seed.

Change (`OwnershipAnalysis.cs`, `OwnershipAnalysis.ScopedChecking.cs`): the analysis keeps a reused per-function `terminalSeeds` list. `Conditional` records, for a completing selection, the continuation seed of every branch/else body that cannot complete normally (`RecordTerminalSeed`); for a noncompleting selection it records each branch's verified seed as before. `JoinChecking(source, mark)` now joins every seed recorded since the caller's mark: it still requires every path to be available and every active comparison-Loan head to agree, uses the existing single-seed region form for one path, and copies multiple seeds into `CheckingSeeds` with an immediate entry Branch so a nested continuation reads the join. `ScopedBody` takes a mark at entry; a noncompleting scope whose proof passes appends its own continuation and joins the recorded paths, otherwise it releases them; a completing scope leaves its pending paths to the enclosing join. The scope proof no longer forbids termination inside completing selections (every terminal branch is now recorded), so the `IfKoto` special case and `VisitBranchBody` were deleted; loops, `while` with completing conditions, `match`/`for`/`require`/deferred blocks and `and`/`or` remain refused. No runtime edge, result or cleanup is added; single-path regions keep the previous operation shape. Net source change is 62 insertions and 75 deletions.

Tests: new `PartialScopeContinuationTest` (27 cases): both branch orders for Move/uninitialized/let-history diagnostics, nested partial branches inside terminal and completing branches and inside a completing nested `do`, positive initialized/Moved-before/Borrow counterparts with native `done` output, later termination Abort with no checking successor executed, omitted/supplied partial scalar defaults, function-level terminal selections with nested partial paths, retained guards (deferred cleanup, completing `loop` with an inner return, unequal comparison-Loan heads), and reload plus zero-allocation warm reuse. `ScopedContinuationTest.MultipleTerminalPathsAndCleanupRetainTheirGuard` became `DivergentCleanupRetainsItsGuard` (the partial-scope case is now supported and covered positively); `CompletingScopeContinuationTest.PartialTerminationStillRequiresAnOuterJoin` became `PartialTerminationJoinsEveryPath`, asserting verified analysis for the same three shapes. A first default-declaration case used `stop()` inside the default and was rejected as an effectful default (existing I4 boundary, not this unit); it now diverges by `loop => continue` on both paths, and its dead read sits after the inner scope because dead source inside the scope after the second loop correctly sees only that loop's state.

Evidence (`bin/plan-execution/20260917-083038/`): after the change, `after-next-partial-scope.log` reports MovedPlace_Kd at 8:15, `after-terminal-branch-partial.log` reports MovedPlace_Kd at 10:15, and `after-partial-scope-positive.log` exits 0. Focused Debug suites (Continuation/UnreachableOwnership/ScalarDefault/DefaultCompletion/Ownership filters) PASS 740/740 (`focused-debug-5.log`). Clean Debug and Release builds have zero warnings/errors. Full suites PASS **7,452/configuration**, zero failures/skips (`full-debug.log`, `full-release.log`; 26 net added cases). New fixtures `NeverPartialScope{Debug,Release}*` (eight per configuration) PASS **16 ordinary native O0/O2 executions per configuration** (`native-debug-partial.log`, `native-release-partial.log`). Serial broader replay PASS: **284 `Never*` and 406 `*Default*` ordinary native O0/O2 executions** (`native-never-all.log`, `native-default-all.log`; the four `NeverPartialScope*Default*` fixtures match both patterns). Milestone 1 PASS **25 checks/configuration**: `bin/milestone1/Debug/f8276af0d6fc48c9aacc864c6b053987/verification.json` and `bin/milestone1/Release/b9a4a24c520c4742acbf45c024c6f122/verification.json` (`milestone1-debug.log`, `milestone1-release.log`). Both compiler builds postdate the analysis source edits and both test assemblies postdate the test edits. STATUS.md summarizes the new support and remaining limits; SPEC.md already states the rules and needed no edit. No draft file was changed, no NativeAOT test was run, and no commit was created.

**Revision (T4n-q attempt and T4n-p correction, 08:53–09:09:00 JST).** A bounded T4n-q slice (recording a completing loop body's terminal continuation and walking completing loops as ordinary proof children) was implemented and its focused analysis cases passed, but the native fixture `loop\n    if c\n        x = 1\n        return\n    exit\nx = 2\nstop()` reported a spurious UninitializedPlace_Kd at the dead `let y = x` (`loop-fixture.kimi`, `focused-debug-q1.log`): the state at the loop-internal `exit` (x uninitialized) was joined as if it left the scope, although at runtime it continues through `x = 2`. The same over-approximation applied to T4n-p's first form: a partial branch whose terminal transfer is labeled (`exit`/`continue`/`yield` to a loop or selection inside the joining construct) must not be joined as a terminal path; `labeled-partial-terminal-selection.kimi` (function-level terminal if/else whose first branch holds `loop\n    if d => exit\n    x = 1\n    exit` before `x = 2`/`stop()`) would have rejected a valid program in dead source. The T4n-q edits were reverted and T4n-p was made conservative: `OwnershipCheckingRegion` gained `Labeled`, set by `Jump` for every transfer other than a direct function return; `Block` reports whether its terminal region is labeled; `RecordTerminalSeed` records a labeled partial path as unknown (`-2`) instead of a seed; `JoinChecking(source, mark, labeled)` omits unknown paths from a selection's join (exactly the pre-T4n-p behavior for those joins) and refuses a scope join that contains one, and propagates `Labeled` so an enclosing completing selection treats such a branch as unknown. Loops that pass their own divergence/local proof release the seeds recorded inside them, because those proofs already exclude function-terminal paths (this restored `LocalLoopContinuationTest.EmitsLoopsWhoseEffectsStayLocal(Nested)`, which failed once in `focused-debug-fix1.log`). Tests: `LabeledPartialPathsNeverJoinAsTerminal` (two accepted programs: loop-internal `exit` and `exit to inner` partial paths inside a terminal if/else) and a scope guard for a labeled partial path (`loop\n    do\n        if c => exit\n        writeLine(s)\n        stop()\n    writeLine(s)`) were added to `PartialScopeContinuationTest`; the completing-loop guard case was restored. Final CLI evidence (`final-*.log`): partial scope MovedPlace_Kd 8:15, terminal branch MovedPlace_Kd 10:15, positive scope exit 0, labeled partial selection exit 0, completing loop and loop fixture UnsupportedOwnership_Kd (guarded, no spurious error).

Final verification after the revision: focused Debug suites PASS **743/743** (`focused-debug-fix3.log`); clean Debug/Release builds; full suites PASS **7,455/configuration**, zero failures/skips (`final-full-debug.log`, `final-full-release.log`); `NeverPartialScope*` fixtures PASS **16 native O0/O2 executions per configuration** (`final-native-debug-partial.log`, `final-native-release-partial.log`); broader replay PASS **284 `Never*` and 406 `*Default*` executions** (`final-native-never-all.log`, `final-native-default-all.log`); Milestone 1 PASS **25/configuration** (`bin/milestone1/Debug/64187af0f669403f80df9d887ec5926d/verification.json`, `bin/milestone1/Release/994fe9bb29794b7e87bc974d83757ef0/verification.json`). Net source change: 7 files, about 134 insertions and 107 deletions including tests.

**Exact next action: T4n-q with per-path target extents.** `next-completing-loop.kimi` and `loop-fixture.kimi` remain guarded. A correct completing-loop join needs each recorded terminal seed to carry the extent it leaves (function, or the loop/selection it targets) and each join at construct K to include only seeds whose target is outside K, dropping loop-internal ones; multi-seed join regions must keep their seeds individually available for enclosing joins rather than a single merged target, or refuse mixed-target propagation. Only then may `ScopedCheckingProof` accept completing loops. Deferred cleanup, unequal Loan heads and `and`/`or` conditions remain guarded.

### Final checkpoint for the 08:30:38 execution

Final close recorded **2026-09-17T09:10:00+09:00**, elapsed about **39m30s**, before the 09:30:38 soft deadline. T4n-p is DONE with all required verification on the final source; no implementation unit is in progress or unverified. The T4n-q per-path-extent redesign is not a small unit and was not started in the remaining window. The stale `NeverPartialScopeDebugWhile.*` fixture from the reverted slice was deleted before the final Debug partial and `Never*` replays. No draft file was changed, no NativeAOT test was run, and no commit was created; all changes remain uncommitted for the user to review.

### Execution unit 30 — I6/I8 T4n-q completing loops and transfer extents

**DONE, 2026-09-17 09:42 JST.** Resumed from clean HEAD `97bebe1`. Evidence: `bin/plan-execution/20260917-092418/`. The unchanged Release compiler reproduced UnsupportedOwnership_Kd for unit 29's `next-completing-loop.kimi` (10:15) and `loop-fixture.kimi` (12:13); the updated Debug compiler reports the required MovedPlace_Kd for the former and accepts the latter. Logs: `baseline-*` and `after-*`. Initial `dotnet test` probes encountered the sandboxed NuGet config and then selected zero cases with incompatible MTP switches; neither is test evidence. All subsequent tests use the direct xUnit DLL runner with nonzero counts.

Implementation: replace the reused integer seed list and opaque `Labeled` flag with value-type `(Seed, Target)` continuations and region target metadata. Each completing loop/while body records its terminal continuation; loops, scopes and selections remove transfers caught within their own extent before propagating pending paths. The scope proof visits completing loops through its existing child traversal. Never calls, jumps and proved divergence share `BeginChecking`, preserving the original target when another transfer appears in already-dead source. This preserves existing dead-source assignments/defaults instead of treating their later written exit as a new runtime path. No execution edge, result or cleanup is synthesized. Lists and visitors retain their storage; the reload/warmed analysis plus IR-emission test measures zero allocated bytes.

Mixed-target joins remain valid for local checking, but `MixedTargets` explicitly refuses propagation of their merged state into another enclosing join. This implements unit 29's allowed conservative alternative; it does not claim general per-target replay of effects after a mixed join. Other existing cleanup/Loan/short-circuit/divergence guards remain.

Tests: `CompletingLoopContinuationTest` adds **32 cases**, covering early-return Move/uninitialized/let histories, loop and while, internal exit/continue/yield, nested and outer targets, same-target joins, dead transfers, function-terminal selections, Borrow ending, omitted/supplied scalar defaults, mixed-target guards, serialization/reload and zero warmed allocation. Two former partial-scope guard cases now check successful completing-loop analysis and the correct Move diagnostic. Focused Debug checks PASS **639**; full Debug and Release checks PASS **7,487 each**, zero failures/skips; builds have zero warnings/errors. Sixteen new fixtures/configuration PASS **32 O0/O2 native executions/configuration**. Broader replay PASS **348 Never + 414 Default executions** (patterns overlap for default/Never fixtures); Milestone 1 PASS **25/configuration**, reports at `bin/milestone1/Debug/684f853e8e3945e6b5fdd842d32a9632/verification.json` and `bin/milestone1/Release/599310bce5fd4088846e314e000fa532/verification.json`. Native execution initially encountered sandbox tool permissions and then passed under authorized escalation. Source/artifact and fixture hashes are recorded in `source-artifact-hashes.json` and `fixture-hashes.json`. SPEC.md and §14.10.3 already define the required behavior and need no change; no draft edits, NativeAOT tests or commit.

**Exact next action:** mixed-target propagation. `next-mixed-target.kimi` and `next-mixed-target.log` in this unit's evidence root reproduce UnsupportedOwnership_Kd at 12:13 for a scope containing `loop\n    if c\n        x = 1\n        return\n    else => exit`, followed by `x = 2; stop()` and an outer dead read of `x`; the correct result is acceptance. The terminal selection's local join is valid, but an outer loop must retain only the return path while handling the exit normally. Keep constituent targets and any subsequent checking-only effects separate until the enclosing extent has selected its escaping paths; do not assign one target to a merged state or drop an unavailable path. Deferred cleanup, unequal Loan heads, short-circuit conditions and effectful divergent loops remain separate unfinished obligations. M2/M3 remain IN_PROGRESS; this unit has no unverified implementation work.

### Execution unit 31 — I6/I8 T4n-r unchanged mixed-target continuations

**DONE, 2026-09-17 14:58 JST (about 14 minutes elapsed).**
The baseline Debug build was clean and 62 existing loop/partial-scope tests passed.
The current-source CLI reproduced unit 30's UnsupportedOwnership_Kd at 12:13.
Checking seeds now retain both operation and transfer target. A mixed region may
export its original constituent seeds only while its current tail is exactly the
join entry; enclosing constructs filter their own handled targets before joining.
The local common-state join and runtime CFG are unchanged. Subsequent checking
effects still produce an unavailable continuation rather than a guessed target or
discarded state. This is a deliberately bounded slice of unit 30's next action.

Sixteen new tests cover nested selection/loop/scope/yield extents, branch order,
both execution choices, Move/init/let histories, local dead-source checking and
reload/warmed zero-allocation analysis/emission. The old empty-tail guard case is
now positive coverage; its guard slot uses a subsequent assignment instead.
Focused Debug PASS 78; final Debug/Release builds have zero warnings/errors and
both full suites PASS 7,583 each, zero failures/skips. LLVM/native O0/O2 regression
PASS 380 Never + 414 Default executions (overlapping families), including 32 new
executions. Milestone 1/8 PASS 25/46 checks per configuration. Evidence root:
`bin/plan-execution/20260917-144405/`. SPEC §14.10.3 already defines the
behavior; no language change or draft edit is needed. A possible next bounded
slice is an identity continuation through a subsequent bare transfer:
`next-bare-transfer.kimi` still reproduces UnsupportedOwnership_Kd at 13:13.
Preserve original targets and pre-cleanup seeds; never apply a later dead transfer's
target to every incoming path. Additional checking effects remain separate work.

### Execution unit 32 — I6/I8 T4n-s bare transfers after mixed joins

**DONE, 15:03 JST (about 19 minutes elapsed).** Retain the
constituent seed range when a bare transfer preserves the mixed join's exact
pre-cleanup seed. Capture its region before lowering implicit cleanup: the first
focused run exposed three chain/reuse failures because cleanup itself gave the
previously empty region an entry before provenance was inspected. The corrected
path captures the existing value-type region once, without allocating or changing
runtime edges. Ordinary unavailable/effectful continuations remain guarded.
Eleven new cases cover return/exit/continue and consecutive transfers, both native
choices, and invalid initialization/Move/let histories. Reload/warmed allocation
coverage now includes consecutive transfers. Final Debug/Release builds have zero
warnings/errors; full suites PASS 7,594 each with no failures/skips. Sixteen new
fixtures across both configurations PASS 32 LLVM/O0/O2 native executions. Every
previous Never/Default IR and expectation hash matches final regeneration, retaining
unit 31's native evidence without unnecessary repeat execution. Together these
two units add 27 cases and 64 new native executions. No NativeAOT test, draft edit,
specification change or commit was made; prior uncommitted work is preserved.
Evidence uses `s-*` files in `bin/plan-execution/20260917-144405/`.

Next action: `next-mixed-effect.kimi` adds `x = 3` after the mixed terminal selection
and reproduces UnsupportedOwnership_Kd at 13:13. It requires per-target application
of later checking effects; a merged state cannot be filtered correctly afterward.
Do not remove the guard without that representation and its negative/Loan/cleanup
tests. No part of this next implementation was started under the remaining budget.

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

The initial planning deliverable was complete when its scope, architecture, stable coverage IDs, dependencies, gates and honest evidence records were reviewable, with only `PLAN.md` changed during that phase. The current implementation effort is subject to the compiler completion criteria below; a timed execution checkpoint is not compiler completion.

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
| 2026-09-17 | Timed continuation from 05:43:33 completed T4n-h–o at unchanged HEAD, preserving all prior work | Eight units add 124 net tests; final suites pass 7,426 each, native replay passes 616 O0/O2 executions, and Milestone 1 passes 25/configuration. Scoped/conditional/loop checking expands under explicit proofs; broader M2/M3 and partial-scope joins remain open. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Third timed continuation completed T4g–T4m at unchanged HEAD `826f6077`, preserving all earlier changes | Seven units add 91 tests; final suites pass 7,207 each, default native replay passes all 79 fixtures at O0/O2 per configuration. Broader M2/M3 remain incomplete; next T4n addresses checking after noncompleting local initializers. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Second timed continuation completed T4a–T4f at unchanged HEAD `826f6077`, preserving prior uncommitted changes | Six default-plan/flow/scalar-execution units, 66 new tests, final Debug/Release and O0/O2 evidence. M2/M3 remain incomplete; next T4g checks forbidden operations on prepared slots at declaration time. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Timed execution at HEAD `826f6077` completed T1a, T5a/G9, T27a, T27b and T7a, with fresh failing reproducers and final Debug/Release verification | Five focused changes close modifier diagnostics, missing-conformance cascades, stale analysis, source-error lifetime and pending-effect diagnostic classification. 63 new tests and three strengthened cases; no specification weakening or draft edits. I4 pending-slot audit records the next implementation boundary. |
| 2026-09-17 | Created PLAN.md at the user-designated path; implementation remains TODO | Full finalized-spec completion requires a maintained execution record, not a single-feature patch. |
| 2026-09-17 | Reused existing struct/borrow/CFG/Project graph work rather than planning replacements | Inspected current code is newer than some STATUS summaries; program milestone numbers are distinct from M IDs. |
| 2026-09-17 | Retained object/Weak and collection contracts in scope; separated undefined host/test interfaces | Owning specifications settle former features while explicitly deferring the latter interfaces. |
| 2026-09-17 | Recorded help-triggered NuGet access failure and existing runner help separately from tests | No current conformance test run occurred; planning did not justify restore/build or native artifact mutation. |
| 2026-09-17 | Corrected representative optional-parameter syntax, verified actual dependency-example commands, and assigned generic enum integration to M6 | Final review prevented an unintended earlier error in T4 and a circular M5/M6 completion dependency. No baseline requirement was removed. |
| 2026-09-17 | Implementation started: I1 completed as a verification-only checkpoint; G5 closed as an environment artifact; evidence archived under `bin/plan-baseline/m1-8c49edb/` | Fresh restore/build/test at HEAD `8c49edb` succeeded with no configuration error, so no NuGet or source change was justified. Historical 6,986 figure is now current evidence for this HEAD only. |
| 2026-09-17 | I2 recorded as a clause coverage map in Section 4 and M1 closed | Mapping used executed per-class counts and keyword scans rather than reading every test body; a class PASS is evidence only for what it asserts, so uncovered clauses stay assigned to their T/I items instead of being marked covered. |
| 2026-09-17 | M2 started; I3 audit recorded; T3a implemented (merged containers keep the first fragment's CodeContext) with a regression test; G9 opened and assigned to I5 as T5a; STATUS gained a dated entry | Appendix A.1 requires retaining fragment contexts after merging; the parent's source-less context produced document-less diagnostics. The sibling cascade is a separate semantic decision, so it was recorded rather than patched in the same unit. |
| 2026-09-17 | Observed a concurrent user addition `draft/Design/2026-09-17 Documentation Comments.md` (untracked) | Preserved unread and unedited per the draft exclusion; not part of this execution's scope or evidence. |
| 2026-09-17 | T4n-p (unit 29): function-terminal partial-path seeds are recorded per function and joined by the enclosing selection or scope; labeled partial transfers are recorded as unknown (omitted from selection joins, guarding scope joins); the scope proof's completing-selection restriction was removed; a T4n-q completing-loop slice was reverted after producing a spurious error; the previous execution's evidence root was found deleted and the reproducer rebuilt | A partial early transfer followed by later termination is a terminal path of the scope; discarding it substituted a single path for the required join, and the same omission let function-level terminal selections miss dead-source diagnostics. Recording every terminal branch makes selections ordinary children of the proof, which deletes code instead of adding a second proof. |
| 2026-09-17 | Execution stopped at 01:38 JST (about 35 of the 60 budgeted minutes) with T1a recorded as the next I3 item rather than started | Modifier recognition across every declaration form, a new catalog diagnostic, tests and two full-suite verifications were estimated to exceed the remaining window, and a partially implemented modifier grammar would leave the tree incoherent; the workspace holds only the verified T3a change. |

Out-of-scope findings: general LSP/editor integration, KimiCode configuration, debug information, additional target profiles, remote registries/network publication and NuGet distribution automation are not necessary to implement the finalized compiler contracts here. Existing CI improvements may be extended only as needed for conformance evidence. Public APIs omitted by the specification belong in separate design work. No unrelated fix, draft edit, or package publication is authorized by this plan. The implementation execution changes and evidence are recorded above. The original extended front-end benchmark input failure remains an out-of-scope finding (unit 2); its common workload is usable.
