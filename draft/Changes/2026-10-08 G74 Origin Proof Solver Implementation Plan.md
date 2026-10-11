# G74 Origin Proof Solver Implementation Plan

Date: 2026-10-08. Status: draft implementation plan; implementation and performance verification are pending.

Source inspection baseline: `35bbc6ba4a501ec7942e6ac3452313d4c4562f33`. `src/` is unchanged between that baseline and the revisions of this document, so cited line numbers refer to the baseline. Recheck the relevant code and concurrent changes before implementation.

The plan combines the responsibility separation proposed in the supplied `G74a.md` with the workload investigation and closure algorithm proposed in `G74o.md`. A comparative review of both inputs against the baseline added the remaining concrete findings. A later revision reorganized the document and added mechanisms that enforce the Kimigayo Principles structurally (§1.4). The inputs are design material, not specification authority or independently verified measurement evidence. This document is self-contained; implementation must use the current formal specification and repository workflow.

Creating or revising this proposal does not start implementation, close G74, resume paused G55/G60 work, change the milestone order or integrate a specification change. Documentation-only verification consists of reviewing this file, its references and its diff; no build or test is required.

## 1. Objective, scope and principles

### 1.1. Objective

Replace path-dependent recursive Origin proof search with a finite calculation over explicit premises. Separate inference constraint collection from proof before changing the search algorithm. Make correctness, work bounds, allocation behavior and invalidation independently verifiable.

### 1.2. Required outcomes

The completed change must:

1. Preserve the Origin and Loan rules in [SPEC Chapter 15](../../docs/spec/15-ownership-and-lifetime-analysis.md), especially §§15.3.4–15.3.7, §15.4.4 and §§15.6.1–15.6.5.
2. Never expand the same proof state separately for different search paths within one stable proof environment.
3. Handle successful and unsuccessful checks, including successful programs that cause unsuccessful intermediate queries.
4. Preserve actual Loan identity, authority and raw-borrow anchors independently of Origin equivalence.
5. Preserve existing warm allocation guarantees and bound retained storage across edits and repeated analysis.
6. Associate acceptance, diagnostics, work counts and measurements with exact sources, configuration and compiler identity.

### 1.3. Constraints and non-goals

- No language restriction, Origin syntax change, new public library declaration or inference resource-limit rejection is proposed. A specification-valid form must not be prohibited to make the implementation faster. [SETTLED](../../docs/SETTLED.md) has been consulted: result-only universal Origins remain supported, and no entry concerns the proof algorithm or its universe.
- No normative specification change is required. §6 describes one conditional clarification and the conditions under which it may be made.
- Ordinary diagnostic development applies. This plan does not activate the detailed diagnostic workflow or a CLI/LSP presentation audit, and it does not change diagnostic content except to correct a demonstrated defect (§4.5). SPEC §15.6.1 makes Origin Advice conditional prose, never a repair candidate; nothing here changes that.
- NativeAOT is outside this plan.

### 1.4. Principle alignment

Each principle is supported by a mechanism that can be checked, not only by review.

| Principle | Mechanisms in this plan |
| --- | --- |
| One Concept, One Canonical Form | One pure entailment engine for every proof consumer (§3.1). One premise-rule catalog that also covers the structural fast path and meet absorption (§3.3). One verdict per obligation, shared by ownership, emission and diagnostics (§3.8). |
| Local Reasoning | Environments built only from contracts and Types visible at the use (§3.4). A work bound stated in source-visible terms (§3.7). Metamorphic properties such as monotonicity and order independence tested directly (§4.3). A conditional specification clarification that publishes the proof universe (§6). |
| Explicit Semantics | Fitting, proof and judgment as separate responsibilities (§3.1). Purity of proof checked mechanically (§3.2). "Not ready" distinguished from "not entailed" by the environment's type (§3.4). Each obligation records which check discharges it (§3.8). |
| Compiler Server Protocol | Premise provenance and canonical derivation witnesses (§3.6). Counters and evidence tied to source, configuration and compiler identity (§4.4). A whole-suite comparison mode that reports censored comparisons as remaining uncertainty (§4.2). |

## 2. Current behavior and evidence

### 2.1. Responsibilities

The [Origin relations entry in CODEMAP](../../docs/dev/CODEMAP.md) identifies the following implementation boundaries.

| Component | Current responsibility and relevant issue |
| --- | --- |
| [Binding.OriginProof](../../src/Kimi/Compiler/Binding/Binding.OriginProof.cs) | `ProvesOriginOutlives` resolves Origins, collects initializer inference bounds and recursively searches premises. |
| [Binding.Origins](../../src/Kimi/Compiler/Binding/Binding.Origins.cs) | The context-independent `OriginOutlives` fast path and meet construction, which absorbs operands through the same fast path. |
| [Binding.TypeRelations](../../src/Kimi/Compiler/Binding/Binding.TypeRelations.cs), [Binding.OriginDeclarations](../../src/Kimi/Compiler/Binding/Binding.OriginDeclarations.cs), [Binding.OriginInference](../../src/Kimi/Compiler/Binding/Binding.OriginInference.cs) | Fit complete Types, resolve omitted Origins and collect or solve constraints. These are the owners to audit when moving inference effects out of proof. |
| [Binding.ResultPremises](../../src/Kimi/Compiler/Binding/Binding.ResultPremises.cs) | Definition-side result well-formedness and caller-side obligations. `VisitResultPremise` temporarily excludes a particular function's result premises. |
| [Binding.AssociatedOrigins](../../src/Kimi/Compiler/Binding/Binding.AssociatedOrigins.cs), [Binding.AssociatedFormation](../../src/Kimi/Compiler/Binding/Binding.AssociatedFormation.cs) | Associated formation and requirement premises, with declaration and scope boundaries. |
| [Binding.Diagnostics](../../src/Kimi/Compiler/Binding/Binding.Diagnostics.cs) | Repeats proof and judgment while finding failed operands and diagnostic chains. Diagnostic records themselves are use-specific. |
| [Binding.LocalRegions](../../src/Kimi/Compiler/Binding/Binding.LocalRegions.cs), [OwnershipAnalysis.Borrows](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Borrows.cs) | Combine relation judgments with inferred/finite regions and actual Loans. |
| [LlvmEmitter](../../src/Kimi/Compiler/Emission/LlvmEmitter.cs) | Rechecks Origin obligations before generation. |

