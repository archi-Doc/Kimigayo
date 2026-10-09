# Compiler Semantic IR and Responsibility Reorganization Plan

Date: 2026-10-09

Status: Proposed; implementation has not started.

Survey baseline: `8fd12d1254516413ca1b450b2c7dd053350bf614` (`dev`).

## 1. Decision and intended result

Reorganize all of `src/Kimi/Compiler` around explicit phase contracts. Resolve the meaning of a source operation once, publish that decision in a common semantic representation, and make subsequent analyses and physical lowering consume that representation. Separate the facts describing an operation from the independent rules that interpret those facts.

The central deliverable is a **closed semantic module containing declaration contracts and semantic bodies**, followed by explicit analysis results and a closed physical emission module. A semantic body includes control flow, values, Places, calls, projections, acquisitions, transfers and cleanup obligations. It is not an ownership-specific approximation of executable source.

The work replaces responsibility boundaries, not just filenames or method sizes:

1. Syntax interpretation and candidate selection belong to the front end.
2. Resolved identities, contracts, operation meanings and operand relationships belong to the semantic model.
3. Initialization, actual Loan dependencies, retained authority, liveness, effects and cleanup each have an explicit analysis owner.
4. Representation, storage, ABI and instruction selection belong to physical lowering.
5. Source attribution and diagnostic publication are adapters over recorded facts.
6. Compilation orchestration owns phase readiness and invalidation, not the algorithms of those phases.

Prefer deleting repeated interpretation and consolidating existing records before adding abstractions. Each proposed component must replace an identified responsibility or dependency; the number of new interfaces, classes or IR layers is not a measure of progress.

The target is **one canonical representation for each decided fact**, not one universal IR for tokens, type inference, ownership and LLVM. Symbolic generic contracts remain symbolic until their specified interpretation context is supplied. Unknown, pending, invalid and unsupported work remain distinguishable from completed work.

This is an implementation architecture proposal. It does not change language validity, broaden verified support, implement outstanding features or authorize their resumption. This session adds only this plan; the implementation and verification described below are future work.

## 2. Authority, scope and survey method

Required behavior remains in [SPEC](../../docs/SPEC.md), [IMPL](../../docs/IMPL.md), especially [implementation requirements A.3, A.7, A.12, A.14–A.16 and A.23](../../docs/impl/appendices/A-compiler-requirements.md). Navigation and current boundaries were checked against [CODEMAP](../../docs/dev/CODEMAP.md), [PLAN](../../docs/dev/PLAN.md) and [STATUS](../../docs/STATUS.md). [SETTLED](../../docs/SETTLED.md) constrains the design choices below.

The survey inventories the whole Compiler tree, then follows representative producers, retained models and consumers across its phase boundaries. At the baseline it contains 455 C# files, approximately 135,000 lines including comments and blank lines, plus supporting files. This is a structural survey, not a claim that every branch has been behaviorally audited. U0 records an exhaustive dependency inventory before implementation.

The scope includes all eight current directories, all C# files directly under Compiler, and the compiler's internal callers at the Checking, LSP, Testing and Project boundaries. Those adjacent components change only where adapting to the new compiler contract requires it. The plan does not redesign CLI/LSP behavior, add a CSP transport, introduce another backend, replace the Kimi library with LLVM, or implement deferred async/debugger work.

Preserve the following decisions:

- Generic ownership checking continues to use finite Semantics cases and the ordinary analysis engine. Do not revive the rejected universal abstract ownership model or assume pair-slot premises in cases where the slot does not exist.
- Generic defaults remain inline replicas with source evaluation order, prepared-slot protections and cleanup. Preserve the operation/Place interpretation context introduced by the G82 work. Do not replace defaults with instance evaluator functions.
- Instantiation substitutes published contracts and plans; it does not reopen lookup, overload ranking or definition-time legality to rescue a failed selection.
- An Origin relation is not proof of actual Loan identity or access authority. Equal instantiated Origin names do not merge independent inputs.
- Keep the existing Hover scheduling and detached publication contracts. No second scheduler is needed for compiler organization.
- Outstanding boundaries such as OCC-X, G78, G79, G83 and the remaining shared-object work retain their own support conditions. Paused and deferred work stays paused or deferred.

## 3. Findings that determine the design

Line numbers below are navigation hints at the survey baseline; symbols and relative paths identify the lasting references.

| Current location | Observation | Required architectural change |
| --- | --- | --- |
| [OwnershipBody.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs), `ResultArgument`, around 3278; `ReceiverRead`, `SoleResultInput` | Reconstructs result ancestry from default/value/direct call forms, declaration return Types and preceding `CallEntry` operations. | Publish call inputs, evaluation occurrences and symbolic result-source relationships. Analyses must not discover operands by source identity or neighboring operations. |
| [OwnershipBody.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.cs), `Transfer`, around 420; [OwnershipBody.Updates.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.Updates.cs), around 39; [BodyLowering.Updates.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Updates.cs), around 96 | Initialization, borrow contents and emission identify swap/exchange from the invocation's bound target. | One explicit update descriptor, including simultaneous old-content transfers and new-content installation. |
| `OwnershipBody.Borrows.cs`, `AddExplicitDependencies`, around 868; `RetainBorrowAuthority`, around 1426 | Different analyses reconstruct overlapping source/destination relationships. | A common transfer-fact view with separate dependency and authority policies. |
| `OwnershipBody.Borrows.cs`, `OperationFlow`, `ValueFlow`, `FlowOf`, around 54–165 | Total operation classification already exists for retained authority. | Extend this useful foundation into neutral, complete operand/transfer contracts; do not introduce a competing classifier. |
| `OwnershipBody.Borrows.cs`, `VerifyBorrows`, around 232 | Dependency construction, content history, authority, liveness, conflict detection and diagnostic suppression share mutable state. | Separate result owners and make their execution dependencies explicit. |
| [OwnershipAnalysis.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs), `Analyze`, `ReportDiagnostics`, `BuildBody` | Runs the session, constructs bodies, interprets expressions/calls, manages cases/defaults and publishes diagnostics. | Separate scheduling, semantic body construction, interpretation, analysis and publication. |
| [BindingModel.cs](../../src/Kimi/Compiler/Binding/BindingModel.cs), `BindingSymbol.Declaration`; [Binding.Origins.Model.cs](../../src/Kimi/Compiler/Binding/Binding.Origins.Model.cs), `BoundOrigin.Binder` | Semantic identities still carry syntax objects. | Introduce module-local semantic IDs and immutable declaration/binder records, with a separate source map. |
| [Binding.Properties.Model.cs](../../src/Kimi/Compiler/Binding/Binding.Properties.Model.cs), `BoundProperty.IsStored`, `BoundAccessor.IsStandard`, `BoundMemberPath.Declaration` | Properties and paths can compute semantic facts by reading declaration syntax. | Finalize storage/accessor kind, access contracts and member paths in the semantic contract. |
| [InvocationKoto.cs](../../src/Kimi/Compiler/Parsing/Koto/Expressions/InvocationKoto.cs), `RightFirstArguments`; [Binding.ValueCalls.cs](../../src/Kimi/Compiler/Binding/Binding.ValueCalls.cs), `BoundValueCall.ReceiverType` | Evaluation order and receiver Type can be derived from parent syntax or the receiver node. | Record the evaluation schedule and interpreted receiver descriptor explicitly. |
| [ElementAccess.cs](../../src/Kimi/Compiler/ElementAccess.cs) | A shared helper mixes syntax recognition, Binding queries, access authority, selector identity and ownership eligibility. | Resolve a semantic Place/projection plan once; separate front-end recognition from syntax-free path operations. |
| [Binding.OriginProof.cs](../../src/Kimi/Compiler/Binding/Binding.OriginProof.cs), `OriginAtUse` | Proof context can be recovered by walking source parents. | Pass a proof environment and composed interpretation context explicitly. A facade over Binding is insufficient. |
| [BodyLowering.Calls.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Calls.cs), `LowerCall`, around 135–316 | Reads invocation shape, declared parameter syntax, argument mappings and Binding instantiation/fit queries. | Separate semantic instantiation from ABI lowering; consume resolved calls and verified usage. |
| [ObjectGenerationPlan.cs](../../src/Kimi/Compiler/Emission/ObjectGenerationPlan.cs), around 101 and 176; [LlvmEmitter.cs](../../src/Kimi/Compiler/Emission/LlvmEmitter.cs), constructor | Runtime tests and layout/destructor planning can consult syntax, Copy proof or Binding callbacks. | Publish runtime-test, capability, destructor and representation inputs before physical lowering. |
| [EmissionModule.cs](../../src/Kimi/Compiler/Emission/EmissionModule.cs), `SourceCalls`, `PendingEntries`, `DictionaryHelper.Related` | The writer consumes resolved physical facts, but the retained module still carries construction-time `BoundCall` keys and pending generic work. | Move builder queues/maps out of the final module and replace semantic keys with physical function/ABI IDs. Preserve the existing writer boundary. |
| [Compilation.cs](../../src/Kimi/Compiler/Core/Compilation.cs), `InvalidateSourceAnalysis`, `NoteSyntaxEdit` | Reusable phases and source invalidation already exist, with mutable semantic fields on syntax during Binding. | Make phase generations, publication and storage leases explicit while preserving reuse. |

