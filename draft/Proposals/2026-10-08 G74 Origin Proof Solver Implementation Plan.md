# G74 Origin Proof Solver Implementation Plan

Date: 2026-10-08. Status: draft implementation plan; implementation and performance verification are pending.

Source inspection baseline: `35bbc6ba4a501ec7942e6ac3452313d4c4562f33`. The working tree was clean at the start of this planning task. Recheck the relevant code and concurrent changes before implementation.

This plan combines the responsibility separation proposed in the supplied `G74a.md` with the workload investigation and closure algorithm proposed in `G74o.md`. Those documents are design inputs, not specification authority or independently verified measurement evidence. This document is self-contained; implementation must use the current formal specification and repository workflow.

The present task creates this proposal only. It does not start implementation, close G74, resume paused G55/G60 work, change the milestone order, or integrate a specification change. Documentation-only verification consists of reviewing this file, its references and its diff; no build or test is required for this proposal.

## 1. Objective and scope

Replace path-dependent recursive Origin proof search with a finite calculation over explicit premises. Separate inference constraint collection from proof before changing the search algorithm. Make correctness, work bounds, allocation behavior and invalidation independently verifiable.

The completed change must:

1. Preserve the Origin and Loan rules in [SPEC Chapter 15](../../docs/spec/15-ownership-and-lifetime-analysis.md), especially §§15.3.4–15.3.7, §15.4.4 and §§15.6.1–15.6.5.
2. Avoid expanding the same proof state separately for different search paths within one stable proof environment.
3. Handle successful and unsuccessful checks, including successful programs that cause unsuccessful intermediate queries.
4. Preserve actual Loan identity, authority and raw-borrow anchors independently of Origin equivalence.
5. Preserve existing warm allocation guarantees and bound retained storage across edits and repeated analysis.
6. Associate acceptance, diagnostics, work counts and measurements with exact sources and configuration.

No language restriction, Origin syntax change, new public library declaration or inference resource-limit rejection is proposed. [SETTLED](../../docs/SETTLED.md) has been consulted: result-only universal Origins remain supported by the language design. A specification-valid form must not be prohibited to make the implementation faster.

This supports all four Kimigayo Principles: one proof engine serves its callers; explicit environments make reasoning local; fitting and proof have explicit responsibilities; source-specific evidence records what was verified and what remains uncertain.

## 2. Evidence and current responsibilities

### 2.1. Confirmed structure

The [Origin relations entry in CODEMAP](../../docs/dev/CODEMAP.md) identifies the following implementation boundaries.

| Component | Current responsibility and relevant issue |
| --- | --- |
| [Binding.OriginProof](../../src/Kimi/Compiler/Binding/Binding.OriginProof.cs) | `ProvesOriginOutlives` resolves Origins, collects initializer inference bounds, and recursively searches premises. `originProofPath` is removed on return, so it prevents cycles along a path but does not reuse completed subqueries across paths. |
| [Binding.TypeRelations](../../src/Kimi/Compiler/Binding/Binding.TypeRelations.cs), [Binding.OriginDeclarations](../../src/Kimi/Compiler/Binding/Binding.OriginDeclarations.cs), [Binding.OriginInference](../../src/Kimi/Compiler/Binding/Binding.OriginInference.cs) | Fit complete Types, resolve omitted Origins and collect or solve constraints. These are the owners to audit when moving inference effects out of proof. |
| [Binding.ResultPremises](../../src/Kimi/Compiler/Binding/Binding.ResultPremises.cs) | Definition-side result well-formedness and caller-side obligations. `VisitResultPremise` temporarily excludes a particular function's result premises. |
| [Binding.AssociatedOrigins](../../src/Kimi/Compiler/Binding/Binding.AssociatedOrigins.cs) | Associated formation and requirement premises, with declaration and scope boundaries. |
| [Binding.Diagnostics](../../src/Kimi/Compiler/Binding/Binding.Diagnostics.cs) | Repeats proof and judgment while finding failed operands and diagnostic chains. Diagnostic records themselves are use-specific. |
| [Binding.LocalRegions](../../src/Kimi/Compiler/Binding/Binding.LocalRegions.cs), [OwnershipAnalysis.Borrows](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Borrows.cs) | Combine relation judgments with inferred/finite regions and actual Loans. |
| [LlvmEmitter](../../src/Kimi/Compiler/Emission/LlvmEmitter.cs) | Rechecks Origin obligations before generation. |

