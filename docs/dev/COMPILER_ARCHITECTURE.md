# Compiler architecture

The compiler reorganization is paused at the user's request (2026-10-10). This document separates enforced boundaries from migration work; it does not
claim that the current compiler already has a closed semantic IR. Required behavior remains in SPEC/IMPL, navigation
in CODEMAP, verified support in STATUS, and execution/verification rules in AGENTS and VERIFICATION.

## Contracts

Resolve an operation's meaning once and retain its operands, evaluation order, selected target, acquisitions,
transfers, proof context and source attribution. Subsequent analyses consume those facts. Source attribution may
retain syntax for diagnostics and editing; it must not become a second way to decide operation meaning.

The target boundaries are:

| Owner | Input and output |
| --- | --- |
| Source front end | Snapshots and configuration -> tokens, selected syntax, recovery and structural flow |
| Binding and semantic construction | Syntax and explicit environments -> completed declaration contracts and semantic bodies |
| Body analyses | Semantic bodies and declared assumptions -> converged initialization, Loan, authority, liveness, effect and cleanup facts |
| Generation coordinator | Verified templates and substitutions -> bounded concrete instance/dependency discovery |
| Representation and lowering | Verified operations and target -> layout, ABI, physical functions and helpers |
| LLVM writer | Closed physical module -> LLVM text |
| Artifact/native services | Closed artifacts and toolchain inputs -> published/build/execution results |
| Editor/diagnostic projection | Retained facts and source associations -> detached public output |

Closure is transitive: Types, Origins, witnesses, properties and callbacks cannot conceal a route back to syntax or
mutable Binding. Each stored fact has one owner, generation, lifetime and invalidation rule. Reusable capacity does
not imply reusable semantic certification. Candidate-local state cannot escape as a completed contract.

Share operation/operand/transfer descriptions, not the rules of separate analyses. Dependencies, retained authority,
initialization and liveness have different joins and validity rules. Preserve the coupled dependency/authority/
retention convergence through explicit result updates, followed by liveness and per-root Loan/content flow.

Runtime and checking-only CFG edges remain distinct. Checking replay may model cleanup effects but cannot create
runtime predecessors, cleanup executions or placement decisions. Secure results before cleanup and deliver them
after cleanup. Preserve diagnostic source/failure grouping and case merging during extraction.

Generic definitions retain finite Semantics-case verification. Inline defaults retain prepared-slot protection,
evaluation order, cleanup, and their composed interpretation context. Instantiation never reopens overload selection
or repairs an invalid generic definition. Pending prerequisites cannot publish successful verification.

Physical planning may submit typed requests for additional instances/destructors/helpers to the generation coordinator.
It cannot perform semantic lookup itself. Final publication requires every request and physical reference to be
resolved, ABI agreement and valid control flow. Failure clears earlier success.

Prefer removing duplicate interpretation over introducing abstractions. Compact indexed tables and reusable workspaces
remain the default. A moved method or a new interface does not establish an ownership boundary.

## Migration ledger

Stages are implemented as small verified replacement units. Only completed entries below describe enforced changes.
Every migrated route removes its old interpretation; temporary producer adapters have a named removal stage.

| Stage | State | Scope and remaining removal gate |
| --- | --- | --- |
| U0 | Active | Whole-tree inventory, byte/hash baseline and dependency survey. Baseline revision: `620b2a1a`. |
| U1a/U1b | Pending | Semantic identities, readiness and transitively closed declaration/proof contracts, per migrating domain. |
| U2 | Active | Normalize call/update meaning end to end; remove call ancestry reconstruction and update syntax tests. |
| U3 | Active | Common operand/transfer facts, independent dependency and authority policies. |
| U4 | Pending | One semantic body constructor for all supported forms; remove duplicate construction paths. |
| U5 | Active | Borrow liveness owns its result/workspace; remaining dependency, authority, conflict and diagnostic ownership is pending. |
| U6 | Pending | Decompose Binding internals behind closed contracts; remove persistent semantic fields from syntax. |
| U7 | Pending | Semantic default/case/instance templates; remove downstream source replay. |
| U8 | Active | Physical module storage no longer retains semantic references or pending construction entries; representation/lowering input closure remains pending. |
| U9/U10 | Pending | Complete remaining source/service boundaries; remove all adapters and audit closure/costs. |

The first U2 route uses `ReplaceBorrowed`, `ExchangeBorrowed` and `SwapBorrowed` operation kinds for both direct owned
Places and borrowed storage. The direct-owner expansion is removed. Initialization, content transfer, string ownership
flags and physical update selection consume the operation kind. `IsWholeUpdate` identifies the shared operand shape;
it does not erase the distinct transfer behavior. Remaining Binding/type/provenance queries keep U2 and U1b open.