The problem is therefore broader than ownership. Merely adding a `CallKind` field or extracting the two large methods would leave transitive syntax access, duplicated operand discovery, ambiguous phase readiness and backend semantic queries intact.

## 4. Target pipeline and permitted dependencies

```text
Source snapshots + target/configuration + selected dependencies
    |
    v
Lexing / Parsing / directive selection / structural flow
    |
    v
Binding workspace: declarations, candidates, constraints, obligations
    |       <-> explicit contract/effect completion work graph
    v
Semantic module builder
    |-- declaration, Type, Origin and callable contract tables
    |-- semantic body templates + interpretation contexts
    |-- runtime CFG + checking-only continuations
    |-- values, Places, calls, transfers and cleanup obligations
    v
Closed semantic view for a definition / Semantics case / instance
    |
    v
Flow, borrow, authority, effect and cleanup analyses
    |-- analysis results + issue facts + validity certificate
    v
Verified semantic module/body view
    |
    v
Representation + storage + ABI planning
    |
    v
Physical body lowering -> finalized EmissionModule -> LLVM writer
    |
    v
Artifact publication -> native toolchain

Source map + recorded issue facts -> diagnostic publication
Syntax + semantic projections -> editor/documentation adapters
```

The front end is not required to be a single linear pass. Inferred completion, conformance, recursive effects and receiver preservation can depend on one another. They live in an explicit work graph before publication; pending obligations name their prerequisites and selected operation. Completing a prerequisite must not restart name lookup or choose a different candidate.

| Component | Permitted semantic inputs | Forbidden dependency |
| --- | --- | --- |
| Parser and structural flow | Tokens, snapshots, prepared directive environment, explicit provisional Type facts where required | Ownership results, layout, ABI, LLVM |
| Binding services and semantic builder | Syntax, declaration workspace, candidate/proof contexts, completed contracts | Physical emission choices deciding language legality |
| Semantic tables and body contracts | IDs, normalized Types/Origins, resolved plans, explicit contexts, source-anchor IDs | `Koto`, `Binding`, `Compilation`, callbacks that reach them, mutable candidate state |
| Body analyses | Closed semantic view, declared assumptions, explicit analysis results | Syntax traversal, overload selection, source-string matching, backend storage guesses |
| Physical planners and lowering | Verified semantic view, concrete instantiation facts, target profile | Binding queries, syntax shape, renewed language proof/selection |
| LLVM writer | Finalized physical module and target serialization data | Semantic builder queues, syntax, Binding, ownership solver |
| Publication adapters | Immutable issue/fact records and source/editor mappings | Re-running semantic analysis to manufacture an explanation |

These rules apply transitively. Replacing `Koto` with a wrapper that exposes `BindingSymbol.Declaration`, or passing a delegate that consults Binding, does not satisfy the boundary.

Source attribution remains available through `SourceAnchorId`: source snapshot identity, span, declaration anchor, expansion/call-site relationships and operation role. The source/editor side may retain syntax for inspection, formatting and exact rendering; analyzers and lowering receive no syntax accessor. A source anchor alone must not identify an execution occurrence: inline defaults, deferred executions and instantiated bodies can share the same source range.

## 5. Semantic model contracts

The names in this section describe proposed responsibilities, not mandatory final C# spellings. Evolve the existing retained plans into this model; avoid a second implementation of their language rules.

### 5.1 Identities, publication and readiness

Use typed, range-checked IDs for module, declaration, Type, Origin binder, proof context, body, block, operation, value, Place, call and source anchor. Define the owner and generation of every ID. A local integer is not a persisted artifact identity or a globally comparable symbol.

A semantic module owns shared tables. Bodies refer to these tables instead of copying signatures, Types or Origin graphs. Equality of semantic identity must not depend on object identity of syntax or declaration traversal order. Stable external identities, where already required, map to local IDs at the boundary.

Distinguish three capabilities:

| Capability | Meaning | Consumers |
| --- | --- | --- |
| Selected with obligations | Target, operands and relevant contracts are fixed, but named semantic proofs are pending. | Contract/effect completion and explicitly authorized prerequisite analyses only. |
| Closed for analysis | All facts required by the specified analysis are present, or legitimate symbolic assumptions are explicit. No syntax lookup is needed. | The relevant body analyses. This is not a successful check result. |
| Verified for lowering | Required definition/case/instance checks succeeded for the exact module generation and configuration. | Physical planning and emission. |