Each premise can currently cause more recursive queries, while each query rediscovers premises from ancestor syntax and nested Types. This explains the risk of combinatorial repeated work. The exact asymptotic class of the current compiler has not been established by the supplied measurements.

`OpenInitializerInference` is especially significant: a recursive proof can update `Replacements` and add an obligation before another subgoal fails. Keeping the first part of `ProvesOriginOutlives` textually unchanged while replacing its later recursive calls does not preserve those effects. The new design must assign required constraints to actual fits instead of relying on which proof branches happen to run.

### 2.2. Historical and reported measurements

| Evidence source | Report | Treatment in this plan |
| --- | --- | --- |
| [PLAN G74](../../docs/dev/PLAN.md) | Historical 254 s / 292 s checks; removing `keep` reduced one case to 2.6 s. | Historical observations, not current baseline values. The abbreviated example must be reconstructed as a complete source. |
| `G74o.md` | Original `t_g1` completed in 0.97 s including process startup; 7 Holder inputs took 9.7 s and 8 exceeded a 60 s limit. | Preliminary reported observations from a build containing uncommitted changes, with one run per case. Reproduce before updating current support or performance claims. |
| `G74o.md` | A valid 7-input program took 5.7 s; profiles attributed much of the time to ownership-side Origin obligation proof. | Include valid programs and ownership verification in the workload. Reconfirm phase attribution. |
| Commit `0e6fd329` | Added direct-premise priority and skipped Origin-free targets as part of fixed-Origin Loan work. | Confirmed historical optimization; its commit message does not establish a general bound on proof search. |

Neither the reported speed of the old reproducer nor Program 26's historical warm Binding time closes G74. Retain the original case as a regression and add scalable families that isolate the remaining search behavior.

## 3. Required design

### 3.1. Separate fitting, proof and final judgment

Use three explicit responsibilities; final API names are implementation choices.

| Responsibility | Inputs and allowed effects | Result |
| --- | --- | --- |
| Collect fit constraints | Source and destination complete Types, variance, the actual fit/use and its inference owner. May add required bounds and obligations. | Constraints attributed to the fit that requires them. |
| Prove a relation | Resolved Origin expressions and a stable environment of permitted premises. May modify private scratch storage and counters only. | Entailed or not entailed by that environment. A failed proof is not a contradiction. |
| Judge an obligation | Proof result, fixed/finite/inferred classification, region bounds and the existing ownership rules. | Existing `Proven`, `Refuted`, `Unknown` or `Unrepresentable` judgment and use-specific diagnostics. |

Audit every `ProvesOriginOutlives` caller, including capability checks, invariant/reversed fits, calls, result-premise detection, local annotations and anonymous function headers. Record which operations legitimately collect constraints and which only ask for proof.

Resolve already published substitutions before proof. Where active inference remains, route required fitting constraints to their owner and take a new proof snapshot after relevant state changes. Do not freeze an unfinished inference variable as a universal Origin or turn its desired bound into an assumed premise.

Initially retain the old recursive algorithm behind the pure proof boundary to isolate the effect of responsibility separation. A pure proof call must leave substitutions, obligations and inference state unchanged on success and failure, including repeated calls and calls in a different order.

Any difference caused by removing speculative inference effects needs a minimized reproducer and a SPEC-based disposition. Preserve required fit constraints; do not preserve an accidental effect merely to match the old search. Conversely, do not delete an effect until its intended semantic owner is identified. No unexplained difference may pass this unit.

### 3.2. Stable proof environment and premise extraction

A proof environment identifies at least:

- Source/Binding generation and the relevant binder identities.
- The use and its lexical/constraint scope, including local Place eligibility.
- Visible completed declaration, input, result and associated premises.
- The substitution state used to resolve every query and premise endpoint.
- The identity of `resultPremiseExcluded`, rather than only a boolean flag.
- Anonymous-header completion and the Semantics conditions used to admit pair-layer premises.