Calls now retain an indexed input range in evaluation order, parameter slots, default acquisitions, verified input
Types and the selected target. Construction records the declared result-source rule and the callable environment's
read/retention capability. Result ancestry and call retention use those records, including recursive default reads;
they no longer reconstruct input correspondence by walking preceding operations or reading invocation syntax.
`CallAt` uses the recorded target, with no source fallback. These records still contain BoundCall/BoundType and therefore
do not establish U1b closure. Physical argument validation and remaining source-based consumers are still open U2 work.

`OwnershipFlow` owns operation/value flow categories and compact source/destination ranges. Abstract-effect dependency
propagation and retained-authority propagation share that correspondence while selecting their own applicable transfers
and joins. Prepared payloads of abandoned constructions retain their existing dependency treatment.
`BorrowLiveness` owns the live-holder table and holder index; it reads the body, converged dependencies and explicit
runtime/checking edges without modifying dependencies or diagnosing conflicts. Cleanup observation is a Place fact
computed during semantic construction. The body still coordinates dependency/authority convergence and conflict checks,
and the remaining analysis inputs are not yet transitively closed.

`EmissionModule` stores physical function references by index. Source-call deduplication and the pending-instance
cursor belong to `GenericStoragePlan`, whose generation work is drained before publication. The redundant pending
list and repeated searches are removed. Module completion rejects unresolved Dictionary helper references and clears
prior success on failure. `MinimalEmissionTest` checks the transitive stored-type graph for semantic/syntax references;
`DictionaryLibraryTest` checks rejection and successful repreparation. This is a storage boundary, not proof that
lowering inputs are closed or that every physical opcode, ABI and control-flow invariant has a final verifier.

Resume with the remaining call consumers (`LocalRegions`, `Defaults`, `Updates`, `Reservations` and physical argument
validation), closed Type/target/proof contracts and the corresponding builder routes. Dependency/authority convergence,
conflict checking and diagnostic publication still need separate state owners. Binding, default/instance generation,
lowering and source/service boundaries remain in the ledger above; this checkpoint does not complete U0-U10.

U1b closure is a prerequisite for each migrated consumer. Moving all Binding internals need not precede a vertical
slice, but an existing syntax-bearing record cannot be called closed merely because it is exposed read-only.

## Inventory and measurement

The implementation starts from 462 files, 6,324,004 recursive file-content bytes, 455 C# files and 134,706 C# lines
(including comments/blank lines) under `src/Kimi/Compiler`. These are payload bytes, not allocated filesystem clusters.
The before manifest and file hashes are retained under `artifacts/benchmarks/compiler-reorganization-2026-10-09/`.
Measure the same recursive scope after changes; report C# and whole-folder totals. Moving code outside Compiler,
changing line endings or removing explanatory comments is not semantic consolidation and must not count as its benefit.

The 2026-10-10 checkpoint uses the same scope and retains per-file hashes in `after.json` and deltas in `summary.json`
beside the baseline. LF-normalized bytes exclude the checkout's mixed line endings; no code was moved outside Compiler.

| Measure | Before | Checkpoint | Reduction |
| --- | ---: | ---: | ---: |
| Folder payload bytes | 6,324,004 | 6,313,352 | 10,652 (0.168%) |
| LF-normalized folder bytes | 6,283,538 | 6,277,454 | 6,084 (0.097%) |
| C# payload bytes | 6,168,600 | 6,157,948 | 10,652 |
| C# lines, including comments/blanks | 134,706 | 134,591 | 115 |
| Files (C# files) | 462 (455) | 465 (458) | Three additional responsibility owners |

The normalized reduction is modest: duplicate interpretation was removed, while retained semantic facts and explicit
state owners were added. File splitting is not counted as a reduction. Whole-plan consolidation and final cost audits
remain unfinished.

| Current area | Destination responsibility |
| --- | --- |
| Core | Inputs, module registry, phase scheduling/readiness and workspace lifetime |
| Lexing/Parsing | Source grammar, directives, recovery, formatting/serialization; semantic decisions leave Koto |
| Binding | Declaration/index, candidate selection, Type/Origin/proof services, committed operation plans and optional projections |
| Analysis | Semantic body construction, distinct analysis workspaces/results, explicit scheduling and issue recording |
| Emission | Semantic instance coordination, target representation, physical lowering, closed writer and artifact/native services |
| Documentation | Optional source association and detached declaration projection; independent Markdown parsing/rendering |
| Helper/root utilities | Assign each function to source recognition, semantic queries or representation; eliminate cross-phase helper backdoors |

## Completion

Each feature must identify its canonical semantic record, context, operand/transfer contract, consumers and old code
removed. Operation totality, closed-input consumers, stale-generation rejection, positive/negative behavior and
relevant native execution establish different guarantees and must be checked at the owning layer.

The whole reorganization remains incomplete until all areas are accounted for, downstream semantic interpretation of
syntax is absent transitively, shared facts replace duplicate operand discovery, analysis state has explicit ownership,
physical publication is closed, and temporary adapters/allowlists are deleted. Follow ordinary Unit/Session verification
and fixed-condition allocation/reuse/performance requirements; retain unresolved support limits independently.