Represent failed, unsupported and resource-limited results separately. A missing plan in otherwise completed supported code is an internal invariant failure, not permission to infer it from source. Known unsupported input retains its factual diagnostic and never gains a success certificate.

For the future OCC-X completion path, selected operations can be analyzed under explicit prerequisite contracts without publishing preservation as proven. A proof work graph computes the supported fixed points, detects unresolved cycles and controls final publication. This architecture prepares that boundary; it does not itself implement or claim OCC-X support.

### 5.2 Declaration and Type contracts

Publish complete declaration facts needed downstream: parameter and receiver slots, argument-name contract, result/completion mode, unsafe requirements, storage/accessor kind, field/base identities, access guarantees, capability results, defaults, effect bounds, Origin schemas, requirement/witness identities, specialization selection and compiler-owned operation identity. Constants retain exact fitted values and operation semantics; downstream code does not recover arithmetic or conversion rules from token spelling.

`BoundType` and related current models need an explicit migration decision per field: retain a syntax-free value, convert to an ID/table lookup, keep only in the builder, or place in source metadata. No final semantic record may reach a syntax object through a Type, Origin, witness, property or declaration record.

Origin contracts retain binder/slot identities, intersections, conditional presence and proof dependencies. A pure Origin proof service takes normalized Origin expressions plus a `ProofContextId`; it cannot find its environment by source-parent traversal. Syntax resolution and proof queries are separate operations.

Capability/proof outcomes keep their status and dependencies. Missing evidence is not an empty effect, false flag or implicit success. Existing finalization rules for associated inference, conditional conformances and inherited mappings remain authoritative.

### 5.3 Body, value and Place contracts

A semantic body publishes:

- Explicit runtime blocks, ordered operations, terminators and edges.
- Separate checking-only regions, seeds and replay edges. They never become runtime predecessors, phi inputs or runtime cleanup executions; checking replay still models and checks cleanup effects.
- Values with interpreted Types and definition occurrences; Places with logical storage identity, mutability and path capabilities.
- Projections with field/base/tuple/enum identities, literal-only static selectors or evaluated dynamic selectors, and slot/referent distinctions.
- Calls, acquisitions, conversions, tests, updates and other operations with typed operands and explicit result IDs.
- Scope/region identities, result capture and delivery, deferred registrations and execution occurrences, and ordered cleanup obligations on exits.
- Per-operation and per-Place interpretation context and source anchor.

Do not infer a static Move Path from a folded dynamic expression or physical address. Preserve the distinction between a literal selector, a runtime key and a known constant value. Zero-sized storage retains logical Place identity and all evaluation/ownership effects.

An operation descriptor exposes its complete operand roles, reads, writes, definitions, acquisitions and transfer descriptors. An exhaustive operation schema identifies operations with no transfers and explains that result. New operation kinds cannot silently fall through a default case. The current `FlowOf`/totality work is the seed for this schema.

This does not require converting all storage to SSA. Use explicit definition occurrences and before/after content endpoints where analysis needs them; preserve the existing efficient Place/block representation until measurements justify a different algorithm.

### 5.4 One call contract for every call form

A `CallPlan` separates the selected callable contract from its invocation occurrence:

| Field group | Required facts |
| --- | --- |
| Identity | Call ID, selected target/requirement/witness or callable-value contract, dispatch kind, compiler operation identity, requesting source anchor |
| Evaluation | Explicit ordered preparation steps, receiver occurrence, explicit argument occurrences, inline default occurrences, invocation/activation point and abrupt-completion exits |
| Binding | Parameter slot for each prepared input, separate evaluation ordinal, completed Type/length/Origin substitutions, result Type and value/Place result mode |
| Acquisition | Selected adaptation/Copy/Move/Reborrow/borrow plan, reservation identity, activation and prepared-slot protections |
| Provenance | Result-source expressions over distinct input/receiver/environment/static-root identities, projected result components, declared Origin relations and proof context |
| Effects | Verified or explicitly pending effect summary, writable referent relationships, contract/implementation family dependencies |
| Lifetime | Result securing point, input responsibility transfer, abandoned-call cleanup and cleanup after normal completion |

Store parameter order independently from evaluation order. A backend may reorder already prepared values for the ABI; it must not evaluate source arguments in that order.

Direct calls, requirement dispatch, function items, common Function values, Callable receivers, constructors, property/indexer calls, compiler-managed calls and inline default calls normalize to this vocabulary. Distinct dispatch mechanisms remain explicit tags; they do not receive independent argument-discovery algorithms.

Result provenance is a symbolic relationship, not a promise that there is one source. It can name several inputs, the callable environment, fixed/static roots or contract-dependent projections. Analysis combines this relationship with the actual Loans at the call. Preserve the difference between a set of possible origins and proven single-source descent. A conservative unknown relationship cannot silently grant authority or discard a dependency.

After migration, `ResultArgument`, `ReceiverRead`, `ReceiverEntry` and analogous backward `CallEntry` scans are removed or replaced by queries over these explicit records. Source equality and operation adjacency have no semantic role in argument mapping.

### 5.5 Updates and common transfer facts

An `UpdatePlan` states whether the operation is swap, exchange or another supported replacement. It identifies the targets, incoming values, old-content result, acquisition, alias relationship and before/after content endpoints. Initialization analysis, borrow analysis and lowering consume the same plan.

Swap reads both old contents before installing either new content. Exchange secures the old content for its result before replacement. Both preserve slot identity and distinguish slot authority from the authority carried by contents. Self-updates and overlapping paths follow their resolved legality/alias contract; an implementation must not simulate swap by two sequential destructive stores.

Common transfer facts describe **where values or their components go**, including:

| Semantic operation | Shared fact | Rules kept outside the fact |
| --- | --- | --- |
| Copy, Move, Reborrow or acquisition | Source/destination and selected acquisition; source responsibility effect | Which dependencies/authority survive; whether a Loan is a child of another Loan |
| Write, payload placement, subject initialization | Destination slot, old/new content endpoints and incoming value | Initialization lattice, destruction decision and borrow validity |
| Construction/decomposition/pattern acquisition | Component mapping and subject acquisition mode, including each payload | Pattern legality, component Loan relationships, partial cleanup |
| Closure/environment creation | Capture entry to environment component mapping | Escape/lifetime constraints and callable effect rules |
| Call and writable retention | Prepared input/result relationships and contract projection | Actual Loan propagation, retained access, effect conflicts |
| Borrowed/element/pointer store | Stored value, located target and explicit exact/conservative target set | Kill versus may-update policy and path authority |
| Swap/exchange | Grouped old-content and new-content transfers | Conflict checking and ownership state changes |
| Control/test/cleanup | Explicit read/end/destroy/no-transfer description as appropriate | Reachability, lifetime ends, cleanup action and diagnostic policy |

A transfer endpoint includes the operation occurrence, value/Place component, content version where needed, and exactness. A projection cannot be reduced to an unqualified whole-Place edge. No-transfer is explicit, not a missing row.