Start with one environment per top-level proof request, using reusable storage. Sharing across requests is a later optimization, permitted only after those dependencies are explicit and tested.

| Premise source | Extraction requirement |
| --- | --- |
| Declaration contracts | Include only established, visible contract relations. Equality supplies both directions. Field/local obligations do not become assumptions that prove themselves. |
| Input complete Types | Extract substituted Type clauses and permitted stored-Origin/outside-borrow relations. Respect binder identity and the scope of Function Type inputs. |
| Result complete Types | Include permitted definition-side well-formedness. Exclude the identified result premise owner when requested, and stop at nested Function Type quantification boundaries. |
| Associated formations and requirements | Preserve the current declaration exclusions, inherited formation context and admitted conditions. Account for the completed G84 associated-inference changes at the implementation baseline. |
| Borrowed local Places | Extract stored-dependency relations only where borrowing that complete Place permits them at this use. Do not treat the local annotation under validation as an established relation. |

Extract premises once for an environment, resolving their endpoints under the same effective use context as the query. Do not assume that resolving at a declaration node is equivalent to `OriginAtUse(..., use)`; prove equivalence before sharing such normalized endpoints.

Include eligible local-Place relations for intermediate nodes reached through other premises, not just for the original shorter endpoint. Memoize discovery under the appropriate context so shared stored-Type or local-dependency graphs are not unfolded once per path.

Premise extraction must not recursively invoke the same proof engine through `AdmittedSemantics` or capability checking. Complete required semantic inputs before freezing the environment, or model a finite dependency explicitly. A re-entry guard alone must not turn unfinished construction into a final negative answer.

### 3.3. Finite closure over composite Origin nodes

The preferred implementation candidate is a worklist computing which expressions outlive a selected target. Validate it against the specification before integrating it. The semantic interface is the pure proof boundary; the algorithm can be revised without moving inference effects back inside it.

Let `L >= S` denote `L outlives S`. The finite universe contains query expressions, extracted premise endpoints, their normalized substitution results and all their subexpressions. Meet nodes retain their operands. Do not synthesize arbitrary combinations of meets.

For a stable environment and target `S`, compute `Outlivers(S)`:

1. Seed `S` by reflexivity, plus the applicable intrinsic top-like facts. Preserve the existing distinctions between `static`, raw-borrow `Anchor` and their separate Loan dependencies.
2. For an extracted edge `A >= B`, reaching `B` adds `A`.
3. For a meet `M = A and B`, reaching `M` adds `A` and `B`, because each operand outlives its meet.
4. When every operand of an existing meet node has been reached, add that meet. Maintain an unmet-operand counter and reverse operand adjacency instead of rescanning all meets.
5. Process each newly reached node once, propagating until no new node is added. Then `L >= S` is entailed exactly when `L` has been reached.

This retains a composite premise such as `x >= (a and b)` without deriving `x >= a` or `x >= b`. For example, that premise alone proves the composite query while both atomic queries can remain unproven. This is a required test from SPEC §15.3.6.

Cycles with no supporting fact add nothing. A negative result is final only after convergence for this environment; a node on an active search path is not a negative cache entry. Premise order must not change the result.

Move the existing contextual local-projection and premise recursion into the finite calculation. Structural fast paths may remain if they are pure and have an explicit work bound; they must not retain a separate unbounded search before the solver runs. An implementation that only replaces the final premise loop has not established the full G74 bound.

### 3.4. Work and storage bounds

Define and measure:

- `V`: distinct normalized Origin expressions in an environment.
- `E`: extracted directed premise edges, including eligible local-Place relations.
- `M`: total operand incidences across its meet nodes.
- `P`: visited Type/context states and substitution/extraction work required to construct the environment.
- `Q`: actual top-level proof requests; `D`: distinct target closures evaluated in that environment.

For an already indexed environment, the closure target is `O(V + E + M)` time and scratch space: each node is scheduled once and each edge/operand incidence is processed a bounded number of times. Include meet adjacency in the cost; counting meet nodes alone is insufficient.

