# Compiler architecture

The compiler reorganization is active. This document separates enforced boundaries from migration work; it does not
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
| U3 | Pending | Common operand/transfer facts, independent dependency and authority policies. |
| U4 | Pending | One semantic body constructor for all supported forms; remove duplicate construction paths. |
| U5 | Pending | Independent analysis result ownership, explicit convergence and diagnostic publication. |
| U6 | Pending | Decompose Binding internals behind closed contracts; remove persistent semantic fields from syntax. |
| U7 | Pending | Semantic default/case/instance templates; remove downstream source replay. |
| U8 | Pending | Close representation/lowering inputs and the physical module, including construction queues/maps. |
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

U1b closure is a prerequisite for each migrated consumer. Moving all Binding internals need not precede a vertical
slice, but an existing syntax-bearing record cannot be called closed merely because it is exposed read-only.

## Inventory and measurement

The implementation starts from 462 files, 6,324,004 recursive file-content bytes, 455 C# files and 134,706 C# lines
(including comments/blank lines) under `src/Kimi/Compiler`. These are payload bytes, not allocated filesystem clusters.
The before manifest and file hashes are retained under `artifacts/benchmarks/compiler-reorganization-2026-10-09/`.
Measure the same recursive scope after changes; report C# and whole-folder totals. Moving code outside Compiler,
changing line endings or removing explanatory comments is not semantic consolidation and must not count as its benefit.

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