Use a shared enumerator/view over authoritative operation descriptors, with reusable flat storage where materialization is necessary. Do not keep a manually maintained second graph that can disagree with the operation. During transition, generated/cached edges are stamped with their source body generation and checked against the descriptor.

`AddExplicitDependencies` and `RetainBorrowAuthority` become separate consumers of this view. Their result tables, joins, kills, fixed points and error conditions remain separate. Sharing input facts does not imply sharing the semantic meaning of those facts.

### 5.6 Generics, inline defaults and interpretation

Promote the existing [InterpretationContext](../../src/Kimi/Compiler/Analysis/InterpretationContext.cs) mechanism into an explicit semantic context: definition identity, composed substitutions, Semantics case, default replica identity and proof anchor. Preserve bounded sharing of contexts and the current prepared-slot rules.

Normalize default bodies into reusable semantic templates and expand their execution inline at each use. A template expansion substitutes IDs and plans with the composed context; it does not revisit default syntax to rediscover call targets or transfer meaning. Declaration checking and caller execution keep distinct occurrences and source relationships.

Select the admitted Semantics cases before running the ordinary case analysis. Conditional slot existence and premises are evaluated in that case. Share descriptors that are independent of the case; never reuse a result across incompatible contexts merely because the source node is the same.

Monomorphization operates on verified templates and explicit substitutions. Concrete layout, runtime identity and target-supported representation may still require instance work. Existing generic-instance refusals retain the requesting call, instance context and original cause. Instantiation does not turn a rejected generic definition into an accepted one.

## 6. Responsibilities and analysis execution

### 6.1 Replace the shared ownership object with explicit owners

| Proposed responsibility | Owns | Reads/publishes |
| --- | --- | --- |
| `SemanticBodyBuilder` | Construction cursor, lexical scopes, temporary/occurrence IDs and pending body records | Final Binding decisions -> semantic body; no analysis result tables |
| `AnalysisCoordinator` | Body/case scheduling, readiness, cancellation and result publication | Semantic views and explicit prerequisite results |
| `InitializationAnalysis` | Reachability, initialization/move/assignment/ownership lattice and work queues | CFG + operation effects -> converged states and placement/destruction facts |
| `ReferentAnalysis` | Content definitions, target sets, content predecessor indexing | Places + transfers -> exact/conservative referent/content facts |
| `BorrowDependencyAnalysis` | Actual Loan dependency and carrying relationships | Contract seeds + transfers + referent facts -> dependency results |
| `BorrowAuthorityAnalysis` | Retained authority, ancestry and recovery facts | Transfers, dependencies and retention contracts -> authority results |
| `BorrowLivenessAnalysis` | Runtime/checking liveness and implicit uses | CFG + established dependencies + cleanup uses -> live Loan/holder facts |
| `BorrowConflictChecker` | Local conflict candidates | Converged state/dependency/authority/liveness results -> issue facts |
| `CallUsageChecker` | Reservation/activation, prepared-default access and reserved-write checks | Explicit call plans + converged state/Loan facts -> issue facts and validated usage facts |
| `EffectAnalysis` | Body summaries and explicit interprocedural completion graph | Operations, calls, destruction and published contracts -> effect/proof results |
| `CleanupPlanner` | Ordered, finalized cleanup actions and result arrival mapping | Scope obligations + converged states -> runtime cleanup plan |
| `AnalysisDiagnosticPublisher` | Stable issue identities, deduplication and source conversion | Immutable issue facts + source map -> existing diagnostic model |

These are ownership boundaries, not a requirement to allocate one object per pass or per body. The scheduler may lease reusable workspaces. A read-only result view must not expose another analysis's mutable scratch arrays.

Extracting partial files under the same giant `OwnershipBody` or passing it as a service locator does not complete this work. Each result needs a named writer, readers, lifetime, reset rule and validity key. Shared graph iteration, packed storage and bounded work-list mechanics may be reused; lattice joins and transfer policies belong to their analysis.

### 6.2 Make order and convergence reviewable

Start by recording the current order and dependencies, including reads hidden in local functions. Preserve that schedule during extraction.

The current `OwnershipAnalysis.CompleteBody` sequence is `Solve` -> `FinalizeResults` -> `CheckUnreachable` -> `PrepareCallReservations` -> `VerifyBorrows` -> applicable prepared-default verification -> `VerifyCallReservations` -> reserved-write publication. Within `VerifyBorrows`, dependency seeds and requirement propagation precede content/referent preparation, authority/retention closure, stored-borrow activity, liveness, per-root Loan-flow convergence/conflicts and requirement-effect checks. This is the initial extraction baseline, not an incidental order that can be discarded.

One coupling is already known: `RetainBorrowAuthority.Merge` updates borrow dependencies, transferred-Origin facts and stored-borrow records as well as authority; local-region Loan flow consumes those facts. Model this as an explicit dependency/authority/retention work group. Each result still has one writer, and a policy submits deltas to that writer's join rather than mutating another analysis's private table. Converge that group before dependent liveness work. Per-root local-region/content flow has its own subsequent convergence; do not collapse these two schedules or assume liveness itself belongs to a universal fixed point.

After preserving those relationships, organize the responsibilities as follows:

1. Validate the semantic body and its contexts; prepare runtime and checking graph indexes.
2. Converge runtime initialization/Move state, then checking-only state/replay under its explicit seeds. Derive runtime placement and cleanup actions only from converged runtime states.
3. Prepare contract seeds, content definitions, referent facts and explicit transfer indexes.
4. Compute the coupled dependency/authority/retention closure and stored-borrow activity through explicit result joins.
5. Compute Loan liveness over runtime and checking-continuation edge sets.
6. Converge the dependent per-root Loan/content flows; check conflicts, prepared defaults, reservations and effect/contract obligations; record factual issues.
7. Finalize runtime cleanup and delivery plans, validate completeness and publish the verified view.

This list is not permission to impose one traversal where a fixed point is required. U0/U5 identify actual dependency cycles. For any mutually dependent results, publish a specific strongly connected work group with its domain, initialization, join, monotonicity/termination argument, invalidation and convergence condition. Do not create an opaque general-purpose pass manager or iterate all analyses until no arbitrary field changes.

Preserve checking-only replay and abandoned-call paths. They may check errors on paths absent from runtime execution, but cannot finalize runtime placement, add runtime predecessors or execute cleanup. Likewise, retain the distinction between capturing a result before cleanup and delivering it after cleanup; a divergent cleanup does not rewrite the checked result Type.

### 6.3 Diagnostics and editor facts

Solvers record code/category, cause, operation/Place/contract IDs and source anchors through the existing diagnostic vocabulary. Publication maps those facts to locations, prerequisites and existing evidence. Preserve the existing externally observable source/failure grouping and Semantics-case merging during extraction (`OwnershipBody.ReportIssue`/`MergeIssue`). Internal occurrence IDs distinguish execution facts; they must not turn one intentionally merged source diagnostic into one diagnostic per replica or case. Any later change to public issue grouping is a separate behavior change with explicit expectations, never an incidental consequence of new IDs.