End-to-end accounting includes `P`, endpoint normalization, caller requests, diagnostic traversal and invalidation. With environment reuse, closure work is bounded by `O(D * (V + E + M))` in addition to extraction and caller work. Without reuse, account for the environment-construction cost of every request. Neither a single fast closure nor a doubling-ratio measurement proves an overall compiler bound.

Specify a bound for each extraction state and each permitted dependency expansion before integration. Deduplicate by identity and context rather than repeatedly walking shared graphs. Instrument retained graph capacity separately from active size.

Do not always materialize all-pairs reachability. One scratch closure has linear storage in the extracted graph; retaining all target sets can require quadratic storage in `V`. Query-local reuse is the default. Add retained target sets only for a measured benefit with a tested eviction or environment-lifetime policy.

### 3.5. Reuse and invalidation

The proof engine may share entailed facts within one stable environment. Final judgments and diagnostic records remain outside its cache: the `Proven` judgment used to constrain a finite/inferred region is not an established theorem between fixed Origins.

If repeated ownership, diagnostics or emission queries justify broader reuse, prefer environments after Binding completion. A declaration's `State == 3` alone is insufficient to establish that all effective substitutions and scope-dependent conditions are stable.

Invalidate or replace the environment after relevant premise, substitution, binder, header, Semantics or result-exclusion changes. `ResetPass`, reparsing and cross-Compilation use must not retain stale syntax or facts. Test source removal as well as addition. Reusable arrays and tables must release old object references while retaining only the capacity justified by the allocation policy.

## 4. Reproducer, regression and measurement design

Create a shared `tests/Workloads/OriginProofWorkloads.cs` for compiler regressions and opt-in Benchmark measurements. This and the new files named below are proposed paths, not existing interfaces.

| Family | Required shapes |
| --- | --- |
| A: successful program | Increasing borrowed Holder inputs; no explicit failed relation, including returned references. |
| B: failed composite relation | The original G74 shape and the intermediate-local `t_g1` shape, with/without `keep`, plus scalable Holder inputs. |
| C: failed atomic relation | The same environment with a simple failed relation and no result meet. |
| D: proven relation | Add sufficient explicit clauses; vary clause order and include direct and transitive proofs. |
| E: result premises | Definition-side premises, caller obligations, excluded result-owner checks and Function Item/contract comparison paths. |
| F: anonymous and generic context | Anonymous inputs, Function Types, pair Semantics conditions and associated requirements. |
| G: graph structure | Chains, diamonds, cycles, independent binders, both-sided meets and shared local-Place dependency graphs. |
| H: edits and reuse | Repeated analysis; add/remove/restore a relation; change only a result Type, binder or admitted condition; alternate successful and unsuccessful sources. |

Vary Origin count, edge density, meet operand count, Type nesting and scope depth independently where possible. Keep valid cases and their invalid counterparts. Failure is an expected workload result, not a failed measurement setup.

Derive semantic expectations from SPEC. Record accepted/rejected status, required diagnostic code, location, relation and failed operand coverage. Baseline diagnostics are useful comparison evidence but do not override a demonstrated specification defect.

Use small sizes such as 3–5 inputs for old/new differential cases. Expand the new solver to 8, 16 and 32 inputs and larger structural cases justified by the graph bounds. Time-limit old pathological cases in isolated benchmark processes, record timeout as censored evidence, and do not repeat a minute-long old case as routine warm-up. Timeouts are measurement controls, never language acceptance rules.

Counters should include proof entry calls, distinct normalized queries, environment builds, Type/context visits, premise edges, meet incidences, worklist insertions, edge activations, old search depth, cache reuse and active/retained capacity. Count complete operations even if a fast path succeeds.

Measure parsing/setup, Binding, ownership, diagnostic construction and end-to-end checking separately where instrumentation can distinguish them. Keep opt-in instrumentation from changing unmeasured hot-path allocation behavior. Pin source/configuration, runtime settings, warm-up and iteration counts before the final A/B comparison; document them in the new `src/Benchmark/OriginProof.md`.

Wall-clock benchmarks belong in `src/Benchmark`. Functional and allocation/work-bound regressions belong in xUnit. Preserve failed runs and raw data under `artifacts/verify/` and `artifacts/benchmarks/` respectively. Use repeated matched runs on an isolated baseline and candidate, not a shared build containing unrelated edits, to claim a speedup.