### 2.2. Structural causes

- **Path-only cycle check.** `originProofPath` adds `(longer, shorter)` on entry and removes it on return. It prevents infinite recursion along one path but does not reuse completed subqueries, so an unprovable subgoal is re-explored from every path that reaches it.
- **Per-query premise rediscovery.** Each query walks ancestor syntax and nested Types again. The premise loop (lines 301–312) tries every premise `A >= B` through two recursive subgoals, `longer >= A` and `B >= shorter`, each of which repeats the whole walk.
- **Mixed responsibilities.** A proof can update `Replacements` and add an obligation through `OpenInitializerInference` (lines 189–197). The function is not a pure question, so its results cannot be memoized safely, and a negative result found on an active path differs from non-entailment by a stable environment.

The exact asymptotic class of the current compiler has not been established by the supplied measurements.

### 2.3. Order-dependent inference effects

The inference effects of §2.2 already depend on proof control flow in three places of `Binding.OriginProof`:

- The longer-meet loop (line 240) deliberately evaluates every operand with `all &=`. Short-circuiting it as an optimization would also skip bound collection for the remaining operands.
- The shorter-meet loop (line 253) returns at the first operand that succeeds. Consider `x outlives (?i and b)` where `x outlives b` is provable. Whether the open variable `?i` receives the bound `x` depends on the canonical operand order of `CompareOrigins` (Kind, then source position), not on any fit.
- In the premise loop (lines 303 and 308), the first conjunct can record a bound before the second conjunct fails. The bound remains although that premise path was rejected.

Keeping the first part of `ProvesOriginOutlives` textually unchanged while replacing its later recursive calls does not preserve these effects. The new design assigns required constraints to actual fits (§3.1). The three cases are U1 reproducer candidates whose required behavior is decided from SPEC §15.4.4 and §15.3.6, not from the current evaluation order.

### 2.4. Repeated and implicit caller work

- In `OwnershipAnalysis.Borrows`, `UncheckedOriginObligation` evaluates `IsVerifiedOriginObligation` and `JudgeOriginObligation` (lines 113–115) before its structural exclusion of input well-formedness obligations (lines 123–128). `VerifyBorrows` checks the nested dependencies of those obligations at call sites. An excluded obligation yields `false` on either path, so the order affects only cost.
- That exclusion recognizes the responsible check from the obligation's shape, after the fact. Which check discharges an obligation is not recorded when the obligation is created; the related flag `BindingObligation.WellFormed` is set only for result-premise obligations.
- `ReportUnprovenOriginObligation` judges each reported obligation again and once per meet operand. `SupportsOriginObligations` repeats the whole scan before emission (`LlvmEmitter` line 532).
- Diagnostic chain construction calls `FailingOperand` once with `refuted == true` and once with `false` (`Binding.Diagnostics` line 1609). `OriginPartFails` and `AddFailingChains` repeat proof and judgment per failing operand.
- Result-premise detection (`ResultPremiseAction.Detect`) proves relations with the function's own result premise excluded. Its queries fail exactly when that premise is needed, and each failure pays the full search cost.

### 2.5. Historical and reported measurements

| Evidence source | Report | Treatment in this plan |
| --- | --- | --- |
| [PLAN G74](../../docs/dev/PLAN.md) | Historical 254 s / 292 s checks; removing `keep` reduced one case to 2.6 s. | Historical observations, not current baseline values. Reconstruct the abbreviated example as a complete source. |
| `G74o.md` | Original `t_g1` completed in 0.97 s including process startup; 7 Holder inputs took 9.7 s and 8 exceeded a 60 s limit. | Preliminary observations from a build containing uncommitted changes, one run per case. Reproduce before updating any support or performance claim. |
| `G74o.md` | A valid 7-input program took 5.7 s. Adding the clauses that make the relation provable increased the time to more than 60 s. | Include valid and clause-provable programs at the same sizes as failing ones; more premises can make the old search slower. |
| `G74o.md` | Traces attributed 85% (valid) and 94.5% (clause-provable) to `UncheckedOriginObligation → IsVerifiedOriginObligation → ProvesOriginOutlives → ProvesTypeOriginPremise → ProvesStoredOriginPremise`, with up to 24 nested proof frames; Binding itself took 2–5%. | Reconfirm phase attribution in U0. Confirm with counters whether the dominant obligations are the input well-formedness obligations that ownership later excludes (U0b). |
| Commit `0e6fd329` | Added direct-premise priority and skipped Origin-free targets as part of fixed-Origin Loan work. | Confirmed historical optimization; its commit message does not establish a general bound on proof search. |

Neither the reported speed of the old reproducer nor Program 26's historical warm Binding time closes G74. Retain the original case as a regression and add scalable families that isolate the remaining search behavior.

## 3. Design

### 3.1. Fitting, proof and judgment

Use three explicit responsibilities; final API names are implementation choices.

| Responsibility | Inputs and allowed effects | Result |
| --- | --- | --- |
| Collect fit constraints | Source and destination complete Types, variance, the actual fit/use and its inference owner. May add required bounds and obligations. | Constraints attributed to the fit that requires them. |
| Prove a relation | Resolved Origin expressions and a sealed environment (§3.4). May modify private scratch storage and counters only. | Entailed or not entailed by that environment. Not entailed is not a contradiction. |
| Judge an obligation | Proof result, fixed/finite/inferred classification, region bounds and the existing ownership rules. | The existing `Proven`, `Refuted`, `Unknown` or `Unrepresentable` judgment, recorded once per obligation (§3.8). |

Audit every `ProvesOriginOutlives` caller, including capability checks, invariant/reversed fits, calls, result-premise detection, local annotations and anonymous function headers. Record which operations legitimately collect constraints and which only ask for proof. The audit also establishes two semantic boundaries:

- **Selection independence:** an Origin proof outcome must not change overload selection, candidate applicability or retries. Origin requirements are judged after selection and never make a candidate inapplicable (SPEC §15.3.6, §15.6.1). Identify every caller that runs while candidates are still open, including capability checks, and show that its proof result cannot influence the choice.
- **Invariant positions:** proof and constraint collection never weaken an invariant inner Origin to a meet. This covers `uniq`, `obj`, `objuniq` and `raw` targets, and pair layers whose admitted Semantics include an invariant one (SPEC §15.3.5; §15.3.6, Invariant row). An `==` obligation stays two directions.

Resolve already published substitutions before proof. Where active inference remains, route required fitting constraints to their owner and seal a new environment after relevant state changes. Do not freeze an unfinished inference variable as a universal Origin or turn its desired bound into an assumed premise.

Initially retain the old recursive algorithm behind the pure proof boundary to isolate the effect of responsibility separation. Any difference caused by removing speculative inference effects needs a minimized reproducer and a SPEC-based disposition. Preserve required fit constraints; do not preserve an accidental effect merely to match the old search. Conversely, do not delete an effect until its intended semantic owner is identified.

### 3.2. Mechanical purity check

A pure proof request leaves substitutions, obligations, declaration states and inference state unchanged on success and failure, including repeated calls and calls in a different order. Enforce this by construction rather than review:

- Route every mutation of inference and obligation state through helpers that increment one Binding-owned version counter. This covers `Replacements`, the obligation list, initializer and Origin declaration states, and `resultPremiseExcluded` outside its scoped restore. U1 audits that no other mutation path exists.
- The pure proof entry reads the counter on entry and compares it on exit. A difference is an `InternalInvariant_Kd` failure that names the request. Counters, scratch storage and interning tables of Origin atoms and expressions are exempt; an interning table may grow, but never changes the meaning of an existing expression.
- The check is one integer comparison per request, so it stays enabled in Release and in Verify's default configuration. Measure it in U0 and U5 against the allocation and timing baselines.

The same check confirms that ownership-time proof records no bounds (U0b) and that no later change reintroduces an effect into proof.

### 3.3. Premise-rule catalog

One catalog lists every rule the proof engine may apply. Production extraction, the structural fast path `OriginOutlives`, meet absorption in `Meet`, counters and provenance kinds (§3.6) are organized by it, and no proof rule exists outside it. The independent oracle (§4.2) is written from the SPEC rules, not from the catalog code.