Move diagnostic formatting, source spelling and per-case presentation out of solver code. Preserve independent errors, resource outcomes and the existing guarantees; do not enrich repair promises as part of this reorganization. The detailed Diagnostic Development Workflow is not activated by this plan.

Hover and documentation projection may combine syntax headers/comments with a finalized semantic projection at the adapter boundary. They must not obtain whole mutable Binding/analysis workspaces. Analysis, lowering and emission remain independent of optional documentation collection.

Preserve the documentation facade's pre-Binding `Unclassified` result and its refresh after declaration changes. A projected declaration descriptor supplies parameter namespaces, receiver role, unsafe requirement and selected/public fragments without making rendering start Binding.

## 7. Whole-tree disposition

Every current Compiler area has a destination. New folders below are conceptual; finalize namespace/file moves only after ownership and dependency tests are in place.

| Current area | Target ownership and work | What establishes completion |
| --- | --- | --- |
| `Core/` | Thin compilation session, input/configuration identity, module registry, stage capabilities and workspace leases; keep target/source preparation separate from phase algorithms. | Source edits, rebinds, test-mode changes, failure and cancellation invalidate exactly the permitted generations; no stale verified handle is accepted. |
| `Lexing/` | Tokenization, layout, literal scanning, recovery and source spans. Keep ordinary lexing as the documentation candidate source. | No dependency on semantic analysis/backend; token/recovery/collection behavior and existing allocation bounds preserved. |
| `Parsing/`, including all `Koto/` node families | Grammar, recovery, syntax writing/serialization and source structure. Extract semantic fields/queries into the Binding workspace and source-to-semantic index. Split `Parser` by grammar responsibility with shared cursor/recovery contracts. | Syntax nodes no longer serve as the persistent semantic database; formatter/reload fidelity and early structural checks remain. No duplicated parser. |
| `Binding/`: scopes, modules, declarations, access, duplicates and headers | Declaration index/completion services with explicit environments and independent workspaces. | One owner of declaration completion and access identity; provisional state cannot escape as a finalized contract. |
| `Binding/`: Types, lengths, Origins, constraints, conformance, associated inference, specialization and inheritance | Normalized semantic tables plus pure proof/query services; isolate candidate-local inference and committed evidence. | Query inputs identify all premises/substitutions; no source-parent context recovery downstream; deterministic cycle/pending outcomes. |
| `Binding/`: expressions, calls, operators, adaptations, properties, elements, patterns, closures and effects | Selection services publish committed operation descriptors to the semantic builder; effect completion has an explicit dependency graph. | One evaluation/acquisition/target plan per operation, reused by checking and lowering without selection fallback. |
| `Binding/`: diagnostics, Hover and Kimi catalogs | Diagnostic/editor adapters separate from semantic engines; retain one library identity/signature catalog and its source/bound validation. | Solvers do not render Hover/diagnostics; compiler-managed identities are explicit semantics and library bodies still use ordinary compilation. |
| `Analysis/`: control flow | Keep front-end structural flow/type-completion assistance separate from finalized semantic CFG analysis; share the resolved transfer/result facts. | Early `PendingBinding` and recovery still work; post-finalization analyses do not walk Koto or independently rebuild execution meaning. |
| `Analysis/`: ownership bodies/builders/cases/defaults/instances | Semantic body construction and interpretation move to their own boundary; body data, workspace and analysis outputs separate as in §6. | Every analyzer has explicit inputs/results and no syntax, Binding or global mutable body service dependency. |
| `Emission/`: generic collection, layout, object/virtual plans, storage and ABI | Split semantic instance/contract completion from target representation planning; descriptors carry field/base/drop/runtime identity and dispatch facts. | No semantic proof or syntax interpretation in target planners; bounded instance discovery and all required witness/library bodies preserved. |
| `Emission/`: body lowering and physical models | Verified semantic operations -> physical instructions, slots and cleanup; builder queues/maps separate from final `EmissionModule`. | Lowering cannot request a language decision from Binding; physical module is transitively closed. |
| `Emission/`: LLVM writer, Windows runtime/profile, templates, artifact publication and native tools | Preserve serialization and runtime contracts; separate target data, runtime helpers, artifact IO and process execution behind explicit inputs. | Writer consumes only physical IDs/data; native tools do not depend on compiler workspaces or decide semantic acceptance. |
| `Documentation/` | Keep comment parsing, document syntax, links and Markdown rendering as an optional source/editor subsystem; semantic association uses detached IDs/facts. | Collection on/off changes no program meaning, analyses or output; published documents do not retain mutable semantic workspaces. |
| `Helper/` | Classify every helper by source/text, syntax, semantic or target responsibility. Move it beside its owner; keep only genuinely phase-neutral primitives shared. | No helper accepts a broad `Compilation`/Koto parameter to bypass a forbidden dependency. Preserve Unicode data ownership and scanning performance. |
| Compiler-root `AbstractTypes`, `ComparisonTypes`, `FloatingTypes`, `FormattingTypes`, `ObjectTypes`, `ReferenceTypes`, `ScalarTypes`, `SharedReadTypes`, `SlotTypes` | Canonical semantic Type/capability queries; representation-specific queries belong with the target. | Classification uses semantic descriptors and explicit context; no repeated syntax classification in analyses. |
| Compiler-root `ElementAccess`, `DefaultParameters`, `MatchTypes`, `StructStorage`, `EnumStorage` | Split syntax recognition, semantic plans and physical storage by their actual inputs. Keep the canonical rule in its owning phase. | All cross-phase consumers use published selection/projection/default/member facts. |
| Compiler-root `StaticScalar`, `StrictBestCandidate` | Place exact constant/value operations with semantic evaluation and ranking mechanics with candidate selection. | No new shared utility layer that combines language policy, mutation and backend representation. |

A feature may legitimately have files in several phases. Orthogonality means that each phase owns a distinct decision and consumes the previous phase's contract, not that all files mentioning a feature are moved into one class.

## 8. Physical lowering and emission closure

Move semantic instantiation and witness collection out of target lowering. Publish a generation catalog identifying requested concrete bodies, selected implementations, imports, runtime tests, destructor requirements and object/virtual dispatch relationships. The catalog retains pending/failure facts until completion, with bounded discovery and requesting locations.

Discovery is iterative. Representation or body lowering may discover another destructor, library helper or object descriptor and submit a typed dependency request to a bounded generation coordinator. That coordinator completes the semantic instantiation and required verification, then supplies the resolved representation/entry. Lowering cannot satisfy the request by calling Binding or inspecting syntax. The working catalog may be incomplete; only the finalized catalog and emission module claim closure after all requests converge or produce a recorded failure.