## 5. Implementation sequence

The dependency order is U0 → U1 → U2 → U3 → U4 → U5. Each unit includes its reproducer, change and focused verification. Compiler units require Unit verification; each implementation session ends with one Session verification under [VERIFICATION](../../docs/dev/VERIFICATION.md). Stage only the unit's files, commit verified work and push without force.

### U0 — Freeze evidence and measurement contracts

**Change:** Add opt-in `Binding.OriginProofMetrics.cs`, the shared workloads and `OriginProofScalingTest`. Add representative small A–H cases and record a diagnostic baseline. Establish current behavior on a clean, identified revision. Define work counters and normal-case performance comparison conditions before changing the algorithm. If existing Benchmark entries cannot execute these workloads, introduce the minimal `--origin-proof` driver here and retain the same workload/measurement contract for the later A/B comparison.

**Verification:** Related `OriginProofScalingTest`, `ResultPremiseTest` and `OriginRelationDiagnosticTest`; review every expected rejection against SPEC. Obtain whole-solution Release build evidence before running performance measurements. If Benchmark code is added in this unit, satisfy its Session and execution requirements before committing it.

**Exit:** Complete reproducer sources, source/configuration identity, raw counter/timing evidence, observed phase attribution and reproducible benchmark commands. Update PLAN's G74 description with confirmed current observations only. Do not close G74.

### U1 — Give inference effects explicit owners

**Change:** Audit proof callers and move required initializer fitting effects into constraint collection. Keep the old search behind a pure interface. Cover local annotations, initialization branches, invariant/equality directions, nested anonymous headers and subsequent uses of the same local.

**Verification:** Assert that proof does not change inference state on either result. Compare permutations of equivalent premises and fit branches. Check inferred Types, obligations, actual Loans and diagnostics. Add focused cases where old speculative proof branches used to encounter an open inference variable.

**Exit:** Every required bound has a semantic owner. No proof call mutates substitutions or obligations. Every behavioral difference is resolved against SPEC with a regression; unresolved cases block the dependent solver replacement, not unrelated work.

### U2 — Implement finite premise extraction and the pure solver

**Change:** Introduce `Binding.OriginPremises.cs` and `OriginPremiseClosure.cs`, using the stable environment contract and reusable storage. Cover every source in §3.2, with binder and context distinctions. Add `OriginPremiseClosureTest` and extraction tests before routing all production queries through it.

**Verification:** Use a small, independent SPEC-rule oracle: a relation table saturated by reflexivity, explicit edges/equality, permitted intrinsic facts, transitivity and meet rules. It must not call production extraction or closure helpers. Compare exhaustive small graphs and deterministic generated graphs, including composite-only proofs, cycles and renamed distinct binders. Separately verify extraction from real source programs and the rule that obligations cannot prove themselves.

**Exit:** Soundness and completeness for the selected finite rule model are explained, all oracle comparisons pass, extraction boundaries are covered, and state/work/storage bounds are written and asserted. Justify that restricting intermediate expressions to the selected universe preserves the consequences required by SPEC; oracle agreement within that universe alone does not establish this. The old DFS is supplementary comparison evidence, not the sole correctness oracle. U2 and U3 may share one verified commit to avoid leaving an unused production solver.

### U3 — Integrate the solver across proof consumers

**Change:** Route the pure proof interface through the new environment/closure. Include contextual local-Place and meet processing so the old recursive search is not left in a wrapper. Preserve `JudgeOriginRelation`, region inference and Loan analysis as separate consumers. Reuse proof facts during failed-operand selection without caching diagnostic records.

**Verification:** Compare old and new pure engines on bounded cases in independent state or immutable snapshots; run only the new engine for large scaling cases. Compare compiler acceptance, required diagnostics and actual-Loan regressions. Cover repeated ownership and emission checks, result-premise exclusion, capability queries and rejected fit branches. Check A–G through sizes 8/16/32 and assert the bounds from §3.4 rather than an unexplained timing ratio.