| ID | Rule or premise source | SPEC basis | Edges or effect | Context dependence |
| --- | --- | --- | --- | --- |
| I1 | Reflexivity | §15.3.6 | Seeds the target. | None. |
| I2 | Maximality of `static` | §15.2.3, §15.6.5 | `static` outlives every target. | None. |
| I3 | Raw-borrow `Anchor` | §5.2.2, §15.2.3 | Outlives every target but is no lifetime bound; a meet keeps it, so separate Loan dependencies survive. | None. |
| I4 | Meet laws | §15.3.6 | Operands outlive their meet; a meet outlives a target when all operands do. | None. |
| I5 | Equality | §15.3.3, §15.3.6 | `a == b` supplies both directions; published substitutions resolve endpoints first. | Substitution state. |
| R1 | Declaration contracts | §15.3.3 | Relations of ancestor functions, accessors, containers and associated requirements with established declarations (`State == 3`). Local and Field relations are never premises. | Ancestor chain. |
| R2 | Input well-formedness | §15.3.4, §15.3.7, §8.1.1 | Substituted Type clauses, stored Origins outliving an outer borrow, pair layers under admitted Semantics. | Binder scope, Function Type inputs, admitted Semantics, anonymous-header completion. |
| R3 | Result well-formedness | §15.3.7 | Definition-side premises of a named function's result. | The identified excluded result-premise owner; stops at nested Function Types. |
| R4 | Associated formation and requirements | §8.4.3.1 | Formation-Type and requirement relations. | Excludes the requirement's own declaration; inherited formation context. |
| R5 | Borrowed complete local Place | Identify the governing SPEC rule in U2. | Stored Origins of a local outlive a borrow of that complete Place. | Use position (the use is outside the local's declaration). |

R5 is the one source without a cited SPEC rule at the baseline; its code comment states that ownership verifies the availability at each use. U2 identifies the governing rule. If no rule is found, record that as an open specification item rather than keeping the premise silently (§8).

The structural fast path and meet absorption are instances of I1–I4. The oracle checks them like every other rule, so the compiler never contains a second rule set.

### 3.4. Sealed proof environments and premise extraction

A proof environment identifies at least:

- Source/Binding generation and the relevant binder identities.
- The use and its lexical/constraint scope, including local Place eligibility.
- Visible completed R1–R5 premises.
- The substitution state used to resolve every query and premise endpoint.
- The identity of the excluded result-premise owner, not only whether an exclusion is active.
- Anonymous-header completion and the Semantics conditions used to admit pair-layer premises.

**Construction outcome.** Environment construction returns either a sealed environment or *not ready* with a reason. Proof accepts only a sealed environment, so "not ready", "not entailed" and "refuted" cannot be confused. A context that is still under construction, including one reached again through `AdmittedSemantics` or capability checking, yields *not ready*. A caller receiving *not ready* treats the relation as not yet decided: it keeps or records the obligation under its existing deadline (`BindingDeadline`) and draws no negative conclusion. At the final deadline, *not ready* is an `InternalInvariant_Kd` failure, never `Unknown` or `Refuted`. A re-entry guard therefore cannot turn unfinished construction into a negative answer. Complete required semantic inputs before sealing, or model a finite dependency explicitly.

**Granularity.** Start with one environment per top-level proof request, using reusable storage. Decide from U0 counters whether a function-level environment after Binding completion is needed earlier (§3.7). Sharing across requests is permitted only after its dependencies are explicit and tested (§3.9).

**Extraction requirements.**

- Extract premises once per environment, resolving their endpoints under the same effective use context as the query. Do not assume that resolving at a declaration node is equivalent to `OriginAtUse(..., use)`; prove equivalence before sharing such normalized endpoints.
- Apply R5 to every node the closure reaches, not only to the seed. The baseline applies the local-Place premise (line 228) at every recursive subgoal, so expanding it only from the seed would prove less than the old search.
- Memoize discovery under the appropriate context so shared stored-Type or local-dependency graphs are not unfolded once per path. Visit each Type schema at most once per context; recursive Types have finite schemas, and instantiation never discovers infinitely expanded slots (§15.3.7).
- Do not treat a local annotation under validation, or any obligation, as an established relation.

**Baseline details to check against SPEC.** These are candidates for the rule, not a specification to copy:

- A pair layer returns after recursing into its target and does not continue to the component loop (lines 369–380).
- Stored-Origin traversal of an input enters nested Function Types, whereas result premises stop at them (§8, §15.3.4 question).
- Borrow and slice components contribute stored relations only under the current `CarriesOrigin` and outer-Origin conditions (line 385).
- Associated-requirement and formation premises exclude the requirement's own declaration (`ReferenceEquals(associated.Declaration, node)`).
- Function Type input premises are assumptions only inside that signature.
- The completed G84 associated-inference changes at the implementation baseline are accounted for.

### 3.5. Finite closure

The semantic interface is the pure proof boundary; the algorithm below can be revised without moving inference effects back inside it.

Let `L >= S` denote `L outlives S`. The finite universe contains query expressions, extracted premise endpoints, their normalized substitution results and all their subexpressions. Meet nodes retain their operands. Do not synthesize arbitrary combinations of meets.

For a sealed environment and target `S`, compute `Outlivers(S)`:

1. Seed `S` (I1) and the top-like facts I2 and I3.
2. For an extracted edge `A >= B`, reaching `B` adds `A`.
3. For a meet `M = A and B`, reaching `M` adds `A` and `B`, because each operand outlives its meet.
4. When every operand of an existing meet node has been reached, add that meet. Maintain an unmet-operand counter and reverse operand adjacency instead of rescanning all meets.
5. Process each newly reached node once, propagating until no new node is added. `L >= S` is entailed exactly when `L` has been reached. A positive query may stop as soon as `L` is reached; a negative result is final only after convergence.

This retains a composite premise such as `x >= (a and b)` without deriving `x >= a` or `x >= b`: that premise alone proves the composite query while both atomic queries can remain unproven, as SPEC §15.3.6 requires. Cycles with no supporting fact add nothing. A node on an active search path is never a negative cache entry.

**Canonical processing order.** Enumerate seeds and edges in catalog order and, within one catalog row, in the deterministic traversal order that row defines: the ancestor chain from the use outward, clauses in source order, inputs in parameter order and Type components in component order. Process the worklist in insertion order. Never depend on hash-table iteration order. Entailment does not depend on order in any case; the canonical order additionally makes witnesses (§3.6) and every counter a function of the source and the environment alone, without a sorting step.

**Universe restriction.** The expected argument, to be completed in U2, covers I1–I5 and plain edges:

1. Map each universe expression `t` to the set `T(t)` of universe targets `S'` with `t ∈ Outlivers(S')`.
2. The rules make edges monotone (`A >= B` implies `T(B) ⊆ T(A)`) and make `T(a and b)` equal to `T(a) ∩ T(b)`. Top-like facts give the full set.
3. These sets, ordered by inclusion with meet as intersection, form a region model that satisfies every premise.
4. If `L` is not reached from `S`, then `S ∈ T(S)` but `S ∉ T(L)`, so the model refutes `L >= S`.

Restricting to the universe therefore loses no consequence of this fragment. The context-dependent rules (R2 pair-layer admission, R5 and any premise that is not a plain edge) each need their own argument or an explicit encoding as edges of the environment.

**Relation to the old search.** The old search is a goal search over ground Horn rules with an ancestor loop check, and such a search is complete for the least model. Its entailments should match the closure, except where the closure omits a context rule that the old search applied at intermediate subgoals, or where the old search produced inference effects. Each observed difference needs one of these explanations; any other difference indicates an encoding or algorithm error.

**No residual search.** Move the contextual local-projection and premise recursion into the closure. Structural fast paths may remain only as pure catalog instances with an explicit work bound. An implementation that only replaces the final premise loop has not established the G74 bound (§3.10).

### 3.6. Provenance and witnesses

- Each extracted edge records its catalog ID (§3.3) and the syntax that introduced it: the clause, the Type occurrence or the borrowed local.
- The closure records, for each reached node, the edge or rule that first reached it in canonical order. The witness of an entailment is the resulting path back to the seed.
- Storage is one index per reached node and one reference per edge in pooled arrays, released with the environment. U0 and U5 measurements cover it.

Uses within G74:

- Explaining comparison-mode mismatches and oracle failures.
- Reusing one closure for failed-operand selection instead of re-proving each operand.
- Counting work per catalog ID.

The interface also evaluates entailment under additional hypothetical edges in scratch storage, without mutating the environment. This plan does not use that capability or witnesses in diagnostic output. Exposing them through the Compiler Server Protocol, or using them to qualify Advice, needs a separate proposal under SPEC §23 and the diagnostic workflow; §15.6.1 keeps Origin Advice conditional prose, never a repair candidate.

### 3.7. Work and storage bounds

Define and measure:

- `V`: distinct normalized Origin expressions in an environment.
- `E`: extracted directed premise edges, including eligible local-Place relations.
- `M`: total operand incidences across its meet nodes.
- `P`: visited Type/context states and substitution/extraction work required to construct the environment.
- `Q`: actual top-level proof requests; `D`: distinct target closures evaluated in that environment.

For a sealed environment, one closure takes `O(V + E + M)` time and scratch space. Assert per closure, in tests, that worklist insertions do not exceed `V`, edge activations do not exceed `E` and operand decrements do not exceed `M`. Count meet adjacency in the cost; counting meet nodes alone is insufficient.

End-to-end accounting includes `P`, endpoint normalization, caller requests, diagnostic traversal and invalidation. With environment reuse, closure work is bounded by `O(D * (V + E + M))` in addition to extraction and caller work. Without reuse, account for the construction cost of every request. Neither a single fast closure nor a doubling-ratio measurement proves an overall compiler bound.

**Source-visible statement.** At one use, `V`, `E` and `M` are bounded by the size of the contracts and complete Types visible from the use's declaration chain plus the borrowed local Place chains reachable there. `P` is bounded by the visited Type/context states of those declarations. Proof work at a use is therefore predictable from local code. U2 verifies this statement and records it in the closure's documentation comment and in `src/Benchmark/OriginProof.md`.

Specify a bound for each extraction state and each permitted dependency expansion before integration. Deduplicate by identity and context rather than repeatedly walking shared graphs. Instrument retained graph capacity separately from active size.

Do not always materialize all-pairs reachability. One scratch closure has linear storage in the extracted graph; retaining all target sets can require quadratic storage in `V`. Query-local reuse is the default. Add retained target sets only for a measured benefit with a tested eviction or environment-lifetime policy.

**Environment granularity.** Decide it from U0 evidence, not after integration. Valid programs issue many ownership-side requests in the same function, so per-request construction costs about `Q * P` per function. If U0 counters show that this term dominates the valid workloads, implement the completed-Binding, function-level environment in U3 instead of U5. Record the decision and its counter values.

### 3.8. Obligation verdicts and discharge owners

**Discharge owner.** Each Origin obligation records, at creation, the check responsible for discharging it: Binding proof at its deadline, region inference for finite/inferred chains, or call-site borrow formation (`VerifyBorrows`) for input well-formedness. The value comes from the rule that created the obligation and replaces the structural recognition of §2.4. Relate the existing `WellFormed` flag to it: if the flag denotes the same responsibility, fold it into the value; otherwise keep it as an independent classification and document the difference. Ownership then skips obligations it does not own without any proof. U0b's reorder is the interim step until this lands.

**One verdict per obligation.** Once Binding and local-region inputs are final, compute one verdict per obligation: the judgment, the reversed direction where relevant and the failing operands of a meet. `UncheckedOriginObligation`, `ReportUnprovenOriginObligation`, `SupportsOriginObligations` and diagnostic chain construction read this verdict instead of judging again. The verdict is keyed by the obligation within one Binding generation and invalidated with that generation. A consumer that observes changed inputs is an `InternalInvariant_Kd` failure, not a reason to judge again silently.

The verdict table is not a proof cache. It never supplies a premise or a theorem for another use, so the rule of §3.9 still holds: a `Proven` judgment that constrains a finite or inferred region is not an established relation between fixed Origins.

### 3.9. Reuse and invalidation

The proof engine may share entailed facts within one sealed environment. Final judgments, verdicts and diagnostic records remain outside its cache.

If repeated ownership, diagnostics or emission queries justify broader reuse, prefer environments after Binding completion. A declaration's `State == 3` alone is insufficient to establish that all effective substitutions and scope-dependent conditions are stable.

Invalidate or replace the environment after relevant premise, substitution, binder, header, Semantics or result-exclusion changes. A cache key identifies the excluded result-premise owner itself. `ResetPass`, reparsing and cross-Compilation use must not retain stale syntax or facts. Test source removal as well as addition. Reusable arrays and tables must release old object references while retaining only the capacity justified by the allocation policy.

### 3.10. Rejected alternatives

| Alternative | Reason not adopted |
| --- | --- |
| More search-order heuristics or pruning, extending `0e6fd329` | They lower the branching factor but leave the path-dependent worst case. The Holder family was still reported to grow about 25–30 times per added input after that commit. |
| Tabling the existing recursive pair search | A tabled negative result needs completion detection over mutually dependent pairs. Premises would still be rediscovered per query, and the table ranges over pairs and conjunctive subgoals instead of a single-target closure. |
| Step, depth or time limits with a resource diagnostic | Would reject specification-valid programs, contrary to §1.3. A finite polynomial calculation exists. |
| Replacing only the final premise loop and keeping the earlier stages unchanged | Leaves the inference effects of §2.3 inside proof, and the local-projection and meet recursion outside the work bound. Callers therefore cannot reuse whole proof results. Not an acceptable U3 exit. |
| A re-entry guard returning `false` during environment construction | Turns unfinished construction into a negative answer; replaced by the *not ready* outcome (§3.4). |

## 4. Verification design

### 4.1. Workload families

Create a shared `tests/Workloads/OriginProofWorkloads.cs` for compiler regressions and opt-in Benchmark measurements. This and the other new files named in this plan are proposed paths, not existing interfaces.

| Family | Required shapes |
| --- | --- |
| A: successful program | Increasing borrowed Holder inputs; no explicit failed relation, including returned references. |
| B: failed composite relation | The original G74 shape and the intermediate-local `t_g1` shape, with/without `keep`, plus scalable Holder inputs. |
| C: failed atomic relation | The same environment with a simple failed relation and no result meet. |
| D: proven relation | Add sufficient explicit clauses at the sizes of A–C; vary clause order and include direct and transitive proofs. |
| E: result premises | Definition-side premises, caller obligations, excluded result-owner checks and Function Item/contract comparison paths. |
| F: anonymous and generic context | Anonymous inputs, Function Types, pair Semantics conditions and associated requirements. |
| G: graph structure | Chains, diamonds, cycles, independent binders, both-sided meets, shared local-Place dependency graphs and recursive Types. |
| H: edits and reuse | Repeated analysis; add/remove/restore a relation; change only a result Type, binder or admitted condition; alternate successful and unsuccessful sources. |

Vary Origin count, edge density, meet operand count, Type nesting and scope depth independently where possible. Keep valid cases and their invalid counterparts. Failure is an expected workload result, not a failed measurement setup.

Derive semantic expectations from SPEC. Record accepted/rejected status, required diagnostic code, location, relation and failed operand coverage. Baseline diagnostics are comparison evidence but do not override a demonstrated specification defect.

Use small sizes such as 3–5 inputs for old/new differential cases. Expand the new solver to 8, 16 and 32 inputs and larger structural cases justified by the graph bounds. Time-limit old pathological cases in isolated benchmark processes, record timeouts as censored evidence, and do not repeat a minute-long old case as routine warm-up. Timeouts are measurement controls, never language acceptance rules.

### 4.2. Correctness evidence

| Evidence | Unit | Requirement |
| --- | --- | --- |
| Purity check | U1 onward | §3.2 enabled for every test run; no violation. |
| Independent rule oracle | U2 | A small relation table saturated by I1–I5 and explicit edges, written from SPEC and calling no production extraction or closure helper. Compare exhaustive small graphs and deterministic generated graphs, including composite-only proofs, cycles and renamed distinct binders. |
| Universe-restriction argument | U2 | §3.5 argument completed, including the context-dependent rules. Oracle agreement inside the universe alone does not establish it. |
| Extraction tests | U2 | Real source programs for every catalog row, including the rule that obligations cannot prove themselves. |
| Whole-suite comparison mode | U3 | See below. |
| Metamorphic properties | U2, U3 | §4.3. |

**Comparison mode.** Add a temporary mode, enabled by an internal switch or environment variable, in which every production proof request also runs the old pure engine and compares the two entailment results. U1 has already removed inference effects, so only truth values are compared. A mismatch is an `InternalInvariant_Kd` failure that records both expressions, the environment identity and the new engine's witness. Give the old engine a fixed work budget per request; a request that exceeds it is recorded as not compared, never counted as agreement. Run the whole Functional suite once with the mode enabled. Store the evidence, including the number of compared and censored requests, under `artifacts/verify/<id>-origin-oracle`. Each mismatch needs a disposition under §3.5 before U3 exits. The old engine is supplementary evidence, not the correctness oracle.

### 4.3. Metamorphic properties

These properties test local reasoning directly and scale to generated graphs where the old engine cannot run.

At the closure level (U2):

- Monotonicity: adding an edge never removes an entailment.
- Order independence: permuting or duplicating premises changes no entailment.
- Determinism: the same environment always yields the same witnesses and counters.
- Renaming invariance: consistently renaming binders changes no entailment; distinct binders with the same spelling stay distinct.
- Equality symmetry: `a == b` entails both directions.
- Meet normalization: idempotence, commutativity and associativity of meets do not change any result.

At the source level (U3), for the checked body:

- Adding a clause to the function's own contract never turns a `Proven` relation into `Unknown`. Callers gain the corresponding obligations, so caller acceptance is outside this property.
- Reordering or duplicating clauses, and consistently renaming Origin sets, changes no acceptance and no judgment.
- Adding the queried relation as a local clause or Field relation never proves it.
- Adding an unrelated input changes no judgment of the existing relations.

### 4.4. Counters and measurement

Counters include proof entry calls, distinct normalized queries, environment builds and *not ready* outcomes, Type/context visits, premise edges per catalog ID, meet incidences, worklist insertions, edge activations, old search depth, verdict reuse, cache reuse and active/retained capacity. Count complete operations even if a fast path succeeds.

Emit counters only in opt-in instrumentation, together with the compiler build identity and the source/configuration identity of the run. A comparison script rejects runs whose compiler identity does not match the build under test, so a stale build cannot be measured silently. Keep instrumentation from changing unmeasured hot-path allocation behavior.

Measure parsing/setup, Binding, ownership, diagnostic construction and end-to-end checking separately where instrumentation can distinguish them. Pin source/configuration, runtime settings, warm-up and iteration counts before the final A/B comparison; document them in the new `src/Benchmark/OriginProof.md`. Use repeated matched runs on an isolated baseline and candidate, not a shared build containing unrelated edits, to claim a speedup.

Wall-clock benchmarks belong in `src/Benchmark`. Functional and allocation/work-bound regressions belong in xUnit. Preserve failed runs and raw data under `artifacts/verify/` and `artifacts/benchmarks/` respectively.

### 4.5. Existing tests, commands and diagnostics

Start with the following test groups and refine selection from the files actually changed.

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

Replace placeholders with checked selections and actual evidence paths. Session always includes all functional and allocation regressions; it does not implicitly select native fixtures or milestones. Toolchain identity verification is separate and must be reported as performed or not performed.

No unexplained diagnostic snapshot change is allowed. A demonstrated old defect may justify an intentional corrected result: minimize it, state the governing SPEC rule, add a focused regression and record the reviewed difference. Do not silently regenerate the baseline or treat all old behavior as normative. Verify affected observable records and retention behavior without expanding a unit into diagnostic enrichment.

## 5. Implementation units

The dependency order is U0 → U1 → U2 → U3 → U4 → U5 → U6. U0b depends only on U0 and may land before or after U1. S1 (§6) depends only on U2's completed argument. Each unit includes its reproducer, change and focused verification. Compiler units require Unit verification; each implementation session ends with one Session verification under [VERIFICATION](../../docs/dev/VERIFICATION.md). Stage only the unit's files, commit verified work and push without force.

### U0 — Freeze evidence and measurement contracts

**Change:** Add opt-in `Binding.OriginProofMetrics.cs`, the shared workloads and `OriginProofScalingTest`. Add representative small A–H cases and record a diagnostic baseline. Establish current behavior on a clean, identified revision. Define the counters of §4.4 and the normal-case comparison conditions before changing the algorithm. If existing Benchmark entries cannot execute these workloads, introduce the minimal `--origin-proof` driver here and keep the same workload/measurement contract for the later A/B comparison.

**Verification:** Related `OriginProofScalingTest`, `ResultPremiseTest` and `OriginRelationDiagnosticTest`; review every expected rejection against SPEC. Obtain whole-solution Release build evidence before running performance measurements. If Benchmark code is added, satisfy its Session and execution requirements before committing it.

**Exit:** Complete reproducer sources, source/configuration/compiler identity, raw counter/timing evidence, observed phase attribution, the `Q * P` measurement that decides environment granularity (§3.7) and reproducible benchmark commands. Choose and record numeric performance targets. Update PLAN's G74 description with confirmed current observations only. Do not close G74.

### U0b — Check cheap ownership exclusions before proof

**Change:** In `UncheckedOriginObligation`, evaluate the structural exclusion of input well-formedness obligations before `IsVerifiedOriginObligation` and `JudgeOriginObligation` (§2.4). Both orders return the same value. Ownership runs after Binding has closed initializer inference, so this proof should record no bounds; confirm that with an assertion, or with the purity check if U1 has landed. Do not otherwise change ownership semantics.

**Verification:** Related ownership and Origin relation tests and a diagnostic snapshot comparison with zero differences. Report from U0 counters how many full proofs the reorder removes in each family.

**Exit:** The U0 baseline stays the reference for later A/B comparisons; report this gain separately from the solver gain. Workloads whose old cost came only from excluded obligations no longer measure the solver, so keep failing, clause-provable and Binding-side families that still do. U4 replaces the reordered predicate with the discharge owner.

### U1 — Give inference effects explicit owners

**Change:** Audit proof callers (§3.1) and move required initializer fitting effects into constraint collection. Route inference and obligation mutations through the versioned helpers and enable the purity check (§3.2). Keep the old search behind the pure interface. Start from the three order-dependent effects in §2.3. Cover local annotations, initialization branches, invariant/equality directions, nested anonymous headers, subsequent uses of the same local and anonymous-function inputs inside a local initializer whose omitted Origins are still open when they appear as premise endpoints. Establish selection independence.

**Verification:** The purity check passes on every test. Compare permutations of equivalent premises, meet operands and fit branches. Check inferred Types, obligations, actual Loans and diagnostics. Add focused cases where old speculative proof branches encountered an open inference variable, and cases where a proof outcome could have influenced selection.

**Exit:** Every required bound has a semantic owner. No proof call mutates versioned state. Every behavioral difference is resolved against SPEC with a regression; unresolved cases block the dependent solver replacement, not unrelated work.

### U2 — Catalog, sealed environments and the pure solver

**Change:** Introduce the premise-rule catalog (§3.3), `Binding.OriginPremises.cs` for sealed environments and R1–R5 extraction (§3.4), and `OriginPremiseClosure.cs` for the closure with canonical order, provenance and witnesses (§§3.5–3.6). Express the structural fast path and meet absorption as catalog instances. Add `OriginPremiseClosureTest` and extraction tests before routing all production queries through it.

**Verification:** The independent oracle, extraction tests and closure-level metamorphic properties (§§4.2–4.3). Per-closure work assertions (§3.7). *Not ready* outcomes for re-entrant and incomplete contexts. Identify R5's governing SPEC rule.

**Exit:** Soundness and completeness for the selected finite rule model are explained, including the universe-restriction argument for the context-dependent rules. All oracle comparisons and properties pass, every catalog row has extraction coverage and a SPEC basis or a recorded gap, and the source-visible bound of §3.7 is verified. U2 and U3 may share one verified commit to avoid leaving an unused production solver.

### U3 — Integrate the solver across proof consumers

**Change:** Route the pure proof interface through sealed environments and the closure. Include contextual local-Place and meet processing so the old recursive search is not left in a wrapper. Preserve `JudgeOriginRelation`, region inference and Loan analysis as separate consumers. Reuse one closure for failed-operand selection without caching diagnostic records. Implement the function-level environment here if U0 decided so (§3.7). Add the temporary comparison mode (§4.2).

**Verification:** The comparison mode over the whole Functional suite, with its evidence. Source-level metamorphic properties (§4.3). Compiler acceptance, required diagnostics and actual-Loan regressions. Repeated ownership and emission checks, result-premise exclusion, capability queries and rejected fit branches. A–G through sizes 8/16/32 against the bounds of §3.7 rather than an unexplained timing ratio.

**Exit:** No remaining proof path can reintroduce path enumeration or inference effects. All differences have a SPEC-based disposition and censored comparisons are counted. Large valid and invalid workloads complete with bounded measured work; the proof/region judgment distinction and diagnostic source attribution remain intact.

### U4 — Discharge owners and one verdict per obligation

**Change:** Record the discharge owner at obligation creation and fold `WellFormed` into it (§3.8). Replace U0b's structural predicate with the recorded owner. Compute one verdict per obligation once its inputs are final, and switch ownership reporting, `SupportsOriginObligations` and diagnostic chain construction to it.

**Verification:** Diagnostic snapshot comparison with zero unexplained differences. Every obligation kind has an owner, and every consumer reads the same verdict. A test edits the inputs after verdict computation and expects the `InternalInvariant_Kd` failure. Counters show one judgment per obligation per Binding generation.

**Exit:** No consumer re-derives a verdict or infers a discharge owner from an obligation's shape. Caller repetition is within the bounds of §3.7.

### U5 — Validate lifecycle and optimize measured repetition

**Change:** Exercise environment reuse, scratch storage, provenance storage and invalidation. Extend reuse across completed Binding consumers only if measurements justify it. Keep query-local environments for unstable contexts. Add lifecycle/allocation coverage, and complete the `--origin-proof` Benchmark entry and its measurement document.

**Verification:** H-family edits, same-Compilation and cross-Compilation isolation, result-owner exclusion changes, substituted endpoints, header completion, removal of source references and many equivalent edits. Assert warm zero allocation where already guaranteed, bounded retained capacity and valid results for every measured operation. Measure the purity check, canonical order and provenance overhead. Compare small normal library workloads as well as pathological workloads under fixed conditions.

**Exit:** No stale proof or verdict survives an environment or generation change, and no stale syntax reference or per-query retained growth accumulates. Cache scope, invalidation dependencies, measured benefit and retained-space cost are documented. If broad reuse adds no material benefit, complete the unit with the narrower tested lifetime.

### U6 — Final verification, evidence and documentation

**Change:** Remove the comparison mode and obsolete search machinery once their evidence is retained. Keep the deterministic semantic, metamorphic and work-bound tests and the purity check. Prepare CODEMAP changes for new responsibilities and entry points, concise PLAN/HISTORY updates and any verified STATUS boundary changes before final verification.

**Verification:** Final Session on unchanged source/configuration, the selected native O0/O2 fixtures, Programs 20/26/27/37, diagnostic snapshot comparison and the relevant performance measurements. A successful final Session may also satisfy the last Unit when it covers all required checks; do not run a duplicate merely for the mode name. Associate Verify evidence with the commit using `scripts/verify-commit.ps1`, then push.

**Exit:** All conditions in §7 pass. Record limitations and residual independent issues explicitly. G74 may be closed only with whole-path evidence; Program 26 passing alone is insufficient.

## 6. Specification position

No formal specification change is required for G74. §15.3.6 already lists the permitted rules and states that the solver neither enumerates arbitrary regions nor requires general theorem proving. It states the boundary negatively, however, and does not say which expressions a derivation may use or that premise order is irrelevant.

**S1 — Conditional clarification of the proof universe.** After U2 completes the universe-restriction argument for every catalog rule, consider adding to §15.3.6 a sentence with this content: entailment does not depend on premise order, and every consequence is derivable using only the Origin expressions in the queried relation, the premises and their subexpressions.

- **Rationale:** it publishes the proof universe as part of the contract (Local Reasoning, bounded and predictable inference) and prevents a later implementation from adding search over synthesized regions. Because the argument shows that no consequence is lost, it changes no acceptance.
- **Conditions:** apply it only if the argument covers every catalog rule, including R5 and pair-layer admission, and if no specification example, test or milestone program changes its result. Otherwise record the obstacle here and do not apply it.
- **Procedure:** a separate, coherent unit that states the rationale against the Kimigayo Principles in its commit and updates any affected chapter text and examples together. Record the intake in [INTEGRATED](../INTEGRATED.md), mark this proposal 一部取り込み (partially integrated) for that scope, and keep the formal specification independent of this draft. Recheck [SETTLED](../../docs/SETTLED.md) at that time.

If a concrete contradiction or a deliberately better rule is established during implementation, handle it the same way as a separate unit. Merely matching the old engine never justifies a new normative sentence.

## 7. Completion conditions

G74 is complete only when all of the following are evidenced:

- Pure proof is independent of inference mutation, premise order and prior query order, and the purity check is permanently enabled.
- Proof accepts only sealed environments; *not ready* never becomes `Unknown` or `Refuted`.
- Every proof rule is a catalog entry with a SPEC basis or a recorded specification gap; all permitted premise sources and quantification/exclusion boundaries are represented; local/Field obligations cannot establish themselves.
- Composite relations retain their meaning; fixed-Origin proof and finite/inferred-region judgments remain distinct.
- Origin proof outcomes do not influence selection, and invariant positions are never weakened to a meet.
- Source expectations, the independent rule oracle, the metamorphic properties, the whole-suite comparison mode and the affected regression suite agree, with every intentional behavior correction and censored comparison documented.
- Each obligation has a recorded discharge owner and exactly one verdict per Binding generation, shared by all consumers.
- Valid and invalid scalable workloads have bounds on environment construction, closure work, caller repetition and retained storage, stated in source-visible terms. No old recursive path escapes that accounting.
- Repeated fixed-condition measurements, tied to compiler identity, establish a practical improvement on pathological workloads and an acceptable normal-case cost against the targets chosen in U0, which are not relaxed after a failing comparison.
- Existing allocation guarantees pass with their original fixed warm-up conditions. Capacity and object-reference retention remain bounded across edits and failures.
- Required Unit/Session, native fixtures, milestones and snapshot checks pass on identified inputs, and evidence is associated with the committed implementation.

## 8. Open questions and change triggers

| Question or finding | Required disposition |
| --- | --- |
| Old speculative search adds an inference bound that the explicit fit collector lacks. | Minimize the case and decide from SPEC whether the fit collector is incomplete or the old effect is incorrect. Resolve before integrating the new solver. |
| A premise endpoint changes when resolved at a different use. | Keep use-specific normalization and expand the environment identity; do not share the affected result. |
| Pair Semantics admission or associated formation re-enters proof. | Return *not ready* and establish an explicit phase/dependency boundary before sealing. |
| *Not ready* reaches a final deadline in a valid program. | Treat it as a construction-order defect; fix the dependency, never report it as `Unknown`. |
| Input well-formedness traversal crosses a nested Function Type binder. | Check §15.3.4 with a real source case; preserve only premises in the legal quantification scope. Do not copy an old traversal mechanically. |
| No SPEC rule is found for R5. | Keep the current behavior, record the open item in PLAN, and decide in a separate specification unit whether an existing borrowing rule implies the premise or a rule must be stated. |
| Local-Place or Type graph extraction revisits shared dependencies. | Deduplicate discovery with the necessary context, count its states and include the cost in the full bound. |
| The closure and independent SPEC oracle disagree. | Preserve the smallest graph and correct the rule encoding or algorithm. No performance claim supersedes a semantic discrepancy. |
| A metamorphic property fails at the source level but not at the closure level. | The difference lies in extraction or judgment; locate it with witnesses before changing the solver. |
| A verdict input changes after verdict computation. | Move verdict computation later or split the obligation; never re-judge silently. |
| A discharge owner cannot be determined at obligation creation. | Keep the structural predicate for that kind with a focused test, and record why. |
| Purity check, canonical order or provenance storage costs measurable normal-case time or allocation. | Reduce the representation; do not disable the check or the canonical order. |
| Current profiling attributes the dominant cost elsewhere. | Keep responsibility separation where justified, but revise the performance unit around the measured owner before promising a speedup. |
| Cache sealing cannot be shown safe. | Use query-local storage or completed-Binding-only reuse; caching is not a condition for solving the search problem. |
| Normal cases regress or retained memory grows. | Revisit extraction granularity, lazy construction and cache lifetime. Preserve failed evidence and existing assertions. |

## 9. Documentation and proposal lifecycle

Creating or revising this file changes no implemented support claim. Keep the current SPEC, STATUS, PLAN and integration register unchanged for planning tasks.

During implementation:

- Update CODEMAP when entry points or responsibilities change, including the catalog, sealed environments and verdicts.
- Update PLAN with confirmed G74 scope and progress, and PLAN_HISTORY with concise session evidence.
- Update STATUS only when a verified support boundary changes.
- Record benchmark procedures in `src/Benchmark/OriginProof.md` and detailed evidence in the ignored artifact directories.

No public Kimi declaration change is planned, so LIBRARY and library style updates are not expected.

This proposal remains in `draft/Proposals` while its design and implementation disposition are open. S1, if applied, follows the intake rules in §6. Any later closure follows the existing register rules; completing this planning document is not integration, freezing or implementation completion.