Representation planning consumes that catalog and verified semantic bodies. Keep logical fields, move paths and cleanup order independent of physical offsets, tag layouts, zero-size ABI omission and erased runtime Origins. A physical representation cannot erase semantic dependencies required to prove correctness.

Body lowering chooses registers/slots, addresses, ABI argument/result passing and physical control flow. It implements the supplied operation and cleanup plan. It may reject an unsupported target representation with the existing reporting contract, but it cannot reinterpret source syntax or prove an unproven language guarantee.

Keep `LlvmModuleWriter` as a serializer over resolved physical operations. Strengthen its input boundary by moving `SourceCalls` and `PendingEntries` into the emission builder and replacing `DictionaryHelper.Related` with resolved physical ABI/function references. Validate module closure before writing: every operand/function reference resolves, call/definition ABIs agree, blocks have valid terminators, every needed body/helper is present, generation requests are exhausted, and no build queue or semantic callback remains.

Artifacts and native tools take closed artifact inputs and target/toolchain identity. Preserve import providers, runtime templates, source/build manifests, deterministic naming and publication behavior. Runtime helpers remain subject to the Kimi-library implementation policy; this work does not move ordinary collection algorithms into hand-written IR.

## 9. Invalidation, allocation and resource bounds

The session owns immutable input identity and reusable mutable storage. A published semantic/analysis view has a generation and a bounded lifetime; buffers cannot be reset or reused while a consumer holds that view. Detached editor outputs copy only the facts they need or retain an explicitly owned immutable snapshot.

Invalidation follows the dependency chain: syntax/configuration/dependency changes revoke Binding completion, affected semantic views, analysis certificates, instance catalogs and physical plans. Until fine-grained dependency tracking is proven, retain conservative invalidation. Do not introduce persistent incremental reuse as an accidental side effect of this refactor.

Keys include all relevant inputs: definition and dependency generation, selected contracts/witnesses, Type/length/Origin substitution, proof environment, Semantics case, default/deferred occurrence where relevant, target profile and test/product mode. Do not use syntax reference equality or result Type alone as a cache key.

Performance requirements:

- Reuse compact indexed tables, packed states, adjacency storage and work queues. Prefer small value records and spans/views; avoid a heap object or iterator allocation per operand/edge/operation.
- Share immutable Type, signature, contract and context data at module scope. Do not replicate full declaration graphs per body or Semantics case.
- Avoid retaining complete bound-expression and semantic-CFG copies after publication. Temporary builder representations have a release point.
- Set checked bounds for tables, edges, contexts, case expansion and instance discovery. Preserve located resource/unsupported outcomes; exhaustion cannot look like success or an absent effect.
- Measure both cold compilation and warm reanalysis, retained bytes/capacities, operation/edge/context counts and scaling. A smaller method is not evidence of a faster or smaller compiler.
- Preserve existing zero-allocation/reuse assertions and fixed workloads. Do not add warm-up or relax a limit to make a new architecture pass.

Physical assembly/project separation is not a prerequisite. Begin with explicit C# contracts and automated dependency checks in the existing build. Introduce another assembly only if it improves enforceability without unacceptable startup, build or allocation costs; folder moves alone establish no boundary.

## 10. Migration sequence and removal gates

Use small verified replacement units within the following stages. Each stage has an explicit old-path removal condition. A temporary adapter belongs at the producing boundary, never as a syntax fallback inside an analyzer or lowering routine. Do not merge an operation route that has two permanent semantic authorities.

| Stage | Dependencies | Deliverable | Completion and deletion gate |
| --- | --- | --- | --- |
| U0: Inventory and baseline | None | Classify every Compiler file and cross-phase dependency; record operation/value kinds, hidden context reads, current analysis schedule, existing failures and fixed workloads. Specify IR contracts and migration ledger. | Every dependency has an owner and destination; baseline evidence distinguishes known defects/Unsupported from regression expectations. No code reorganization before this map is usable. |
| U1a: Semantic identities and readiness | U0 | Shared semantic/source identities, explicit proof/interpretation contexts, phase capability/generation contracts and source attribution adapter. Begin dependency guard with a finite migration allowlist. | Identity/lifetime rules are enforced. Old model bridges are producer-owned and individually tracked. |
| U1b: Semantic contract closure | U1a | Publish syntax-free Type/Origin/declaration/property/witness descriptors and proof-query inputs needed by each migrating route, using the existing Binding decisions. | Every route's reachable contracts and queries are closed before its consumer migration is complete. A wrapper around syntax-bearing Bound records does not pass. |
| U2: Calls and updates end to end | U1a and U1b for these routes | Normalize call input mappings, result-source contracts, evaluation steps and update descriptors; route initialization, borrow contents and physical calls/updates through them. | Remove `ResultArgument`/receiver/backward-entry reconstruction for migrated call families and every downstream swap/exchange syntax test. No family left on an unrecorded fallback. |
| U3: Shared operand and transfer facts | U2 | Generalize the existing totality classifier; cover values, stores, aggregates, patterns, captures, retention and grouped updates. Migrate dependency and authority propagation independently. | The two analyses no longer rediscover source/destination operands. All operation/value kinds have explicit tested flow contracts; separate analysis policies remain. |
| U4: Complete semantic body construction | U1a–U3, including U1b for all body domains | Move expression/call/loop/pattern/cleanup construction out of `OwnershipAnalysis`; normalize Place paths, default/case occurrences, conversions and result/control-flow semantics. | All supported executable forms produce the common closed body and duplicate construction paths are removed. Structural front-end flow retains its distinct purpose. Syntax dependence inside still-unmigrated consumers remains tracked until U5/U8. |
| U5: Analysis ownership and schedule | U3–U4 and complete U1b analysis contracts | Extract the owners in §6 with typed result views, bounded shared mechanics and explicit convergence groups. Separate issue recording/publication. | `VerifyBorrows` becomes coordination over owned analyses; no shared catch-all state object or hidden phase mutations. Solver inputs can be supplied without syntax/Binding, including transitive queries. |
| U6: Binding internal decomposition | U1a–U1b; use U4 contract feedback without delaying U1b closure | Decompose declaration/candidate/type/proof/selection services behind the published contracts, move semantic fields out of Koto and isolate Hover/diagnostic adapters. | Candidate state is isolated; each store, completion step and invalidation has one owner. No obsolete semantic fields or persistent syntax-backed alternatives remain. |
| U7: Generic and instance closure | U4–U6 | Normalize semantic default templates, compose contexts, retain case checking, separate instance discovery/proof dependencies from physical generation. | Defaults/cases/instances use common semantic descriptors and explicit contexts; no AST replay or selection fallback downstream. Definition-time guarantees and located instance failures remain. |
| U8: Backend semantic closure | U5–U7 | Convert all layout/storage/object/virtual/ABI/body lowering inputs to verified semantic descriptors; close `EmissionModule` transitively. | No Binding queries, declaration/syntax casts, implicit operand scans or semantic proof callbacks in physical planners/writer. Builder queues/maps are absent from published physical modules. |
| U9: Remaining source and service boundaries | U6–U8 | Finish Core, Parser, Helper, Documentation and editor/diagnostic boundary cleanup; update necessary external callers. | Whole-tree disposition in §7 is complete, all temporary bridges and dependency allowlist entries removed, exact snapshot/reuse behavior verified. |
| U10: Whole-compiler closure audit | U0–U9 | Re-run dependency inventory, operation totality, end-to-end regressions, performance comparisons and final documentation review. | All completion conditions in §13 hold. Delete obsolete model fields, switches, helpers, adapters and dual-path test infrastructure. |

