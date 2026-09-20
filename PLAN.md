# Kimigayo Compiler Completion Plan

This is the entry point for implementation resumption. [STATUS.md](STATUS.md) describes product support; [PLAN_HISTORY.md](PLAN_HISTORY.md) holds execution evidence and superseded decisions. [SPEC.md](SPEC.md) and its normative chapters define required behavior.

Read [§1](#1-goal-and-scope), [§2](#2-execution-state), and the relevant [§5 issue](#5-gaps-and-decisions), then only the owning specification, milestone design and test procedures needed for the selected item. Current states and next actions have one home: §2. Historical deadlines never authorize or constrain a new execution.

## 1. Goal and Scope

### Baseline

Complete the compiler for all finalized language rules and implementation contracts in `SPEC.md`, Chapters 1–22, and normative Appendix A. Completion means correct acceptance, required rejection and warnings, ownership verification, checked generation, artifacts, and execution on the specified Windows x64 profile. Parsing or successful LLVM verification alone is insufficient.

The compiler-wide plan covers unfinished implementation Milestones and Checklist items, respecting dependencies and finalized specification boundaries. The current focused request is to review the independent documentation parser, fix defects and make measured improvements. Product migration and formal specification integration are complete. Earlier timed compiler continuations are historical, not current authorization. NativeAOT and draft edits remain excluded.

This file owns the current plan. Its baseline must not be weakened to match implementation limitations. Product-wide support belongs in `STATUS.md`; detailed execution history belongs in `PLAN_HISTORY.md`.

### Authority and boundaries

1. `SPEC.md` identifies the owning chapters. §1.3 makes unqualified rules normative, distinguishes mandatory requirements from recommendations, and says examples do not add rules.
2. Appendix A is normative verification/implementation guidance. Appendix B is optional algorithms; Appendix F is a non-normative syntax summary. `STATUS.md`, code, tests, and program milestones are evidence, not language authority.
3. Use the formal owning sections for requirements, including §21.3 for generic sharing/specialization, Chapter 18 for dependencies/artifacts and §17.5 for verification operations. Proposals and historical precedence notices do not override the formal specification; do not propagate formal changes back into draft files.
4. Appendix D and owning sections delimit exclusions. “Specified, not implemented” remains in scope. “Deferred design” and undefined public APIs do not become requirements merely because syntax/examples exist.
5. Specifically include settled object/Weak operations (§13.5.8–9), collection mutation (§4.7), source packages/local publication (§18), and basic test semantics. Old implementation comments calling these unspecified do not supersede the specification.
6. Exclude Composition Root Entry/Provider selection, runtime Contract Views, source concurrency, extra target profiles, stable external Kimigayo/DLL ABI, source transparent aliases, general user-defined arithmetic, extra generic/specialization forms, persistent generation/object-code caches, dynamic generic scratch allocation, and other Appendix D extensions.
7. Exclude undefined raw-storage acquisition/allocation/reference-conversion APIs (§5.6), checked-cast/exact-test public spellings (§13.6.2), and string concatenation acquisition (§13.3). Preserve their settled constraints without inventing executable syntax. The mention of concatenation in §22.1 does not override §13.3's explicit executable-finalization prohibition. Interpolation and its Stringify contract are independently specified and included.
8. `Kimi.Sealed` and `Kimi.Intrinsics.replace/exchange/swap` are adopted requirements (§15.7); the earlier exclusion of exchange spelling is superseded. Declaration Container nesting is adopted under §6.1.1, §8.4.9, §9.6.1 and §22.2.4. R3/R5/R6/R7/R10/R11/R19/R23 and their existing I/T families cover these requirements, including Appendices A.18–A.20; implementation limits do not exclude them.
9. ObjectCallCompatible inference/publication and release checking remain explicitly deferred by [SPEC Appendix D.5.1](spec/appendices/D-deferred-features.md#objectcallcompatible) pending further instructions. Preserve the specified call rules; I7/I24 must not implement those deferred stages from an old execution instruction.
10. Undefined Mod host interfaces/configuration remain separate design work (G1); G2 test interfaces are now adopted in spec/testing-profile.md. Their settled semantic rules remain baseline requirements; dependent public integration cannot be declared complete by inventing interfaces. Dynamic collection/Slice internal representations may be proposed within settled contracts; a fixed public collection ABI is excluded (G3).

Performance is a first-class constraint: minimize allocations, avoid repeated work, bound graphs/worklists and retained memory, reuse established storage, and measure compile/runtime costs. Do not promise universal zero allocation or throughput improvements without evidence. Language-mandated allocation/complexity bounds are acceptance criteria (R18), not optional tuning.

## 2. Execution State

Current focused checkpoint: **DM6 — parser review complete**. Fixed emphasis-run matching, NUL interpretation, empty-link depth and recursive node formatting; reduced temporary URL-encoding allocations. Debug/Release builds are warning-free and all 10,589 managed tests pass per configuration, including 28 new cases and a 10,000-input official-output digest. No remaining action for this review. [Findings and verification](PLAN_HISTORY.md#documentation-markdown-review-20260920), [measurements](Benchmark/DocumentationMarkdown.md#dm6-parser-review-2026-09-20).

**DM5 remains complete**: the product uses the independent parser and Markdig is confined to comparison tests/benchmarks. Previous Debug/Release builds and all 10,561 managed tests (1,351 documentation cases) passed per configuration. See the [product API](Kimi/Compiler/Documentation/README.md), [output measurements](Benchmark/DocumentationMarkdown.md#dm5-product-output-and-switch-2026-09-20) and [execution evidence](PLAN_HISTORY.md#documentation-markdown-product-switch-20260920). General compiler items below remain backlog.

### Documentation Markdown migration

| ID | State | Scope, acceptance and exact next action |
| --- | --- | --- |
| DM1 | DONE | Full English rules and examples integrated into the formal Markdown profile, §2.3.4–6 and A.21; SPEC indexes their authority. Replaced draft dependencies and duplicate definitions with formal cross-references; proposals remain unchanged. |
| DM2 | DONE | Independent immutable `DocumentationMarkdownDocument` API, arena nodes/source slices, pinned Unicode 15.0.0, iterative blocks/delimiters, candidate extraction and role-aware declaration-name classification. Warning-free Debug/Release builds and 155 passing targeted documentation tests per configuration. Existing Markdig implementation/dependency preserved. |
| DM3 | DONE — implemented parser API | 372 official CommonMark cases and 372 Markdig comparisons; explicit accounting of 280 profile exclusions, every §2.2 difference, 568 generated interactions, source/Unicode/resource/concurrency tests, and test adapters consuming real Binding parameter/receiver information. One link parsing defect corrected. Warning-free builds, 947 documentation and 10,157 full managed tests per configuration, plus two native O0/O2 executions per configuration. [Matrix and reproduction](xUnitTest/TestData/DocumentationMarkdown/README.md). This is bounded verification, not exhaustive conformance or validation of the unimplemented renderer/resolver. |
| DM4 | DONE — implemented parser API | Paired Markdig and DM3/final measurements, 32 scaling inputs, item/query/source mapping, retained-result, first-call and concurrency/interruption probes recorded with raw samples and hashes. Plain paragraph construction, contiguous code slices and single initial extraction reduce measured costs. Pre-tuning gates pass: no allocation regression or >10% timing regression in six representative parse cases; >10% geometric-mean improvement; largest approximately 4× inputs cost <6× time; cached summary/candidate/mapping operations allocate zero. Full Debug/Release regression passes. [Method, limitations and reproduction](Benchmark/DocumentationMarkdown.md). Second tuning round: `<` inside bare destinations fixed; stack-only parser, 36-byte nodes, text merging, lazy destinations, NUL-free search needle; tuned/DM4 geometric means 0.669 time / 0.663 bytes with 12 added regression cases. |
| DM5 | DONE | Product facade switched; no Markdig types/dependency in compiler. Independent iterative rendering, structured source/output link components, current Binding role classification and source-mapped optional item diagnostics are connected. All Debug/Release tests pass; 320 official non-link output examples and explicit URL/role/cancellation/reentrancy checks verify the product path. Same-output seven-sample comparisons and render scaling meet the recorded gates; fresh non-AOT publish and CLI smoke test pass. [API migration](Kimi/Compiler/Documentation/README.md) and [evidence](PLAN_HISTORY.md#documentation-markdown-product-switch-20260920). No remaining migration step. |
| DM6 | DONE | Reviewed block/inline parsing, positions, items, publication and output. Corrected four defect families, added focused and deterministic regression coverage, and measured UTF-8 URL encoding against the pre-review assembly. Full Debug/Release managed regression passes; no new runtime dependency. |

Product rendering, structured URL mapping and declaration classification now consume the independent snapshot. Optional extension-like writing diagnostics and cross-comment caching are not provided; they are not required migration steps. The default parser tree-depth limit is 256, caller-configurable; exceeding it raises `DocumentationMarkdownLimitException` (a documentation interruption), never a syntax fallback. All public ranges are half-open UTF-16. Cancellation does not publish partial results. `GetItemCandidates` shares a completed snapshot-local result; declaration-dependent classification is separately supplied and not cached on syntax alone.

### Earlier program checkpoint

| ID | State | Acceptance / exact next action |
| --- | --- | --- |
| P16-O | DONE | The unchanged target passes Binding and ownership. Substituted enum payload dependencies retain both external sources; escaping-source and conflicting parent/source access are rejected. |
| P16-G | DONE | Enum layout, directed reference payload fitting and canonical call-Origin intersection substitution pass checked LLVM emission, verification, linking and native O0/O2 execution. |
| P16-V | DONE | Warning-free Debug/Release solution builds; Debug full 9,151 plus the extended 13-case suite, Release full 9,157; 57 native target/variant/rejection checks per configuration and 90 Release regression checks for programs 1–15. Exact output, exit, stderr and source mutations verified. |

Programs 1–16 have bounded completion; Programs 17–21 informed design only. General M/I obligations remain open as compiler backlog, separate from the focused documentation parser request. NativeAOT and draft edits remain excluded.

### Current milestone states

| ID | State | Completion evidence or remaining work |
| --- | --- | --- |
| M1 | DONE | Historical I1 baseline/I2 mapping; [baseline](PLAN_HISTORY.md#units-1-32), [clause map](PLAN_HISTORY.md#appendix-a-baseline). Original evidence root is absent; later matching evidence has narrower explicitly listed coverage. |
| M2 | IN_PROGRESS | I3 logical Never fitting is implemented with focused evidence; final clause closure, I4 general inference/call/default plans and I5 Contract/projection/library obligations remain. |
| M3 | IN_PROGRESS | Historical am–as verification is extended by at–ax implementation for terminal guards and finite enum/owned-string checking. Regression gates remain open; general backedges/continue, unequal Loan joins, deferred cleanup, effectful divergence, effects and general defaults remain. |
| M4 | IN_PROGRESS | Concrete struct/borrow, bounded owned receivers and pure scalar static slices exist; inherited layout, accessors and general static lifetime remain. |
| M5 | IN_PROGRESS | Concrete/symbolic enum storage is extended by owned string decomposition and nested UTF-8 literal Patterns with focused evidence. Full borrowed decomposition and user cleanup remain. |
| M6 | IN_PROGRESS | Programs 8/9/11 shared storage, lengths, forwarding and selected Type specialization exist. G10a–h add concrete free/member/constructor calls, checked integer operations/conversions, scalar result joins, owned strings/output and Abort/Never in shared bodies. Full universal proofs, operations and budgets remain. |
| M7 | IN_PROGRESS | Program 9 common calls/inline scalar captures exist; general captures, function items, witnesses and erasure remain. |
| M8 | IN_PROGRESS | Built-in range, bounded Slice views/reference iteration, explicit non-lending Iterator calls and Copy-array iteration subsets exist; full APIs and general user protocol dispatch remain. |
| M9 | TODO | Dynamic collections, Stringify/interpolation and comparison Contracts remain. |
| M10 | IN_PROGRESS | Concrete makeObj/obj ownership, Sealed payload access, exchange and destruction/free execute for P14. rc/arc/Weak, views and general refinement remain; ObjectCallCompatible stages are deferred. |
| M11 | IN_PROGRESS | T26a–c validate `#LibraryImport` arguments, requirement ownership, declaration shape/placement, reserved external names, §22.3.2 signature Types and same-symbol physical signatures in Binding. Provider/dllimport agreement, generated-definition collisions, lowering, linking, pointer operations and C layout execution remain. |
| M12 | IN_PROGRESS | Project graph/locks and T28a/T28b supported source-module common generation/Library inspection exist. Package/store, full native connection and persistent semantic reuse remain. |
| M13 | IN_PROGRESS | Test semantics and Windows runner are implemented for supported backend forms; I30/I31 retain cross-feature prerequisites. |
| M14 | BLOCKED | G1 blocks the public host slice; settled lifecycle audit can proceed independently. |
| M15 | IN_PROGRESS | Full conformance/performance/delivery closure remains. |

M4/M5/M7 are reconciled from stale TODO labels to IN_PROGRESS because recorded completed subsets already exist; no acceptance criterion or unfinished scope was closed by this migration.

### Current implementation items

| ID | Milestone / requirements | State | Completion evidence or remaining outcome |
| --- | --- | --- | --- |
| I1 | M1 / R1–R28 | DONE | Historical baseline; [V1–V5 record](PLAN_HISTORY.md#units-1-32), original logs absent. |
| I2 | M1 / R1–R28 | DONE | Historical Appendix A.1–A.17 mapping; [I2 record](PLAN_HISTORY.md#appendix-a-baseline). Extend clause audit for adopted A.18–A.20 under I33. |
| I3 | M2 / R1–R3, R27 | IN_PROGRESS | Historical T1a, T3a, T5a/G9, T27a/T27b and T7a slices exist; T4n-ax adds logical Never fitting. Final clause/regression closure remains; the historical 250-block parse/SpecTour audit (SpecTour since removed) is not a current whole-spec certificate. |
| I4 | M2 / R4 | IN_PROGRESS | Selected omitted-default plans and independent scalar/Unit declarations execute; T4o–q add guarded/Copy-tuple defaults. T4r verifies candidate-local tuple/array literals, nested structural Type/length/Origin evidence, preserved ambiguity and shared temporary acquisition. Exact prepared-slot validation is retained. General contextual nested calls, recursive defaults, owned/borrowed defaults and pending-slot cleanup remain. |
| I5 | M2 / R5, R19 | IN_PROGRESS | T5a/G9 resolved; 14/31 Kimi declarations validated, including Iterator and Slice identities; this does not certify their entire APIs. Remaining Contract/projection/certificates, bound associated/refinement composition and canonical library identities remain. |
| I6 | M3 / R6–R7 | IN_PROGRESS | T4n-at–ax add bounded terminal/Abort/divergent guards and finite enum/owned-string mixed histories, ending abandoned protection at replay endpoints. T4r/T6a/T6b add tuple borrow lifetime roots, concrete scalar-field updates and stored aggregate projections with immutable-local ancestry. T6d–T6j add returned-reference ancestry, §15.6.2 disjoint/shared projection checks and nested inline borrowed paths/borrows; T6i adds in-place borrows of owned-local/parameter inline parts with static-path footprints and closes element access under a live exclusive root Loan; T6k rejects exclusive reborrows of stored `uniq` references through `ref` bases in Binding; T6m binds scalar Place shorthand borrows; T6n/T6o materialize scalar temporaries for implicit shared argument borrows and explicit `@ref`/`@uniq`. T6s adds disjoint sibling Moves before/during a live inline-part borrow with initialized-subtree validation and checked cleanup; T6t retains single-input returned-reference argument footprints. General backedges/continue, unequal Loan joins, deferred cleanup, effectful divergence and general Origins remain. |
| I7 | M3 / R7, R20 | TODO | Compute effect-family fixed points/public guarantees and dependent use checks. ObjectCallCompatible stages 2/3 remain deferred by SPEC.md; other settled effect work remains in scope. |
| I8 | M3 / R8–R9, R27 | IN_PROGRESS | Bounded defaults retain the historical guarded-pattern/Copy-tuple scope. New terminal match guards preserve abandoned protection and selected binding lifetimes. General escaping-Borrow/capture/Copy proofs, owned defaults, refinement and effectful/deferred joins remain. |
| I9 | M4 / R10 | IN_PROGRESS | Concrete construction/destruction, synthesized construction and bounded borrowed/owned receiver subsets exist; G10g verifies owned-field Move and remaining-field cleanup through concrete methods. Complete inherited/base-layer layout, construction, destruction and general receiver support. |
| I10 | M4 / R11 | TODO | Connect standard/custom/computed/Contract Property operations through ownership and generation; declaration certificates alone do not execute accessors. |
| I11 | M4 / R11, R24 | IN_PROGRESS | Pure immutable integer/bool group literal reads execute. First-access mutable/effectful static storage, address identity, cycle/shutdown rules and inherited-environment storage keys/certificates remain. |
| I12 | M5 / R12 | IN_PROGRESS | Concrete/finite symbolic enum storage is extended by owned string tuple/enum decomposition, Move cleanup/results and exact nested string literal Patterns. Focused checks cover these paths; borrowed composite candidates/Subjects, user destructors and full Pattern/regression closure remain. |
| I13 | M6 / R5–R7, R13 | IN_PROGRESS | Conditional stored-field CopyOrMove, length-dependent storage and finite symbolic enum checking exist; broader universal operations/effects remain. |
| I14 | M6 / R13 | IN_PROGRESS | Closed Type specialization selection/forwarding exists. Complete length/receiver/constrained/defaulted headers, explicit Origin binders and every retained selection path. |
| I15 | M6 / R14 | IN_PROGRESS | Shared storage/selection/length bodies, concrete entries, fixed frames and bounded forwarding exist. G10a: shared bodies call ordinary concrete free functions (including Unit results) through their verified ABI, and bool/8–64-bit signed and unsigned integer leaves support checked `+ - * / %`, unary `- + not`, all comparisons and callback results. G10b/c add bool/integer Phi joins, owned string storage/output and concrete/generic forwarded string results. G10d–f add canonical Abort/require, Never calls/entries and bitwise/checked shifts. G10g/h add supported concrete constructor/receiver calls and checked 8–64-bit integer conversions. General compound fields/calls, broader receiver layouts/defaults, borrowed strings, floating-point/128-bit/character operations and resource contracts remain unfinished. |
| I16 | M6 / R14, R28 | TODO | Add deterministic finite resource limits and bounded optional optimization; measure effects. Existing depth limits are not complete resource-contract coverage. |
| I17 | M7 / R15 | IN_PROGRESS | P12-B/O/G adds bounded concrete, mutable, nested and consuming environments. P14 adds supported ref/uniq and obj captures with dependencies and call effects. Complete general aggregate/generic captures, result inference, receiver effects and wider erasure/ABI coverage; program completion does not close R15. |
| I18 | M7 / R7, R14–R15 | IN_PROGRESS | Checked common calls, inline Copy-environment erasure and a bounded concrete exclusive Callable witness execute. P14 adds direct per-call ref/T inputs and an exclusive borrowed-capture witness. General function-item erasure, owner/Origin-polymorphic witnesses and wider indirect-call ABI remain. |
| I19 | M8 / R16, R19 | IN_PROGRESS | Named Slice Types/backing Origins, full and explicit half-open views, scalar Copy reads, explicit element borrows and metadata exist; general Index/Range/Slice APIs, broader provenance and prerequisite library witnesses remain. |
| I20 | M8 / R17 | IN_PROGRESS | Built-in ResolvedRange/shared range, supported Copy-array and Slice reference iteration exist. Program 13 explicitly calls a checked Iterator.next returning externally borrowed Option payloads; general recognized Iterable/Iterator dispatch, consuming adapters and tuple bindings remain. |
| I21 | M9 / R18 | TODO | Implement Array capacity/mutation/owning iteration with mandatory cost bounds. |
| I22 | M9 / R18–R19 | TODO | Implement Dictionary literal/mutation/equality/order/borrow contracts and allocation-free churn. |
| I23 | M9 / R19 | TODO | Complete comparison/Stringify witnesses and interpolation, documenting float formatting. |
| I24 | M10 / R20 | IN_PROGRESS | P14 implements concrete makeObj, obj metadata/header/allocation, complete Sealed payload projection, exchange and dynamic destruction/free. Complete views, shared/atomic strong ownership and refinement. |
| I25 | M10 / R20 | TODO | Implement Weak migration/upgrade/release/cyclic construction and atomic ordering proof. |
| I26 | M11 / R21, R24 | IN_PROGRESS | T26a: two plain nonempty NUL-free string arguments and a requirement (NativeRequirements or self-targeted NativeLibraries) of the defining module for the current target; reserved kernel32/kimi_backend exempt. T26b/T26e: bodyless `unsafe` group/receiverless struct type functions only, no own or enclosing generic/Origin parameters, specializations or default/optional arguments, and §21.5.2 reserved external names rejected. T26c: parameters/results limited to i8–i64, u8–u64, f32, f64 and `unsafe/T`, with Unit only as a result (`UnsupportedImportSignature_Kd`). T26d: one compilation-wide physical signature per external symbol (i8/u8 etc. and all `unsafe/T` share codes; `ConflictingImportSignature_Kd`). Remaining: provider/`dllimport` agreement after supply resolution and collisions with runtime/generated definitions (§21.5.2), direct-call lowering with `dllimport`/static kinds, supply/symbol validation, pointer semantics and C layout execution. |
| I27 | M12 / R22 | TODO | Implement package integrity/loading/packing/local publication/store lifecycle using fixed bytes. |
| I28 | M12 / R22, R24 | IN_PROGRESS | T28a supported source-module common generation and T28b Library inspection output are verified. T28c validates NativeRequirements and self-targeted NativeLibraries map records (Kind required after expansion, ContractId/Sha256 agreement, kimi_backend Sha256 assertion) at load and emission and diagnoses NativeBindings; T28e rejects repeated target/name entries that the dictionary formatter would silently overwrite. Remaining: `Name`/`Input` array records and `Package`-targeted supplies, module/native input records in manifests, actual supply hashes/kinds and requirement/supply connection validation. |
| I29 | M12 / R23, R28 | TODO | Implement verified semantic records/correspondence/invalidation with cold/warm equivalence. |
| I30 | M13 / R25 | IN_PROGRESS | Discovery, all-body checking, expect/require parsing, ownership and concrete/shared lowering are implemented. Complete cross-feature generation and independent product/test resource plans after I11/I16/I28. |
| I31 | M13 / R25 | IN_PROGRESS | Windows x64 isolated runner, solution scheduling, finite budgets, JSON/text results and retention are implemented. Cancellation, missing completion and descendant recovery are verified. Complete cross-feature lifecycle coverage after compiler prerequisites; see current next actions. |
| I32 | M14 / R26 | TODO | Audit/test settled Mod lifecycle; public host integration BLOCKED by G1. |
| I33 | M15 / R1–R28 | IN_PROGRESS | T6v verified scaled live-Loan snapshot reuse and bounded timing/allocation evidence; complete Appendix A coverage and cross-feature performance/conformance evidence remain. |
| I34 | M15 / R27–R28 | IN_PROGRESS | T34a updated source-dependency usage and added a verified transitive Application example. T34b documents NativeRequirements/self-targeted NativeLibraries usage and current import limits in README; both snippets are loaded by tests. Complete remaining English support/spec-choice/usage records and examples; retain honest residual boundaries. |

### Completed slices and evidence

| Stable IDs | Retained disposition | Evidence / scope |
| --- | --- | --- |
| T1a, T3a, T5a, T7a, T27a, T27b; units 1–6 | DONE for recorded diagnostic/invalidation slices | [Units 1–32](PLAN_HISTORY.md#units-1-32); original evidence absent. |
| T4a–T4m; units 7–19 | DONE for recorded default slices | Same history; full general I4/I8 scope remains open. |
| T4n-a–s; units 20–32 | DONE for bounded continuation slices | Same history, including reverted T4n-q attempt and its later replacement. T4n as a family remains IN_PROGRESS. |
| T4n-t–v; units 33–35 | DONE | [Mixed-target replay](PLAN_HISTORY.md#execution-4). |
| T4n-w–ac; units 36–42 | DONE | [Scope/logical joins](PLAN_HISTORY.md#execution-3). |
| T4n-ad–ah; units 43–47; catalog prerequisite | DONE | [Partial-terminal joins](PLAN_HISTORY.md#execution-2); `ah-*` superseded by corrected `ah2-*`. |
| T4n-ai–al; units 48–51 | DONE | [Pre-program-12 verification](PLAN_HISTORY.md#execution-1); historically matched saved 8,507 managed tests/configuration, 861 fixtures / 1,722 O0/O2 executions, 984 program checks. |
| Programs 1–8 | DONE for each target | Program 7/8 historical records and STATUS history; latest program regressions are in the matching final audit. |
| P9-L1–L4, P9-V, P9-C1/C2, P9-G1–G6, P9-G, P9-F | DONE for target/subset criteria | [Program 9 completion](PLAN_HISTORY.md#execution-6); earlier P9-C umbrella replaced by C1/C2 and G1–G6, tracked here as DONE for its target scope only. |
| P10-1, P10-2, P10-3, P10-V | DONE for target criteria | [Program 10](PLAN_HISTORY.md#execution-8). |
| P11-S, P11-G, P11-P, P11-F | DONE for target criteria | [Program 11](PLAN_HISTORY.md#execution-5). |

Programs 12–14 retain their recorded target completion; §2 separates the current authoring request from executable support. Program numbers are independent of M1–M15; earlier references to M1–M17 are historical numbering errors, not two missing milestones.

### Current next action

1. Continue I26 with §21.5.2 provider and runtime/generated-definition collision checks and direct-call lowering (physical signature shared between declare and call, >4-argument Windows x64 ABI) of `#LibraryImport` functions, then an ordinary native test against a static `.lib` supply; this also provides the first real native requirement for I28 connection validation. Independently, I28 T28d: replace the per-target `Dictionary<string, NativeLibraryInput>` with a type implementing Tinyhand 0.147 `ITinyhandSerializable<T>` that reads either the map shorthand or an array of `Name`/`Input`/`Package`/`ContractId`/`Sha256` records (serializing canonically), keep T28e duplicate checks for both forms, reject `Kind` on Package-targeted records, and validate Package-targeted supplies against the resolved dependency module's requirements (ContractId match, no requirement creation) once the graph is prepared; then add module-scoped native input records to the manifest. I11 dynamic static initialization/shutdown is independent; I6 retains general Origins, joins and backedge work. No Milestone Program is authorized by this checkpoint.
2. Complete independent product/test optimization and resource plans (I16/I30), reusing product entries without enlarging their frames or sharing classes.
3. After those prerequisites, extend T25/T26 to initialization/shutdown failures and complete product-plan isolation; retain the adopted interfaces and finite limits.

Earlier T4r/T6/G10 execution details and the T6l/P14-L1 backlog rationale are in [the preceding checkpoint](PLAN_HISTORY.md#compiler-checkpoint-before-test-implementation-2026-09-19). T6l remains a separate ownership item, not the next test-runner action.

## 3. Current State and Architecture

Evidence levels are separate: parsing, final Binding, control-flow/ownership analysis, checked IR generation, LLVM verification, native execution, and performance measurement. Code inspection establishes a path or guard; test existence establishes intended coverage; a saved PASS establishes only its recorded inputs. Unknown proof never means success.

| Existing path | Responsibility / resumption guidance |
| --- | --- |
| `Kimi/Unit/CommandUnit.cs`, `CommandExecution.cs`, `Solution.cs`, `Project.cs` | CLI dispatch, input resolution, graph/lock preparation and target operations. Registered source commands include build/check/restore/emit/run/test; package/store commands remain absent. |
| `Compilation.cs`, `Compilation.Modules.cs`, `SourceDocument`, `Kotonoha.AddSource` / `ParseSource` | Prepared module environments and immutable source snapshots; parsing/reload invalidates prior semantic certificates. Source-module loading exists. |
| `Binding.Bind`, `Binding.CheckBound`, `BindingModel.cs`, `Binding.*.cs`, `KimiLibrary.cs` | Semantic fields and retained operations on Koto, reusable lookup/proof tables and selected calls. No separate Bound Tree; no lookup/reselection in lowering. |
| `ControlFlowAnalysis`, `StructuralCompletion`, `OwnershipAnalysis`, `OwnershipBody`, `OwnershipModel.cs` | Separate runtime reachability and source checking, places/Loans, defaults, acquisitions, results and cleanup. Extend these records and their validation. |
| `LlvmEmitter`, `GenericStoragePlan`, `FunctionAbiPool`, `AggregateLayoutPool`, `BodyLowering` | Recheck current semantic/startup/ownership state across source modules, then lower supported concrete/shared plans. Supported nested containers, scalar defaults and bounded closures/generics share final generation; Library inspection omits the OS entry. Unimplemented bodies and Library native build/run remain rejected. |
| `EmissionModule.Complete`, `LlvmModuleWriter`, `WindowsLowering` | Validate syntax-free physical records before LLVM writing; no output after failed validation. Retained modules are scratch, not reusable certificates. |
| `EmissionArtifacts`, `NativeToolchain`, `ToolchainResolver`, `Kernel32Imports`, `WindowsRuntime.ll.in` | Staged artifacts, integrity/tool/profile checks, native linking, bounded processes, startup/output/destruction/Abort. Windows x64 profile pins LLVM 22.1.8 and backend ABI 2. |

See [STATUS.md](STATUS.md) for the area-by-area support boundary. The [original architecture audit](PLAN_HISTORY.md#old-architecture) retains obsolete blanket guards and original identifiers for traceability; it must not be used to reimplement completed slices. Source reload reparses saved documents and rebuilds proofs; persistent semantic reuse remains I29.

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

The [I2 baseline mapping](PLAN_HISTORY.md#appendix-a-baseline) covers the then-current A.1–A.17 only. Adopted whole-value replacement, library/alias and container requirements in A.18–A.20 remain mandatory under the R families above; I33 must complete their clause-level mapping and uncovered cases.

## 5. Gaps and Decisions

| ID / classification | Evidence and affected requirements/milestones | Resolution / effect |
| --- | --- | --- |
| G1 `SPEC_GAP` | §20.7.7 and D list concrete Mod query/marker APIs, packaging/compatibility, settings and host limits as design work. R26/M14 | Separate public API/host design must define these before external implementation. Retain existing append/rebind infrastructure and verify settled lifecycle rules internally. Do not treat illustrative `IMod`/`ModContext` as adopted interfaces. |
| G2 `SPEC_GAP` — RESOLVED | Adopted test profile in spec/testing-profile.md; R25/M13. | Implement I30/I31 against the explicit interfaces and limits; top-level runtime bodies are checked but not executed, and test startup requires no product entry. |
| G3 `INVESTIGATION_GAP` | D's collection storage entry versus settled §4.6.8/§4.7 complexity/storage freedom. R16–R18/M8–M9 | Public storage ABI remains excluded. Proposed internal Slice location/length and Array contiguous storage are implementation choices, subject to layout/Loan/zero-size validation. Proposed Dictionary indexed slots/free-list/order links allow allocation-free churn; benchmark before optimizing search. No user approval is needed for ordinary private layout choices within these contracts. |
| G4 `IMPLEMENTATION_MISMATCH` | Kimi catalog omits required Weak and leaves object operations missing with a stale deferral comment, despite §22.1 and §13.5.8–9. R19–R20/M2, M10 | Add canonical declarations/identities with their implementation milestones; update catalog tests intentionally. Do not make `IsCompleteLibrary` true just by changing a count or weakening required shapes. |
| G5 `INVESTIGATION_GAP` — RESOLVED | Earlier NuGet.Config help-probe denial | Fresh restore/builds resolved the recorded environment condition; [original diagnosis and commands](PLAN_HISTORY.md#old-decisions). No user configuration was edited. |
| G6 `INVESTIGATION_GAP` | Older evidence roots are absent; the migration matched the then-current frozen inputs; program-12 results have their own source/artifact identities. Only each record's scoped checks are verified. See [evidence audit](PLAN_HISTORY.md#migration-audit). R1–R28/M1, M15 | Reproduce focused baselines, expand clause coverage using Appendix A before each milestone. Unknown support needs a targeted source reproducer/code trace, not “not implemented” from a failed search. |
| G7 `INVESTIGATION_GAP` | `AggregateLayoutPool` uses int offsets/counts, depth 64 and rejects size > int.MaxValue; §21 defines broader checked layout/resource rules. R10/R14/R16/M4, M6 | Determine which are documented resource limits versus unnecessarily restrictive representations. Use checked wide arithmetic internally where needed; keep finite limits explicit and distinguish semantic, resource and representation diagnostics. Do not allocate giant fixtures to test overflow. |
| G8 `IMPLEMENTATION_MISMATCH` — documentation reconciled | Stale STATUS omitted struct/borrow/module support and conflated CI workflows | Corrected in this migration using recorded subsets and code inspection. Historical wording is retained; comprehensive support audit remains I34. |
| G9 `IMPLEMENTATION_MISMATCH` — RESOLVED (T5a) | Unresolved-conformance diagnostic cascades, R5/R27 | Recorded fix retains failed proof states and independent errors; [exact original diagnostics and rationale](PLAN_HISTORY.md#old-decisions), [unit 3 evidence](PLAN_HISTORY.md#units-1-32). |

Container bound associated/refinement identity, conditional refinement-path merging, inherited conformance composition, declaration-path cycles, abstract Origin bounds, A.20 outer-edit invalidation and inherited-environment static lifetime remain incomplete (I5/I6/I11/I29/I33). The embedded-source catalog has 30 individual declaration entries, 13 validated; 17 missing declarations remain G4. Ownership APIs are tracked individually; the family summary is separate. Source organization does not complete their semantic or runtime requirements. Iterator/Slice declaration identity does not close their full API requirements.

No unresolved **SPEC_CONFLICT** was established by this investigation. Contradictory status text or stale code comments are not specification conflicts. If two applicable normative rules remain contradictory after owning-section/supersession review, add a SPEC_CONFLICT row with both clauses and block only dependent work.

Decisions/proposals:

- D1: Extend Koto semantic fields and existing reusable Binding/ownership/physical plans. No new Bound Tree or mandatory intermediate compiler stage.
- D2: First complete correctness and a valid baseline sharing plan; optional specialization/tuning cannot change selected implementations, caller ABI, checks or acceptance.
- D3: Start persistent reuse with conservative module invalidation; refine only with complete dependency evidence and measured benefit. Source reparse/rebind is the correctness oracle.
- D4: Internal resource/budget defaults and float Stringify spellings are documented implementation choices where explicitly permitted. Record chosen values and measurements at M6/M9; they are not missing language rules.
- D5: Programs `milestones/Milestone7.kimi`–`Milestone9.kimi` are integration targets, not the definition of compiler completion. They depend respectively on M8; M6+M8; and M5–M8 plus general borrowed storage. Preserve their source and expected behavior.

No user decision is needed for documentation reconciliation. Before implementing excluded Mod interfaces, request the concrete G1 decisions. G2 is resolved by the adopted test profile.

## 6. Milestones

The milestone designs below define the authorized scope; their current execution states are only in Section 2. Directory names refer to existing directories. Existing files/symbols are reused unless explicitly marked **new**. A family is complete only when its remaining finalized input range is supported and its mandatory negative boundaries still reject. Tests of known behavior are regression gates, not redundant implementation tasks.

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
- Files: `Binding.cs`, `Binding.Expressions.cs`, `Binding.Calls.cs`, `Binding.CandidateEvaluation.cs`, `Binding.ArgumentOperations.cs`, `Binding.Contracts*.cs`, `Binding.Constraints*.cs`, `Binding.Properties*.cs`, `KimiLibrary.cs`, relevant Koto/parser/diagnostic files only when syntax or diagnostics actually require changes.
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
- Files: existing generic/constraint/Origin Binding files, `OwnershipModel.cs`, `LlvmEmitter.cs`, `AggregateLayout.cs`, `FunctionAbiPool.cs`, `EmissionModule.cs`; existing `GenericStoragePlan.cs` and its policy/entry/frame records under `Kimi/Compiler/Emission/`; the earlier proposed `GenericGenerationPlan.cs`, `GenericContextSchema.cs`, `GenericFramePlan.cs` names are superseded by that implementation, with all required key/schema/frame obligations retained.
- Work: I13–I16. Verify symbolic Copy/Move, effects and cleanup universally; close explicit-specialization sets; form semantic/selection/body/entry/context keys with distinct roles. Worklist closed substitutions; substitute verified plans without cloning/rebinding syntax. Validate immutable 8-byte GenericContext slots and operation/context pairs, fixed facts, finite recursive graphs, caller-facing ABI and exact per-entry scratch reservation.
- Preserve: explicit selection at budget zero, Origins in proofs even when erased from representation keys, full-width Type tokens, no dynamic scratch layout/alloca/heap fallback, no optimization-induced acceptance change, separate mandatory resource limits.
- Complete: T13–T15/T27 and M5's remaining generic T12 cases via V3–V6/V9. Baseline shared code and adapters must work before bounded optional specialization; measure generation/code-size/runtime tradeoffs. Adding another instance cannot inflate an existing exact frame.
- Risk: exponential key growth or schema/ABI confusion. Require deterministic bounded worklists and separate diagnostics for invalid recursive inline layout versus finite cycles versus required resource exhaustion.

### M7 — Closures, function items and callable values

- Objective: R15, with R7/R14/R19 contracts.
- Dependencies: M2–M4; M6 for generic Callable witnesses, common contexts and concrete instantiations.
- Files: `FunctionKoto.cs`, `Binding.Expressions.cs`, `Binding.CallableContracts.cs`, `Binding.FunctionWitnesses.cs`, `OwnershipAnalysis.*`, `FunctionAbi.cs`, `AggregateLayout.cs`, `EmissionModule.cs`; existing `Binding.Closures.cs`, `OwnershipAnalysis.Closures.cs`, `BodyLowering.Closures.cs` and `LlvmModuleWriter.Closures.cs`; earlier proposed `BodyLowering.Callables.cs` / `CallableLayout.cs` are design suggestions, not a requirement to duplicate current closure plans.
- Work: I17–I18. Capture acquisition/environment layout, concrete receiver kind/Copy, function-reference acquisition, indirect call entry/context, per-call Origins and common Function storage/adapters.
- Preserve: capture order and unused explicit captures, no shared-to-exclusive upgrade, Owned environment only where required by erasure, call result cannot depend on hidden receiver/call-local storage.
- Complete: T16 via V3–V6/V9, including empty/inline/heap and zero-size environments, partial consuming cleanup and all receivers. Callable representation and cleanup must be verified together; parser nodes already exist.

### M8 — Sequence views and iteration

- Objective: R16–R17 plus fixed-array portions of R13 and the supporting Core identities/witnesses of R19.
- Dependencies: M3, M5, M6; existing fixed-array paths are the starting point. M9 dynamic collection adapters follow later.
- Files: `KimiLibrary.cs`, `Binding.Elements.cs`, `Binding.Lengths.cs`, `Binding.ArrayInference.cs`, `ForKoto.cs`, `OwnershipAnalysis.Elements.cs`, `BodyLowering.Elements.cs`; **new** `Binding.Iteration.cs`, `OwnershipAnalysis.Iteration.cs`, `BodyLowering.Iteration.cs`, **new** sequence-view physical records as needed under Emission.
- Work: I19–I20. Canonical Index/Range/ResolvedRange/Slice/Iterator/Iterable declarations, their required Copy/Owned/Equatable witnesses, checked access plans, metadata snapshots and consuming/external-borrow iteration. Establish the minimal Equatable identity/mapping prerequisite here using I5, without waiting for M9's general comparison/Dictionary support. Lower for using selected contract operations and existing cleanup/CFG mechanisms, without source desugaring that changes contexts or Name lookup.
- Preserve: RHS-first assignment, exclusive activation timing, no intermediate array Copy, no constant-folding expansion of Move paths, empty/zero-size Slice provenance, next Loan ends before body.
- Complete: T17–T18 via V3–V5/V9; unchanged program 7 passes native O0/O2 plus invalid/Abort variants. Support both built-in and user-defined conformances, not just a special-case numeric loop.
- Risk: iterator-owned reference escape and excessive temporary materialization. Require no element-proportional storage for views/iteration.

### M9 — Collections, comparison contracts and strings

- Objective: R18–R19; complete the settled Core surface together with its dependencies.
- Dependencies: M2–M8 for witnesses, general values, returned borrows, iterators and generic cleanup.
- Files: `KimiLibrary.cs`, `Binding.Comparisons.cs` if a split is needed (**new**; current comparisons are in existing Binding expression/call files), `Binding.Expressions.cs`, `BodyLowering.Strings.cs`, `WindowsRuntime.ll.in`; **new** `CoreCollectionOperations.cs`, `BodyLowering.Collections.cs`, `CollectionLayout.cs` under compiler Binding/Emission as appropriate.
- Work: I21–I23. Canonical Array/Dictionary APIs and effects, explicit internal allocation/relocation/cleanup plans, literal duplicate checks, insertion-order mutation/iteration. Add Equatable/Comparable and Stringify witnesses plus owned interpolation output. Keep float Contract equality distinct from IEEE operators; document implementation-defined float formatting.
- Preserve: no blanket Owned constraint on collections, no dependency subtraction on clear/None/Err, no allocation within capacity including churn, growth bound, preserved placement on failed shrink, scalar/string operations already working.
- Complete: T19–T21 via V3–V5/V9; counts/instrumentation prove allocation and amortized obligations in addition to timing. String concatenation and extra APIs remain excluded.
- Risk: equality reentry/unstable keys must not break memory safety; whole-collection Loan/effect plans and storage implementation must be integrated, not validated in isolation.

### M10 — Objects, Weak and runtime refinement

- Objective: R20 with settled runtime metadata and ownership intrinsics.
- Dependencies: M3–M7; cyclic factories require consuming Callable and enum Option results.
- Files: `KimiLibrary.cs`, `KimiDeclaration.cs`, `Binding.RuntimeTypeTests.cs`, capability/Origin/effect files, `WindowsRuntime.ll.in`, `WindowsLowering.cs`; **new** `ObjectLayout.cs`, `BodyLowering.Objects.cs`, `ObjectMetadataPlan.cs` in Emission.
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
- Dependencies: I30 can begin after M2–M3; complete execution needs M4/M6/M7/M12. G2 is resolved by the adopted test profile; I31 uses those interfaces.
- Files: `TestDefinition.cs`, `Binding.Testing.cs`, `OwnershipAnalysis.Testing.cs`, `Project.Testing.cs`, `BodyLowering.Testing.cs`, shared storage lowering, `Kimi/Testing/*` and `TestCommand.cs`.
- Work: I30–I31. Test mode verifies all selected bodies before filters/listing, rejects ordinary test calls/product verifications, lowers expect/require using one condition snapshot and lazy message, fixes product plan before test additions. Runner uses one immutable all-case artifact and a fresh managed child per case, bounded logs/control channel/recovery and stable reports.
- Preserve: false `$require` Aborts after condition/message temporaries, not helper return; no outer cleanup/shutdown after Abort; failure latch survives message/cleanup failure; no product budget/frame changes from tests; successful checks allocate no failure diagnostics.
- Complete: T25–T26 via V3–V5/V10; process tree/recovery fault tests and exact failure-stage/site evidence. Cross-feature criteria remain open until their compiler prerequisites pass; the current protocol/process fault tests are recorded in the linked checkpoint.
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

The complete I1–I34 checklist, requirements, states and remaining outcomes are maintained once in [§2](#current-implementation-items). Milestone designs in §6 supply dependencies, invariants and completion gates. Completed target slices do not close their parents; the [earlier checklist](PLAN_HISTORY.md#old-checklist) preserves the audit details and superseded states.

## 8. Test Plan and Verification Records

### Test cases

T IDs denote coherent test families, not one assertion each. Expand only relevant positive, negative, boundary, interaction and regression cases. Existing test class names below are locations under `xUnitTest/Tests/`; a **new** name is a proposed file/class. Reuse `ParseTestHelper`, `MinimalEmissionTest.Analyze`, `ScalarEmissionTest.EmitFixture` / `WriteFixture`, and `StringLifetimeAudit` where suitable.

For a negative test, first make the surrounding program valid, then change only the triggering construct. Assert the intended Binding/ownership/generation diagnostic category, source document/span and lack of published artifacts. An unrelated parse/type error does not satisfy a Loan/ABI/runtime test. New diagnostic identifiers/text are to be selected through the existing diagnostic catalog at implementation time; do not invent exact existing diagnostic codes in this plan.

| ID / requirements | Input/setup and expected result | Stage/evidence | Existing or proposed location |
| --- | --- | --- | --- |
| T1 / R1,R27 | UTF-8/NFC and malformed bytes, CR/LF, comments/indentation/body forms, exact numeric rounding, merged-source aliases; parse/write/reload retain value and original spans | P/B diagnostics and serialization evidence; no early rounding through f64 | SourceEncodingTest, UnicodeIdentifierTest, NumberLiteralParseTest, KotonohaSerializationTest |
| T2 / R2 | Reached invalid condition in unselected `#switch` arm diagnoses; false `#if` target exclusion remains distinct; selected defer/Names share scope; changed settings require fresh Compilation | P/B plus selected cleanup G/native | DirectiveConditionValidationTest, CompilationSpecificationTest, DeferredEmissionTest |
| T3 / R3 | Fragment/header/default-access/base/protected/API-domain variants, added inherited names and source-order permutations; reject restricted Types exposed in broader APIs | B diagnostic at declaration/use; repaired/reloaded input recovers | OriginFragmentBindingTest, InheritedNameBindingTest, FunctionApiCertificateBindingTest |
| T1a / R1, R27 (§2.5.1, A.3) | Positive: `struct abstract`, `let override: i32`, `func virtual(x: i32)`, `self.override`, standalone `abstract` expression followed by `let next: i32 = 2` all accepted. Negative: `abstract open struct S`, `virtual func f(...)`, `virtual init() => ()`, `override deinit => ()`, `abstract get` under a stored Property, `virtual` before a Contract requirement, and root-level `virtual func g()` each produce exactly one unavailable-feature diagnostic at the modifier, no cascade, and the following line still parses as an independent declaration | P diagnostics; Historical unit 2 evidence: 40 new cases, baseline reproducer failures and full Debug/Release PASS | SpecReviewTest or FrontEndSyntaxTest (existing files); no new class needed |
| T3a / R3, R27 (A.1) | Two documents declare fragments of one struct; the first carries contradictory `T is i32` / `T is not i32` inputs. The container keeps its first fragment's SourceDocument and members retain their own. The independently required container diagnostic retains its original header span | Historical evidence; source fixture updated in T5a because a missing-name cascade is no longer independently required | SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument |
| T5a / R5, R27 | Missing conformance Name, sibling Copy/Property checks, independent constraints/uses, both clause orders, fragment locations and rebind/reload | Historical evidence; nine new cases and unit 3 evidence; diagnostic suppression never certifies a failed proof | UnresolvedConstraintBindingTest |
| T7a / R7, R27 | Projected ref/ref, uniq/uniq and uniq/ref calls retain Unknown/no-reselection and reject emission while missing effect verification reports Unsupported | Historical evidence; three strengthened cases, CLI rejection and unit 6 evidence; public effect families remain T7/I7 | InheritedReceiverBindingTest |
| T27a / R1, R5, R23 | Append valid/invalid source through both entry points; replace with empty/nonempty snapshot; revoke retained certificates and require fresh analysis | Historical evidence; seven new cases, full managed and native smoke evidence in unit 4 | MinimalEmissionTest, ContractBindingTest |
| T27b / R1, R23, R27 | Parse errors survive display clearing and duplicate-location reports with default/custom destinations; prior errors and warnings alone do not poison source | Historical evidence; seven new cases, full managed and native evidence in unit 5 | MinimalEmissionTest |
| T4 / R4,R8 | Example A below plus named/default/receiver ordering, default borrowing, interrupted acquisition; count each evaluation once and clean acquired values in specified order | B/A plans, native log, failed default/use span | DefaultBindingTest, FunctionEmissionTest, ScalarDefaultEmissionTest; earlier proposed DefaultExecutionTest is not a requirement for a duplicate class |
| T5 / R5,R19 | Kimi impostors/invalid shape, conditional witnesses, dependent projections, proof cycles/Unknown/Error, comparison mappings; declaration/order/reload variants keep same result | B certificates and diagnostic causes, no stale winner after repair | CoreCatalogTest, ContractBindingTest, ConditionalConformanceBindingTest, NormalizedConstraintProofBindingTest |
| T6 / R6–R7 | Move then read, partial Move/repair, branch/loop state, simultaneous ref/uniq and Reborrow; returned reference to local rejected, input-derived reference accepted | A at the invalid use/escaping result; native final cleanup after accepted borrow ends | OwnershipAnalysisTest, ElementMoveEmissionTest, ReferenceEmissionTest; **new** BorrowCompletionTest |
| T7 / R7 | Direct/recursive/indirect/imported callee mutates a static protected by a live Loan; changed callee effect invalidates use; independent shared read stays valid | B/A public summaries, caller diagnostics, reload/cross-module comparison | ModuleBindingTest; **new** EffectSummaryTest |
| T8 / R6,R8,R10 | Existing Resource fixtures plus base/derived construction, partial failure, field reordering and zero-size deinit; child/base destruction order observable once | B/A/TypeLayout/IR/native, C cross-check only for C layout | StructEmissionTest; **new** InheritedStructEmissionTest |
| T9 / R8–R9 | Existing checked arithmetic/conversion boundaries, short circuit, Never/Unit results, unreachable invalid use, defer divergence and Abort; acceptance unchanged by O0/O2 | Managed diagnostics then LLVM/native stderr/exit/cleanup | NumericConversionEmissionTest, WideIntegerEmissionTest, CurrentControlFlowTest, DeferredEmissionTest, AbortEmissionTest |
| T10 / R11 | Standard vs custom getter borrowing, private setter Move, Non-Copy self-assignment/custom setter, construction prohibition and Contract slot vs value witness | B/A check exact permission/Origin; one getter/setter call and cleanup observed natively | PropertyBindingTest, PropertyCompletionBindingTest; **new** PropertyEmissionTest |
| T11 / R11,R24 | Access a static twice, first-write replacement, A→B→A initialization cycle, unused invalid initializer, reverse successful-init shutdown and shutdown reentry | B/A reject invalid dependencies; native order/cycle Abort and no extra shutdown | **new** StaticInitializationTest / StaticEmissionTest |
| T12 / R12 | Example B, nested enum/Tuple/shared Subject and guard effects, moved payloads; `.Some(0)` plus `.None` alone must not prove full Option<i32> coverage; warned arms still checked | B/A coverage/warnings/spans, G tag/payload checks, native effect/destruction order | EnumBindingTest, EnumOwnershipTest, PatternCoverageCompletionTest, MatchEmissionTest; existing EnumEmissionTest and GenericEnumEmissionTest |
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
| T26 / R14,R25,R28 | Add tests/generic requests without changing product plan; invalid filtered-out test still rejects; list runs no init/codegen; one artifact across filters; stale ID, exit 0 without completion, flood logs, timeout/cancel/descendant pipe | B/G immutable plan evidence and runner protocol/process recovery; finite memory/time/control reserve | TestExecutionAnalysisTest, TestProtocolTest, TestProcessRecoveryTest and backend/windows-x64/test-testing.ps1 |
| T27 / R1,R5,R13,R22–R23 | Existing Project dependency scenarios plus added/removed overload/Name/specialization, changed private effects/access/constraints/source spans, rebind/save/reload/repair; no stale proof accepted | Current-source diagnostics and cold/warm equivalence; native changed selected result after regenerate | ModuleBindingTest, DependencyResolutionTest, KotonohaSerializationTest; **new** SemanticReuseTest |
| T28 / R22–R24 | Malformed/Unicode-colliding/oversized/corrupt packages, different valid ZIP compression, release conflicts across whole closure, interrupted/concurrent publication, pin/GC races, stale native summaries/ambiguous required symbols | Loader/CLI failure stage, atomic files/table snapshot, bounded work; valid local source package compiles/runs | DependencyLockTest, EmissionArtifactsTest, NativeToolchainTest; **new** PackageStoreTest / PackageCommandTest |
| T29 / R9,R24,R27 | Existing Hello/UTF-8/NUL/runtime failures, unique startup/library/no entry, unused unsupported source, stale/tampered build inputs and build/run/emit distinctions | Exact native bytes/stderr/exit, pre-optimization Library IR, object/link records, no output after rejected generation | MinimalEmissionTest, StartupBindingTest; existing backend scripts |
| T30 / R26 | Existing provisional append/rebind transition plus separate generated contexts, dependency-order permutations, missing/cyclic Mod IDs, changed observed input and final unresolved query | B/provenance/record invalidation; public host tests only after G1 | ProvisionalContractPremiseBindingTest, SourceDocumentAndDiagnosticTest; **new** ModLifecycleTest |

Representative source inputs (proposed tests, not files created in this phase):

**A — default evaluation and normal result (T4).** Expect `default\nbody\n`; return value 7. A separately supplied explicit argument must suppress the default call.

```kimi
func defaultValue() -> i32
    ::Kimi.Console.writeLine("default")
    return 7
func take(value?: i32 = defaultValue()) -> i32
    ::Kimi.Console.writeLine("body")
    return value
let result = take()
require result == 7 else => $abort("wrong result")
```

**B — owned enum payload (T12).** Expect `held\n`; string responsibility transfers into then out of Some exactly once.

```kimi
let value: ::Kimi.Option<string> = .Some("held")
match value
    .Some(let text) => ::Kimi.Console.writeLine(text)
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
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement"
match names.remove(1)
    .Some(let entry) => Kimi.Console.writeLine(entry.1)
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

**F — verification failure (T25), §17.5.2.** A test-only input; failure latched at `$require`, no printed defer or final line. The adopted profile requires parent exit 1 with requireAbort termination and no management error.

```kimi
#Test
func stopsAfterFailure()
    defer => ::Kimi.Console.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Kimi.Console.writeLine("Not reached")
```

Omissions: no expected outcomes for undefined unsafe behavior; no runtime Contract Views, mutable Slices, source threads, parameterized test extensions, unknown-substitution scratch, or NativeAOT tests. Do not build an exhaustive Cartesian feature cross-product. Select interactions that exercise distinct acquisition, cleanup, proof, ABI and invalidation boundaries; Appendix A's explicitly required cases still apply.

### Verification procedures

All commands below use working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`. They are resumption procedures, not commands run by this documentation migration; actual executions are recorded in PLAN_HISTORY.md. Command paths, script parameters and runner switches were checked against source/help; execution success is **not** implied. SDK observed in the recorded execution: 10.0.401 (not re-probed during this migration). Projects target net10.0; `global.json` selects Microsoft.Testing.Platform but does not pin an SDK version. Packages include xunit.v3 4.0.1, Microsoft.NET.Test.Sdk 18.10.0 and BenchmarkDotNet 0.15.8.

Use `backend/windows-x64/invoke-verification.ps1` for bounded external work: it captures logs/result JSON, optionally hashes declared inputs before/after, and uses `VerificationJob.cs` plus `verification-worker.ps1` for Windows process-tree management. Default limit is 900 seconds; choose a justified larger finite value for benchmarks/full native suites. Direct native fixtures already have per-process limits (normally 30 seconds; milestone wrappers 60 seconds). Keep output-drain/recovery bounds too. Never use a displayed launcher exit code alone when nested output reports failure.

Example wrapper for the focused suite, after V2:

```powershell
./backend/windows-x64/invoke-verification.ps1 -FilePath dotnet -ArgumentList @('xUnitTest/bin/Debug/net10.0/xUnitTest.dll', '-class', 'XunitTest.StructEmissionTest', '-parallelMode', 'none', '-failSkips') -InputPath @('xUnitTest/bin/Debug/net10.0/xUnitTest.dll', 'Kimi/bin/Debug/net10.0/Kimi.dll') -TimeoutSeconds 900
```

The direct DLL is the xUnit in-process runner, whose observed switches are `-class`, `-method`, `-parallelMode none`, `-failSkips`, `-list tests` and `-result-xml`. Do not mix these with MTP `dotnet test` switches or VSTest `--filter`. Require a nonzero selected case count and save it; use XML/runner output to fail zero-test runs. NativeAOT is not triggered by these managed commands. Existing stale DLL help only verified switch syntax.

| ID | Command / exact target and prerequisites | Success evidence / order / output constraints |
| --- | --- | --- |
| V1 | `git rev-parse HEAD`; `git status --porcelain=v1`; `dotnet --version`; `dotnet --list-sdks`; inspect global/project/profile files. For fresh runner help use `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll --help` only as a help probe | Record HEAD/diff, SDK/tool existence and actual errors; help exit 1 is not a failed test. G5 was resolved historically; record any new environment failure separately. Validate tools/archive hashes against profile before native work; existence is not integrity evidence. |
| V2 | `dotnet restore Kimigayo.slnx -m:1`, then `dotnet build Kimigayo.slnx -c Debug --no-restore -m:1`, then Release equivalent | Restore/config access and package sources available. Zero build errors; investigate warnings/new warnings. Hash actual compiler/test DLLs and source inputs. Build sequentially; do not use old bin output if either build fails. Serial MSBuild avoids the observed parallel solution-build failure without diagnostics. No publish/AOT. |
| V3 | After matching V2: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.StructEmissionTest -parallelMode none -failSkips` as initial focused check; substitute exact T-table class for each item. Full gate: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips`; repeat using Release path | Managed checks and fixture generation are distinct results. All selected cases pass, count >0, no unexplained skips. Inspect T-specific tests' side effects before running. Log exact classes/methods and counts; full suite only at M1, broad semantic changes and M15 or a justified concern. |
| V4 | LLVM verification after V3 generation: `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/StructLocal.ll`; future fixture paths recorded per test. `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'` performs pre/post-O2 verification as part of V5 | Pinned LLVM 22.1.8 and a freshly regenerated exact fixture set required. Record IR hash and compiler/test configuration. LLVM verifier PASS does not prove ABI/execution. New fixtures require matching generated names, not an assumed glob. |
| V5 | `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'`; use `-FixtureDirectory <archive>` for an isolated generated inventory | Pinned installed backend and dlltool hashes; script verifies O0/O2, inspects undefined symbols, links `/NODEFAULTLIB`, executes and checks stdout/stderr/exit/timeouts. Shared fixture/native outputs require serial access. Immutable copied inputs allow later generation while earlier native work runs. Native evidence may be shared only for identical IR and every output/exit/timeout oracle under the same pinned tools/backend and harness settings; retain the exact mapping and never count reuse as another execution. |
| V6 | `./backend/windows-x64/build.ps1 -LlvmBin ./toolchain` when backend changes or pinned candidate prerequisite is missing; then `./backend/windows-x64/test-emission.ps1 -ToolchainRoot ./toolchain -Configuration Debug` and Release after fresh MinimalEmissionTest fixtures | Inspect build script adoption/install side effects first; it can install a matching tested reproduction. Clang/llvm-lib/llvm-objdump plus normal tools required. No unpinned override for conformance. Candidate archive/report must match; verify C ABI/layout, unwind, no hidden CRT dependencies and runtime fault adapters. New T24 adapters/commands must be added and verified before calling them available. |
| V7 | After V2: `dotnet Kimi/bin/Release/net10.0/Kimi.dll restore examples/SourceDependencies/Geometry/Geometry.kimiproj`, then `dotnet Kimi/bin/Release/net10.0/Kimi.dll check examples/SourceDependencies/Geometry/Geometry.kimiproj --locked`; native CLI regressions: `./backend/windows-x64/test-cli.ps1 -ToolchainRoot ./toolchain -Configuration Release` | Restore intentionally writes the example lock; check must not rewrite it. Use an isolated copy when preserving an existing example lock matters. Paths verified against examples/SourceDependencies/README.md; script parameters inspected. After M12 add new isolated package/CLI tests rather than invoking unregistered commands now. Validate diagnostic category, failed publication, hashes and stale-artifact rejection. |
| V8 | V3 with `-class XunitTest.ModuleBindingTest`, `-class XunitTest.DependencyLockTest` and `-class XunitTest.KotonohaSerializationTest` (OR filters); after I29 use **new** SemanticReuseTest/PackageStoreTest classes | Same exact source/settings exercised cold, warm, reload, replacement, relocated and corrupt-input modes; compare acceptance, required diagnostics/current spans and selected results. Package/user-cache tests use isolated test-owned roots, not real publication stores. Record actual new filters when created. |
| V9 | `dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*'`; choose existing workloads by inspected Benchmark/Program.cs; new feature workloads added under Benchmark/Benchmarks | Build/restore environment ready, stable machine/load/settings, no concurrent builds/native tests; BenchmarkDotNet MemoryDiagnoser already configured. Record samples, variance, allocated bytes, retained/peak memory, cold/warm/reparse costs, source scale, IR/code size and runtime separately. New collection/generic instrumentation measures required operation/allocation counts, not just elapsed time. |
| V10 | TestExecutionAnalysisTest, TestProtocolTest, TestProcessRecoveryTest and backend/windows-x64/test-testing.ps1 | Use the adopted test profile; record managed/native verification independently. Verify active-case protocol, all-body list/filter semantics, finite recovery, logs, failure latching, final display and artifact identity. |
| V11 | `./backend/windows-x64/test-milestone6.ps1 -ToolchainRoot ./toolchain -Configuration Debug` then Release; same verified parameter shape for scripts test-milestone1.ps1 through test-milestone5.ps1. Existing `test-milestone7.ps1` through `test-milestone12.ps1` cover the completed targets | Exact original and renamed/changed inputs, invalid/Abort cases, source/compiler/build identities; preserve expected program outputs in milestones/README.md. Integrates earlier V checks, does not replace Appendix A coverage. Add current Windows native CI evidence without package publishing. |
| V12 | `git diff --check`; `git status --short`; inspect PLAN requirement/item/test references, file/symbol existence and new markers | For documentation-only migrations, only PLAN.md, STATUS.md and PLAN_HISTORY.md may differ; for implementation work, also audit the authorized source/test changes; requirement/ID/unfinished-work preservation and local/legacy links checked, no implemented support inferred from plans, no NativeAOT or draft edits. Repeat after plan edits. |

Fixture handling prerequisite for V4–V6: `ScalarEmissionTest.EmitFixture` includes implementation-specific scalar-IR assertions, so reuse its write/capture conventions without copying its incidental scalar opcode restrictions into unrelated generic/object tests. `WriteFixture` writes `.ll`, `.stdout`, `.stderr`, `.exit`, `.timeout` under shared `bin/scalar-fixtures`; it does not certify which compiler source produced an old file. Establish a per-run generated-file inventory plus hashes, such as recorded write events followed by immutable copies. Use dedicated prefixes or safely move only identified stale family outputs within the verified repository bin root; do not delete user files or trust timestamp equality. Concurrent generation/consumption must use different input directories, and native runs must not share output paths concurrently.

Saved verification records are in [PLAN_HISTORY.md](PLAN_HISTORY.md#units-1-32), with [the program-12 run](PLAN_HISTORY.md#program12-completion), [the preceding run](PLAN_HISTORY.md#execution-1) and [migration identity audit](PLAN_HISTORY.md#migration-audit). Do not rerun builds or tests merely to reorganize documentation. Program outputs as well as scalar fixtures are shared across configurations; serialize their producers/consumers even when report paths differ.

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
| 1 | Fresh HEAD/diff at resumption | Retain completed M1; refresh only affected baseline evidence and extend clause mapping | V1–V3, selected V4–V5 | Current evidence and known failures recorded | G5 environment, missing tools/artifacts; retain block evidence |
| 2 | M1 and selected clause reproducers | M2 then M3 semantic/ownership contracts | T1–T9/T27, V3–V5 | Checked plans usable by downstream work | Unresolved proofs or real SPEC_GAP block dependent slice |
| 3 | Checked storage/effect contracts | M4, then M5 concrete complete representations | T8/T10–T12, V3–V6 | Layout/acquisition/destruction verified together | No placeholder zero/undef or unsupported-body omission |
| 4 | Definition-side proof and concrete layout baseline | M6/I13–I15 baseline generic sharing, then I16 bounded optimization | T13–T15, V3–V6/V9 | Explicit selection/entry/frame invariants hold at all budgets | Required growth limit produces explicit resource diagnostic |
| 5 | Generic/call contracts available | M7 callables and M8 views/iteration; independent slices may be developed sequentially in either order | T16–T18, V3–V5/V9 | Program 7 and focused operations verified | Borrow escape or missing witness cannot use special-case fallback |
| 6 | General values/borrows/cleanup | M9 collections/strings; M10 objects after callable/Option support | T19–T23, V3–V6/V9 | API outcomes, effects, cost and lifecycle contracts | Undefined public extensions remain excluded |
| 7 | Checked layout/ABI; package syntax can start earlier | M11 foreign operations; M12 packages/common generation/reuse | T24/T27–T29, V3–V9 | Source closure and native connection independently validated | Corruption/stale proof blocks publication, not hidden by cache |
| 8 | Product semantics fixed | I30 test semantics/discovery; I31 adopted runner; I32 external host only after G1 | T25–T26/T30, V3/V8/V10 | Settled contracts verified, public interface blocks resolved explicitly | G1 keeps the Mod host item BLOCKED; test prerequisites remain explicit |
| 9 | Relevant features ready | Completed programs 1–11 as regression gates and full M15 clause/interaction audit, docs/performance | V2–V12 | Section 11 satisfied, residual exclusions accurate | Historical results cannot substitute for missing evidence |

This is a dependency roadmap, not a second current-action list. Use [§2](#2-execution-state) for current disposition and exact resumption rules.

## 11. Definition of Done

The initial planning deliverable was complete when its scope, architecture, stable coverage IDs, dependencies, gates and honest evidence records were reviewable, with only `PLAN.md` changed during that phase. The current implementation effort is subject to the compiler completion criteria below; a timed execution checkpoint is not compiler completion.

Compiler completion requires:

- Every finalized in-scope R family and mandatory Appendix A case mapped to completed implementation/verification evidence, including unused/unreachable source and rejection boundaries.
- No missing/Unknown/unfinished proof used as success, no incorrect semantic fallback, no required operation left rejected merely because an old emitter subset cannot lower it.
- Binding, ownership, physical validation, LLVM verification, native ABI/linking and ordinary execution independently demonstrated where applicable; Debug/Release compiler acceptance and O0/O2 observable behavior agree.
- All new representations preserve evaluation count/order, complete Types/Origins, Loan authority, initialization, normal cleanup, Abort and result-delivery invariants.
- Packages/reuse/native inputs correctly distinguish identity, proof and connection validity; no-cache and reused results agree, corrupted/stale inputs cannot certify artifacts, failed runs cannot leave valid-looking success records.
- Required allocation/complexity bounds tested; practical performance/retained-memory costs measured against the same workload/configuration. No unjustified universal zero-allocation claim.
- G1-dependent Mod interfaces either separately specified and completed, or explicitly reported as excluded, blocked integration; adopted G2 test interfaces implemented with explicit remaining compiler prerequisites with no blanket claim that their dependent settled execution contracts are finished. A remaining included guarantee prevents claiming the entire compiler complete.
- English SPEC choice notes/STATUS/usage/examples accurate, no planned support labeled implemented, no draft changes or NativeAOT requirement introduced.
- Every relevant verification record has commands, counts, source/artifact/tool identity and retrievable evidence. No unresolved required failure or environment blocker is hidden by historical PASS reports.

## 12. Plan Changes and Out-of-Scope Findings

The 2026-09-18 documentation migration separated current planning, product support and historical evidence; see the [migration audit](PLAN_HISTORY.md#migration-audit) for corrections, missing evidence and preservation checks. Earlier implementation decisions and deviations are retained in [plan changes](PLAN_HISTORY.md#plan-changes). No implementation was advanced during that migration; the subsequent program-12 execution has its own [completion record](PLAN_HISTORY.md#program12-completion).

Out-of-scope findings: general LSP/editor integration, KimiCode configuration, debug information, additional target profiles, remote registries/network publication and NuGet distribution automation are not necessary to implement the finalized compiler contracts here. Existing CI improvements may be extended only as needed for conformance evidence. Public APIs omitted by the specification belong in separate design work. No unrelated fix, draft edit, or package publication is authorized by this plan. Implementation execution details are in PLAN_HISTORY.md. The original extended front-end benchmark input failure remains an out-of-scope finding (unit 2); its common workload is usable.


## Historical section redirects

Compatibility targets for older bookmarks. These links open records, not current instructions.

<a id="current-timed-continuation--partial-bodies-and-caught-transfers-2026-09-18"></a>
[Current timed continuation — partial bodies and caught transfers (2026-09-18)](PLAN_HISTORY.md#execution-1)

<a id="previous-timed-continuation--partial-terminal-joins-2026-09-18"></a>
[Previous timed continuation — partial terminal joins (2026-09-18)](PLAN_HISTORY.md#execution-2)

<a id="previous-timed-continuation--ownership-scope-joins-2026-09-17"></a>
[Previous timed continuation — ownership scope joins (2026-09-17)](PLAN_HISTORY.md#execution-3)

<a id="previous-timed-continuation--broader-ownership-plan-2026-09-17"></a>
[Previous timed continuation — broader ownership plan (2026-09-17)](PLAN_HISTORY.md#execution-4)

<a id="program-milestone-11-completed-2026-09-17"></a>
[Program Milestone 11 completed (2026-09-17)](PLAN_HISTORY.md#execution-5)

<a id="implementation-checkpoints-historical-within-this-execution"></a>
[Implementation checkpoints (historical within this execution)](PLAN_HISTORY.md#implementation-checkpoints-historical-within-this-execution)

<a id="program-milestone-9-completed-2026-09-17"></a>
[Program Milestone 9 completed (2026-09-17)](PLAN_HISTORY.md#execution-6)

<a id="historical-execution-program-milestone-9-resumed-after-program-10-2026-09-17-incomplete-at-that-checkpoint"></a>
[Historical execution: program Milestone 9 resumed after program 10 (2026-09-17, incomplete at that checkpoint)](PLAN_HISTORY.md#execution-7)

<a id="program-milestone-10-integration-2026-09-17-complete"></a>
[Program Milestone 10 integration (2026-09-17, complete)](PLAN_HISTORY.md#execution-8)

<a id="program-milestone-9-integration-2026-09-17-in-progress"></a>
[Program Milestone 9 integration (2026-09-17, in progress)](PLAN_HISTORY.md#execution-9)

<a id="timed-continuation-2026-09-17-064107-utc"></a>
[Timed continuation: 2026-09-17 06:41:07 UTC](PLAN_HISTORY.md#timed-continuation-2026-09-17-064107-utc)

<a id="previous-execution-checkpoint"></a>
[Previous execution checkpoint](PLAN_HISTORY.md#previous-execution-checkpoint)

<a id="program-milestone-8-integration-2026-09-17"></a>
[Program Milestone 8 integration (2026-09-17)](PLAN_HISTORY.md#execution-10)

<a id="program-milestone-7-integration-2026-09-17"></a>
[Program Milestone 7 integration (2026-09-17)](PLAN_HISTORY.md#execution-11)

<a id="evidence-vocabulary"></a>
[Evidence vocabulary](PLAN_HISTORY.md#old-architecture)

<a id="actual-processing-path"></a>
[Actual processing path](PLAN_HISTORY.md#old-architecture)

<a id="support-matrix-at-this-checkout"></a>
[Support matrix at this checkout](PLAN_HISTORY.md#old-architecture)

<a id="appendix-a-clause-coverage-map-i2-2026-09-17"></a>
[Appendix A clause coverage map (I2, 2026-09-17)](PLAN_HISTORY.md#appendix-a-baseline)

<a id="verification-records-current-planning-phase"></a>
[Verification records (current planning phase)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m1-baseline-2026-09-17"></a>
[Verification records (M1 baseline, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-1-2026-09-17"></a>
[Verification records (M2 unit 1, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-2--t1a-execution-started-2026-09-17-014222-jst"></a>
[Verification records (M2 unit 2 / T1a, execution started 2026-09-17 01:42:22 JST)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-3--t5a-2026-09-17"></a>
[Verification records (M2 unit 3 / T5a, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-4--t27a-2026-09-17"></a>
[Verification records (M2 unit 4 / T27a, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-5--t27b-2026-09-17"></a>
[Verification records (M2 unit 5 / T27b, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-6--t7a-2026-09-17"></a>
[Verification records (M2 unit 6 / T7a, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="i4-next-unit-audit-checkpoint-2026-09-17-about-57-minutes-elapsed"></a>
[I4 next-unit audit checkpoint (2026-09-17, about 57 minutes elapsed)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-7--t4a-execution-started-2026-09-17-024236-jst"></a>
[Verification records (M2 unit 7 / T4a, execution started 2026-09-17 02:42:36 JST)](PLAN_HISTORY.md#units-1-32)

<a id="verification-records-m2-unit-8--t4b-2026-09-17"></a>
[Verification records (M2 unit 8 / T4b, 2026-09-17)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-9--t4c-scalar-default-acquisition-done"></a>
[Execution unit 9 — T4c scalar default acquisition (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-10--t4d-scalar-conversions-in-defaults-done"></a>
[Execution unit 10 — T4d scalar conversions in defaults (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-11--t4e-scalar-selection-defaults-done"></a>
[Execution unit 11 — T4e scalar selection defaults (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-12--t4f-scalar-do-defaults-done"></a>
[Execution unit 12 — T4f scalar do defaults (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-13--t4g-definite-default-move-diagnostics-done"></a>
[Execution unit 13 — T4g definite default Move diagnostics (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-14--t4h-prepared-subplaceaggregate-acquisitions-done"></a>
[Execution unit 14 — T4h prepared subplace/aggregate acquisitions (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-15--t4i-contained-scalar-default-transfers-done"></a>
[Execution unit 15 — T4i contained scalar default transfers (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-16--t4j-immutable-scalar-default-locals-done"></a>
[Execution unit 16 — T4j immutable scalar default locals (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-17--t4k-unit-defaults-done"></a>
[Execution unit 17 — T4k Unit defaults (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-18--t4l-initialized-mutable-scalar-default-locals-done"></a>
[Execution unit 18 — T4l initialized mutable scalar default locals (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-19--t4m-scalar-prepared-subplace-reads-done"></a>
[Execution unit 19 — T4m scalar prepared-subplace reads (DONE)](PLAN_HISTORY.md#units-1-32)

<a id="final-checkpoint-for-the-third-timed-continuation"></a>
[Final checkpoint for the third timed continuation](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-20--t4n-checking-after-noncompleting-initializers-in_progress"></a>
[Execution unit 20 — T4n checking after noncompleting initializers (IN_PROGRESS)](PLAN_HISTORY.md#units-1-32)

<a id="final-checkpoint-for-the-044326-execution"></a>
[Final checkpoint for the 04:43:26 execution](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-21--i6i8-t4n-h-scoped-checking-continuation"></a>
[Execution unit 21 — I6/I8 T4n-h scoped checking continuation](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-22--i6i8-t4n-i-terminal-conditional-joins"></a>
[Execution unit 22 — I6/I8 T4n-i terminal conditional joins](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-23--i6i8-t4n-j-scoped-terminal-conditional-joins"></a>
[Execution unit 23 — I6/I8 T4n-j scoped terminal conditional joins](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-24--i6i8-t4n-k-completing-selections-before-scope-termination"></a>
[Execution unit 24 — I6/I8 T4n-k completing selections before scope termination](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-25--i6i8-t4n-l-divergence-with-scalar-loop-local-effects"></a>
[Execution unit 25 — I6/I8 T4n-l divergence with scalar loop-local effects](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-26--i6i8-t4n-m-noncompleting-conditional-operands"></a>
[Execution unit 26 — I6/I8 T4n-m noncompleting conditional operands](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-27--i6i8-t4n-n-scoped-noncompleting-conditions"></a>
[Execution unit 27 — I6/I8 T4n-n scoped noncompleting conditions](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-28--i6i8-t4n-o-noncompleting-while-conditions"></a>
[Execution unit 28 — I6/I8 T4n-o noncompleting while conditions](PLAN_HISTORY.md#units-1-32)

<a id="final-checkpoint-for-the-054333-execution"></a>
[Final checkpoint for the 05:43:33 execution](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-29--i6i8-t4n-p-partial-scoped-terminal-joins"></a>
[Execution unit 29 — I6/I8 T4n-p partial scoped terminal joins](PLAN_HISTORY.md#units-1-32)

<a id="final-checkpoint-for-the-083038-execution"></a>
[Final checkpoint for the 08:30:38 execution](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-30--i6i8-t4n-q-completing-loops-and-transfer-extents"></a>
[Execution unit 30 — I6/I8 T4n-q completing loops and transfer extents](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-31--i6i8-t4n-r-unchanged-mixed-target-continuations"></a>
[Execution unit 31 — I6/I8 T4n-r unchanged mixed-target continuations](PLAN_HISTORY.md#units-1-32)

<a id="execution-unit-32--i6i8-t4n-s-bare-transfers-after-mixed-joins"></a>
[Execution unit 32 — I6/I8 T4n-s bare transfers after mixed joins](PLAN_HISTORY.md#units-1-32)