**Exit:** No remaining proof path can reintroduce path enumeration or inference side effects. All differences have a SPEC-based disposition. Large valid and invalid workloads complete with bounded measured work; the proof/region judgment distinction and diagnostic source attribution remain intact.

### U4 — Validate lifecycle and optimize measured repetition

**Change:** Exercise environment reuse, scratch storage and invalidation. Extend reuse across completed Binding consumers only if measurements justify it. Keep query-local environments for unstable contexts. Add lifecycle/allocation coverage, and complete the `--origin-proof` Benchmark entry and its measurement document.

**Verification:** H-family edits, same-Compilation and cross-Compilation isolation, result-owner exclusion changes, substituted endpoints, header completion, removal of source references and many equivalent edits. Assert warm zero allocation where already guaranteed, bounded retained capacity and valid results for every measured operation. Compare small normal library workloads as well as pathological workloads under fixed conditions.

**Exit:** No stale proof survives an environment change; no stale syntax reference or per-query retained growth accumulates. Cache scope, invalidation dependencies, measured benefit and retained-space cost are documented. If broad reuse adds no material benefit, complete the unit with the narrower tested lifetime.

### U5 — Final verification, evidence and documentation

**Change:** Remove temporary production comparison switches and obsolete search machinery once their evidence is retained. Keep useful deterministic semantic/work-bound tests. Prepare CODEMAP changes for new responsibilities and entry points, concise PLAN/HISTORY updates and any verified STATUS boundary changes before final verification.

**Verification:** Final Session on unchanged source/configuration, the selected native O0/O2 fixtures, Programs 20/26/27/37, diagnostic snapshot comparison and the relevant performance measurements. A successful final Session may also satisfy the last Unit when it covers all required checks; do not run a duplicate merely for the mode name. Associate Verify evidence with the commit using `scripts/verify-commit.ps1`, then push.

**Exit:** All conditions in §7 pass. Record limitations and residual independent issues explicitly. G74 may be closed only with whole-path evidence; Program 26 passing alone is insufficient.

## 6. Verification selection and interpretation

Start with the following existing test groups and refine selection from the files actually changed.

| Concern | Existing test groups |
| --- | --- |
| Relations, obligations and diagnostic chains | `OriginRelationDiagnosticTest`, `CallOriginRelationTest`, `LocalOriginClauseTest`, `BorrowOriginSuffixTest`, `ResultPremiseTest`, `NestedValueCallOriginTest` |
| Identity, normalization and elision | `OriginRedesignTest`, `StaticOriginElisionTest`, `TypeBindingTest` |
| Regions, captures, Loan retention and edits | `LocalRegionInferenceTest`, `FiniteRegionCompositionTest`, `CallableRegionInvalidationTest`, `OwnershipJoinTest`, `ContextualCaptureTest`, `CallableOriginForwardingTest`, `FixedOriginRetentionTest` |
| Associated and generic premises | `AssociatedOriginBindingTest`, `AssociatedFormationTest`, `PairVarianceTest`, `InheritedConformanceTest`, relevant completed G84 inference regressions |

Use functional and allocation checks together for related hot paths. Native fixtures must be explicitly selected from those regenerated by the selected tests, including Origin inference, result contracts, local regions and actual-Loan retention. Confirm the concrete patterns during U0; do not claim coverage from a pattern that has not been checked. Milestone harnesses build the original programs at O0/O2.

Example command shapes, to be used after the new classes and selected fixtures exist:

```powershell
./scripts/verify.ps1 -Class OriginProofScalingTest,ResultPremiseTest,OriginRelationDiagnosticTest -DiagnosticSnapshot
./scripts/verify.ps1 -Class OriginPremiseClosureTest,OriginProofScalingTest
./scripts/verify.ps1 -Mode Session -Fixtures <selected-patterns> -Milestone 20,26,27,37 -DiagnosticSnapshot -DiagnosticBaseline <baseline-json>
./scripts/verify-commit.ps1 -Evidence artifacts/verify/<successful-run> -Commit HEAD
```

The final command shape requires replacing placeholders with checked selections and actual evidence paths. Session always includes all functional and allocation regressions; it does not implicitly select native fixtures or milestones. Toolchain identity verification is separate and must be reported as performed or not performed. NativeAOT is outside this plan.