U1b closes contracts by domain and is a real prerequisite for each migrated consumer, not a promise deferred to U6. U6 can progress beside U2–U5 after those contracts stabilize; source-only Lexer/Documentation boundary work can progress independently. U4/U5 may consume bodies built by the front-end adapter; U7 subsequently removes source replay when producing default/case/instance variants. Shared files and phase contracts need one owner per unit. U8 must not bypass U6/U7 with a read-only wrapper around Binding.

For each migrated feature route, the ledger records: original producer/consumers, new canonical record, context/proof dependencies, verification selection, remaining bridge and the stage that deletes it. The allowlist can only shrink, except for an explicit reviewed correction to the initial inventory; new features must use the new contract.

At each cutover, keep the old and new paths only long enough for focused comparison. Compare normalized semantic observations and externally required behavior, not incidental operation numbering. Delete the old production route when the replacement passes. If a defect is discovered, use the specification-derived expected result; agreement with existing wrong code is not success.

If a unit cannot complete, preserve the last working production route and an explicit unfinished ledger entry. Do not ship partial semantics as a supported path. This is an incremental replacement of the architecture, not an indefinite accumulation of compatibility layers.

## 11. Verification design

### 11.1 Boundary and semantic-model checks

Extend existing owning tests and helpers. Add a dedicated architectural test only for a boundary that has no current owner; do not build a new testing framework.

1. **Dependency enforcement:** use resolved C# symbol/type dependencies where practical, not only namespace text searches. Check contract fields, method signatures, callbacks and implementation calls. Forbid transitive syntax/Binding/Compilation access in closed models, analyses and physical lowering. Source provenance adapters have explicit allowed inputs.
2. **Closed-input execution:** run focused analyses and lowering from a constructed/published semantic view with no syntax or Binding access. Assert expected facts/results, not merely that execution succeeds.
3. **Operation totality:** extend `SharedEngineTotalityTest` to check operand, transfer, read/write and context coverage, including explicit no-flow cases. Unhandled enum values and missing descriptors fail deterministically.
4. **Call independence:** vary named/receiver/default argument arrangement, insert unrelated nonsemantic bookkeeping, and reorder internal storage without changing evaluation edges. Input identity and result ancestry must not depend on adjacency or source-reference equality.
5. **Source attribution:** the same source anchor used by separate defaults, deferred expansions or cases produces distinct execution facts and correctly related diagnostics.
6. **Invalidation:** edits, rebinding, failed analysis, case/instance changes and test-mode changes reject old certificates and agree with a cold run. Optional documentation collection does not alter semantic results.
7. **Publication failure:** invalid, pending, unsupported and resource-limited bodies never reach verified physical lowering. Internal malformed IR is distinguished from a user-language error.
8. **Physical closure failure:** malformed final modules with missing operands/functions, ABI disagreement, invalid terminators or unresolved generation requests publish neither LLVM nor artifacts. A following valid preparation must succeed without retaining the failed module's state.

Dependency guards prove a boundary, not the semantic correctness of a transfer rule. Pair them with positive and negative behavior at the lowest layer that observes the rule.

### 11.2 Representative behavior matrix

Select applicable dimensions without an indiscriminate full cross-product. Existing classes below are starting points; U0/U-stage discovery must select actual methods, regenerated fixtures and harnesses affected by the unit.

| Area | Required distinctions | Existing starting points |
| --- | --- | --- |
| Calls/result ancestry | Direct/value/requirement/callable receiver, named order, inline/nested defaults, receiver/environment/fixed inputs, multiple independent inputs with equal Origins | `BorrowedInputResultTest`, `BorrowedDefaultTest`, `DefaultCallTest`, `DefaultOwnershipTest`, `GenericGenerationDiagnosticTest` |
| Updates and transfer facts | Copy/Move/Reborrow, swap/exchange, alias/self cases, old/new contents, retained slot authority, aggregate and indirect stores | `SharedEngineTotalityTest`, `BorrowedFieldUpdateTest`, `StoredReborrowAncestryTest`, `SharedObjectOwnershipTest` |
| Places and construction | Static/dynamic selectors, field/base/enum/tuple parts, slot versus referent, borrowed collection, partial Move, zero-sized values, abandoned construction | `ElementPathEmissionTest`, `ElementMoveEmissionTest`, `ElementReplacementEmissionTest`, `EnumOwnershipTest`, `StructEmissionTest` |
| CFG and cleanup | Branch/loop joins, unreachable/checking-only continuations, abandoned argument evaluation, guard Loans, result before cleanup, divergent defer, exactly-once destruction | `ControlFlowAnalysisTest`, `ControlFlowConformanceTest`, `ContinuationVerificationTest`, `DeferredEmissionTest`, `ConsumingCallableCleanupTest` |
| Generic interpretation | Every admitted case, conditionally absent slots, composed default context, concrete versus generic agreement, selected implementation and failed instance attribution | `GenericOwnershipCaseTest`, `GenericCaseDiagnosticTest`, `GenericCaseAllocationTest`, `GenericDefaultTest`, `NestedDefaultTest`, `SemanticsDefaultTest`, `GenericGenerationDiagnosticTest` |
| Representation/backend | Logical versus physical fields, aggregate/scalar/zero-size ABI, object/virtual/destructor plans, imports, runtime checks, O0/O2 effect order | `EmissionPlanTest`, `AggregateFunctionEmissionTest`, `ObjectRuntimeTest`, `SealedObjectFinalizationTest`, `GenericObjectFactoryTest`, `NativeToolchainTest`, `EmissionArtifactsTest` |
| Services/reuse | Exact snapshots, detached Hover, optional comments, diagnostic causes/order, failed-request recovery, retained storage bounds | `DiagnosticContractTest`, `HoverProjectionTest`, `HoverCollectionTest`, `BorrowDependencyStorageTest`, `VirtualPlanReuseTest` |

Use `CompilationTestHelper`, `DiagnosticCorpus`, `TestDiagnostics`, existing emission helpers and `AllocationMeasurement`. Do not duplicate CLI/LSP rendering assertions in each ownership test. Native fixtures are required where runtime behavior, ABI/layout or cleanup depends on generated code, not for every semantic table row.

### 11.3 Formal verification and measurements

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). Each compiler unit requires focused Unit verification and one final Session per implementation session. Native fixtures and milestone harnesses are explicitly selected; a Session name alone does not cover them. No NativeAOT runs are planned.

Before changing a unit, select its tests, fixture patterns, affected existing milestone harnesses and measurements. During edits use incremental feedback; completion evidence comes from Verify's non-incremental Release build and the required checks. Do not edit sources while builds or verification run.

The final Session may cover the last Unit only when it verifies the same source/configuration and every required Unit check. Associate successful evidence with the exact commit before pushing. Keep failures in `artifacts/verify/` and measurements in `artifacts/benchmarks/`.

Use the existing fixed workloads for borrow storage, verification continuations, pair cases, generic defaults, element Places, Origin proof, associated inference, adaptation, inheritance, objects and Hover as applicable. Record timing distributions from matched repeated runs, allocated bytes, retained capacity and growth with input size. Establish thresholds from U0 baseline variation before comparison; do not promise a speedup or accept an unexplained regression because the code is more modular.

Known baseline failures, especially recorded wrong-code findings, are tracked with their owning work. This project must not certify those behaviors by preserving their output. If one blocks required verification, the affected unit remains incomplete until the cause is resolved and its scope/evidence is explicit.

## 12. Adoption and documentation responsibilities

This proposal is not an adopted implementation schedule. When implementation is authorized, first reconcile the stage order with active PLAN work without silently resuming paused work or changing milestone completion states.

Adopt the architecture's completion rules into implementation guidance, not into language syntax rules. Add a concise permanent architecture contract under `docs/dev/`, with dependency rules, phase input/output/readiness and model ownership. Update CODEMAP in each unit that changes entry points, phase responsibilities or navigation. Avoid copying the detailed migration ledger into PLAN; keep PLAN below 200 lines and record only current scope and next actions.

Update STATUS only if a verified support boundary changes. Update SPEC/IMPL only when an actual normative requirement changes or needs a deliberate clarification, with rationale, affected examples/tests and proposal intake recorded in `draft/INTEGRATED.md`. The present refactor needs no language version change. Do not weaken rules to fit a partial implementation.

Library declarations/conventions change only if separately needed, with LIBRARY/STYLE updates under existing rules. Record a few lines per implementation session in PLAN_HISTORY; detailed evidence and stage removals belong in commits and artifacts. Frozen proposals stay unchanged. At final disposition, record this proposal's section-by-section treatment and move it according to the established draft lifecycle.

## 13. Completion conditions

A stage is not complete just because existing tests pass. The entire reorganization is complete only when all of the following hold:

1. **Whole-tree accounting:** every Compiler source/supporting file has an explicit owner and permitted dependencies; the U0 inventory has no unassigned residue.
2. **Semantic closure:** post-normalization analyses and physical lowering obtain every language-semantic fact from the semantic module, explicit context and analysis results. No direct or transitive syntax/Binding access remains there.
3. **Canonical operations:** every supported operation has complete operands, evaluation/transfer meaning, effect inputs and context. No backward operation scan, source-node equality or parent syntax determines its meaning.
4. **Shared flow, separate rules:** initialization, dependency, authority, liveness and effect policies consume common operation facts but retain their own domains, joins, checks and result ownership.
5. **Explicit scheduling:** result dependencies, convergence groups, readiness and publication are documented and enforced. No analysis must understand another's private scratch state or incidental call order.
6. **Correct contexts:** inline defaults, deferred expansions, generic cases and instances retain separate occurrences and composed proof environments. Conditional premises and actual Loan identity remain sound.
7. **Closed backend:** verified semantic inputs determine physical plans; final physical modules contain no semantic work queues, syntax handles or semantic callbacks. The LLVM writer only serializes.
8. **Safe reuse:** old generations cannot certify new inputs; repeated/cold checks, failures and optional documentation agree within their contracts. Storage lifetime and resource bounds are explicit.
9. **Behavior and cost:** required tests, native O0/O2 fixtures, selected harnesses, Session verification and relevant fixed-condition measurements pass with exact evidence. Known unresolved work is not relabeled complete.
10. **Removal:** old interpretation paths, duplicated operand mapping, compatibility adapters, temporary allowlists and redundant retained models are deleted. A permanent fallback to the old architecture fails completion.
11. **Documentation:** the permanent architecture contract, CODEMAP, applicable support/spec records and evidence accurately describe the implemented result.

## 14. Completion rule for every later feature

Add the following requirements to the ordinary implementation workflow after adoption:

- Identify the phase that decides each new semantic fact and the canonical record that carries it.
- Define the operation's operand roles, transfer description, interpretation/proof context and validity prerequisites before adding downstream consumers.
- Show that each existing consumer can use those facts without learning the new source spelling or rediscovering a call/storage form.
- When a genuinely new semantic operation needs analysis behavior, add its explicit schema row and each applicable analysis policy. Do not hide it behind an old tag with syntax-sensitive exceptions.
- Add focused positive/negative and totality/boundary checks, relevant native execution, and allocation/reuse measurements under the ordinary verification rules.
- Remove superseded interpretation code in the same completed unit. Tests plus an unremoved second semantic authority do not establish completion.

The review question is concrete: **Can the affected analyses and backend process the new operation from the published semantic records alone, and is each decision owned in exactly one place?**

## 15. Main risks and chosen responses

| Risk | Response and evidence |
| --- | --- |
| A supposedly closed model hides syntax through Types, Origins or delegates | Enforce transitive dependency rules and exercise consumers without syntax/Binding access. |
| Common transfer edges accidentally equate Loan dependency and authority | Preserve distinct domains and tests for equal Origins, independent inputs, slot/content authority and last-holder recovery. |
| A linear pipeline loses pending proofs or recursive effects | Use explicit prerequisite work groups and readiness capabilities; selected targets remain fixed and success remains withheld. |
| Extracting passes changes initialization/borrow/cleanup ordering | Record the existing schedule first, extract with matching behavior, then change only a documented dependency with focused regressions. |
| Normalization loses evaluation order, abrupt exits or result capture | Explicit occurrence IDs, preparation steps, runtime/checking edge classes and cleanup/delivery plans; native effect-order fixtures. |
| Case/default normalization changes accepted generic semantics | Preserve finite case checking and inline default context composition; compare ordinary bodies, declaration defaults and caller replicas. |
| Extra IR layers increase allocation and retention | Shared tables, bounded workspaces, builder release points, compact views and fixed cold/warm/scaling measurements. |
| Migration becomes permanent dual infrastructure | Per-route deletion gates, a shrinking allowlist and no new-feature fallback; final closure audit requires zero residue. |
| Architecture work conceals existing unsupported or wrong-code paths | Keep specification-derived expectations and independently owned gaps visible; never use existing output as the only oracle. |

The plan intentionally settles the semantic boundaries and migration gates now. Record layout, sparse versus dense indexing, assembly partitioning and exact workspace pooling remain implementation choices, decided against the same contracts and measurements rather than by reopening the architecture.