No unexplained diagnostic snapshot changes are allowed. A demonstrated old defect may justify an intentional corrected result: minimize it, state the governing SPEC rule, add a focused regression and record the reviewed difference. Do not silently regenerate the baseline or treat all old behavior as normative.

Ordinary diagnostic development applies. This task does not activate the detailed diagnostic workflow or an unrelated CLI/LSP presentation audit. Verify affected observable records and retention behavior without expanding the unit into diagnostic enrichment.

## 7. Completion conditions

G74 is complete only when all of the following are evidenced:

- Pure proof is independent of inference mutation, premise order and prior query order.
- All permitted premise sources and quantification/exclusion boundaries are represented; local/Field obligations cannot establish themselves.
- Composite relations retain their meaning; fixed-Origin proof and finite/inferred-region judgments remain distinct.
- Source expectations, the independent rule oracle and the affected regression suite agree, with every intentional behavior correction documented.
- Valid and invalid scalable workloads have bounds on environment construction, closure work, caller repetition and retained storage. No old recursive path escapes that accounting.
- Repeated fixed-condition measurements establish a practical improvement on pathological workloads and an acceptable normal-case cost. Choose numeric targets from U0 evidence before final candidate tuning and retain that decision; do not relax them after a failing comparison.
- Existing allocation guarantees pass with their original fixed warm-up conditions. Capacity and object-reference retention remain bounded across edits and failures.
- Required Unit/Session, native fixtures, milestones and snapshot checks pass on identified inputs, and evidence is associated with the committed implementation.

## 8. Open questions and change triggers

| Question or finding | Required disposition |
| --- | --- |
| Old speculative search adds an inference bound that the explicit fit collector lacks. | Minimize the case and decide from SPEC whether the fit collector is incomplete or the old side effect is incorrect. Resolve before integrating the new solver. |
| A premise endpoint changes when resolved at a different use. | Keep use-specific normalization and expand the environment identity; do not share the affected result. |
| Pair Semantics admission or associated formation re-enters proof. | Establish an explicit phase/dependency boundary before sealing; no negative cache entry from a re-entry guard. |
| Input well-formedness traversal crosses a nested Function Type binder. | Check §15.3.4 with a real source case; preserve only premises in the legal quantification scope. Do not copy an old traversal mechanically. |
| Local-Place or Type graph extraction revisits shared dependencies. | Deduplicate discovery with the necessary context, count its states and include the cost in the full bound. |
| The closure and independent SPEC oracle disagree. | Preserve the smallest graph and correct the rule encoding or algorithm. No performance claim supersedes a semantic discrepancy. |
| Current profiling attributes the dominant cost elsewhere. | Keep responsibility separation where justified, but revise the performance unit around the measured owner before promising a speedup. |
| Cache sealing cannot be shown safe. | Use query-local storage or completed-Binding-only reuse; caching is not a condition for solving the search problem. |
| Normal cases regress or retained memory grows. | Revisit extraction granularity, lazy construction and cache lifetime. Preserve failed evidence and existing assertions. |

No formal specification change is currently required. If a concrete contradiction or a deliberately better rule is established, document its rationale against the Kimigayo Principles and make a separate, coherent spec/implementation unit. Update the affected chapters, examples, tests and milestone programs together, record any proposal intake in [INTEGRATED](../INTEGRATED.md), and keep the formal specification independent of this draft. Merely matching the old engine does not justify a new normative sentence.

## 9. Documentation and proposal lifecycle

Creating this file changes no implemented support claim. Keep the current SPEC, STATUS, PLAN and integration register unchanged for this planning task.

During implementation, update CODEMAP when entry points or responsibilities change, PLAN with confirmed G74 scope and progress, PLAN_HISTORY with concise session evidence, and STATUS only when a verified support boundary changes. Record benchmark procedures in `src/Benchmark/OriginProof.md` and detailed evidence in the ignored artifact directories. No public Kimi declaration change is planned, so LIBRARY and library style updates are not expected.

This proposal remains in `draft/Proposals` while its design and implementation disposition are open. Any later integration or closure follows the existing register rules; completion of this planning document is not integration, freezing or implementation completion.
