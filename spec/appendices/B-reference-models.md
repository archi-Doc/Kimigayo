# Appendix B. Non-normative reference models

[Specification index](../../SPEC.md)

**Non-normative.** All algorithms in this appendix are optional. Any alternative must preserve the language and Appendix A, including excluded-syntax validation and committed lookup decisions.

## B.1. Build pipeline reference model

Pass boundaries and scheduling are implementation choices; semantic dependencies remain mandatory.

The logical compilation pipeline is:

```text
Solution -> Project -> Compilation(inputs)
    -> SourceDocuments -> Tokenization -> Parsing / Koto tree
    -> Directive Binding and selection of lookup environments
    -> Declaration/Header Binding and definition-side environments
    -> Body Binding, Type checking and overload selection
    -> CFG construction and symbolic generic-body verification
    -> Common effect-family fixed points, public guarantees, conformance and usage proofs
    -> Generic instantiation and published dependent/representation obligation resolution
       (without directive reselection or new body-derived semantic conditions)
    -> Explicit implementation selection{the} closed defining set
    -> Concrete control-flow, Access Effect, ownership, Origin, lifetime and cleanup analysis
    -> Final acceptance and supported-generation-set validation
    -> Layout / ValueLowering / FunctionAbi / CleanupPlan
    -> LLVM lowering -> pre-optimization .ll + .link.json
    -> build: LLVM verification / opt / llc -> object
    -> Manual native linking -> executable -> execution validation
```

Before publication, verify §12.4.4's common implementation-family guarantee; selecting one concrete implementation cannot strengthen it. Concrete generation then analyzes the selected body's substituted CFG/effects. Early shared-body planning/caching is allowed; executable lowering waits for resolved effects, Loans, and cleanup. Sharing preserves the finalized operations (§21.3); no unverified body may be emitted. The initial output boundary is §20.8: generic sharing, external Library ABI, and automated linking/running are not implied by this pipeline.

## B.2. Directive processing sequence

This schedule preserves §19’s source selection and validation rules.

The evaluation and Syntax-processing sequence is:

```text
Parse a directive Condition
    -> resolve all Names and validate all operands against the prepared environment
        -> True #if: parse the controlled Syntax
        -> False #if: scan only required token/layout/body/directive structure
        -> #switch: validate all arm Conditions; parse every arm with nested #if rules
        -> Error: report required diagnostics and recover structurally
    -> retain any implementation-internal validation work with its source-defined traversal
    -> complete required nested Conditions even in unselected reached #switch arms
    -> discard speculative grammar errors only where False #if rules require skipping grammar
    -> diagnose Names absent{the} prepared environment; do not retry after generic Binding or instantiation
    -> resolve selections that change a scope's lookup environment before ordinary Name resolution using that environment begins
    -> require a final result and completed validation before finalization
    -> bind and lower only the selected Syntax
```

## B.3. Borrow checking reference algorithm

One possible borrow-checking sequence is shown below. Type checking uses the Structural Completion, Runtime Reachability, and unreachable-code rules in §14.9–§14.10; the sequence does not infer Types from optimized CFG reachability. Implementations may interleave these tasks to resolve their dependencies.

```text
1. Type-check and generate subtype constraints.
2. Build the control-flow graph and compute liveness.
3. Generate Origin and well-formedness constraints.
4. Instantiate call-site Origins and propagate result Loan requirements.
5. Solve region constraints to a fixed point.
6. Reject local-to-universal region flows.
7. Compute active Loans and check overlap conflicts.
8. Check reborrows and destructor observations.
```

The region and Loan analyses may be implemented using Datalog or an equivalent fixed-point solver.

## B.4. Callable and object implementation strategies

**Non-normative strategy notes.** Use the concrete environment and common-function storage/entry contracts of §21.2.5. Keep logical capture initialization/destruction order separate from physical offsets. Direct resolution and allocation removal are optimizations under those contracts, not changes to typing.

Implement §12.4.4's required abstract verification with worklists or equivalent fixed-point algorithms. A statically selected method/accessor may have an ordinary entry and a receiver-adjusting adapter. The adapter implements an already permitted call; it supplies neither a new public guarantee nor an unrestricted exclusive receiver. Summary encoding and physical entry sharing are implementation choices.

Descriptor sharing/canonicalization may permit address comparison when it preserves language identity; distinct physical descriptors may still denote one Runtime Type Identity.

## B.5. Type relation and operation plans

**Non-normative.** A Binder can map the [normative relation table](../03-types-and-values.md#38-type-relations-and-expression-operations) to separate APIs such as the following. Names and signatures are illustrative, not language requirements.

| Illustrative API | Responsibility |
| --- | --- |
| `IsIdenticalType(A, B)` | Compare normalized complete Types, including bound Origin identity. |
| `IsSubtype(A, B, constraints)` | Prove static fitting without inserting value operations. |
| `IsExpectedResultCompatible(A, B, constraints)` | Apply only the static relation permitted during candidate result filtering. |
| `CanImplicitlyAdapt(expression, target, context)` | Select an adaptation permitted in that use-site context. |
| `CanExplicitlyAdapt(expression, target, context)` | Select one operation admitted by the resolved explicit target. |
| `CanAcquire(expression, access, state)` | Check the selected access against Place, initialization, ownership, and Loan state. |

Adaptation/acquisition APIs can return plans containing the selected operation, complete result Type, Origins, access, Copy/Move/Loan effects, and deferred obligations. Static relations can return proven, rejected, or unresolved judgments. Isolate candidate plans until commitment, then validate/lower the selected plan in evaluation order. Runtime checks remain part of that operation, never reasons to seek another conversion.

## B.6. Match binding plan

An implementation may attach the following information to bound Pattern positions or an acquisition plan. Names and storage layout are illustrative:

| Information | Meaning |
| --- | --- |
| `MatchedType` | Complete Type at the position before implicit dereference |
| `AccessMode` | `Owned` / `Shared`, including that position's implicit dereference |
| `ImplicitDeref` | `None` / `SharedOnce` at this position |
| `MovePath` | Subject position with owned initialization/destruction tracking, when applicable |
| `CandidateSymbol` / `GuardReadType` | Binding position's candidate Identity and shared-read Type |
| `BodySymbol` / `BodyBindingType` | Distinct body-local Identity and acquired Type |

AccessMode describes the path, not the value's Semantics or the Owned capability. The internal label `Owned` denotes by-value access; an implicit dereference changes it to Shared, inherited by descendants. Binding a `ref/E` Subject with `let r` uses `Owned` / `None`; a Case Pattern inspecting its referent uses `Shared` / `SharedOnce`. Guard reading remains shared in either mode.

Retain Candidate positions, Origin/Loan dependencies, and Copy/Move/Borrow/Reborrow plans alongside these facts. Candidate/body Symbols and binding Types are needed only at Binding positions. Shared position tracking gives no Move authority. Do not reconstruct access effects solely from MatchedType or reuse an instantiation's plan when its effects differ.

## B.7. Generic generation and specialization strategy

**Initial compiler guidance, not additional language rules.** This implements §21.3 and §21.4.6 with bounded exploration. Numerical defaults, setting names and report formats require implementation measurements.

### B.7.1. Plan keys and generation order

Within one generation scheme, distinguish these key roles rather than prescribing four hashes:

| Plan | Key contents |
| --- | --- |
| Semantic | Compiler build/verification rules, definitions/bindings, semantic environment, closed selection sets and dependencies |
| Body | Selected implementation, typed operations/cleanup, internal ABI, reader schema and embedded fixed facts |
| Entry | Entry ABI, destination body, capacity/alignment, adaptation and embedded constants/references |
| Context content | Reader schema and its integers, operation pairs, metadata contents and references |

Omit concrete Types and frame capacities unused by the body; retain exact entries/contexts embedded by direct operations. Register logical nodes before resolving cyclic contents and edges. Do not recursively expand all referenced contents into keys. Resolve hash collisions by structural comparison, recording visited node pairs for cycles; memory addresses and registration IDs have no semantic meaning.

1. Register and validate required closed substitutions/dependencies and check mandatory resources.
2. Group substitutions representable by common typed operations and internal ABI into baseline sharing classes. Apply light simplification, then fix baseline bodies, entry contracts and resource estimates.
3. Within each class, form a bounded set of choices from small, operation-consistent fixed-fact sets. Each choice assigns **every member** to a body, including unspecialized members.
4. Select one choice per class under B.7.2–3, preserve entry ABIs, and finalize each reader's schema/context.
5. Recheck resources, fixed facts and every connection; emit only needed artifacts. If optional choices exceed resources, revert in stable reverse adoption order without adopting the same choice again.

During selection, class-to-class calls use stable entries. Do not make caller choices depend on incorporating a callee's internal body/schema at this stage. Recursive generation still requires finiteness checks, but an entire recursive strongly connected component is not one specialization candidate.

### B.7.2. Exclusive accounting within a class

Use estimated instruction counts from the common plan; do not trial-run later lowering or LLVM to evaluate candidates.

~~~text
growthCost = max(0, instructions in the choice's required bodies
                   - instructions in the baseline class)

Baseline shared body: 100 instructions. Specialized body: 60.
    Shared body still used: 100 + 60 - 100 = 60 growth.
    All uses replaced:      max(0, 60 - 100) = 0 growth.
~~~

Count identical bodies once within a class. Subtract the baseline shared body only when **all uses** are replaced. Estimate each choice against the baseline, not the previous choice; do not credit savings to another class.

Exclude routine entry/adapter ABI adaptation, fixed reservation and context data from the growth budget, but include them in mandatory resource accounting. Body duplication or expansion moved into an entry/helper still counts as body instructions; only routine boundary work is excluded.

Estimate required helpers as part of each class's self-contained body group. Deduplicate across classes after selection without refunding savings or revising costs. Resource limits cover counts, instructions and bytes of entries/contexts as well as bodies; their cost need not grow linearly with instance count.

Estimate benefit as context-dependent operations removed from the lightly simplified baseline. Weight operations remaining in loops by bounded nesting depth; a hoisted load counts once. Do not anticipate another candidate's adoption or future LLVM inlining. Reject negative-benefit and unchanged choices; positive cost requires positive benefit.

### B.7.3. Two growth limits and deterministic selection

Before optional choices, fix baseline instruction quantities Bf for each original generic function and B for the generation unit. Aggregate all of a function's classes into Bf; B also includes internal nongeneric code. Use B.7.2's estimates before cross-class deduplication. Count bodies generated from external source dependencies, but not separately linked existing native code.

Use the separate product/test planning regions in [§21.3.7](../21-layout-runtime-and-code-generation.md#2137-product-and-test-generation) as generation units for B and Bf, not the final LLVM module. Fix the product under current Composition before adding test requests; reused product code is not charged again. Code omission preserves the generation closure and does not redistribute these budgets.

| Growth limit | Formula |
| --- | --- |
| Per original generic function | m * k * Bf |
| Whole generation unit | m * r * B |

Expose one finite nonnegative multiplier m. Internal coefficient k, rate r and rounding are deterministic; zero Bf or B gives zero growth allowance. Do not add an independent body-count optimization budget.

Sort finite choices as follows:

1. Effective zero-cost choices first, by descending benefit.
2. Positive-cost choices by descending benefit/cost.
3. Break ties by stable semantic-plan, class and choice keys.

Visit this order once. Adopt a choice only if both limits and resource conditions permit it, then discard all other choices for that class. Unselected classes retain their baseline. Compare ratios without overflow and never divide by zero. Fixed costs and one choice per class avoid post-adoption reevaluation; this greedy method does not guarantee optimal allocation.

Sorting/selecting n generated choices can take O(n log n), excluding candidate construction, member assignment and connection/resource validation. Bound candidate count, assignment description size, fixed-fact sets and search work; do not enumerate full Cartesian products.

| Profile policy | Multiplier and exploration |
| --- | --- |
| O0 | m = 0; skip optional search, retain mandatory generation and light simplification |
| O2 | Default multiplier chosen by measurement |
| Size-focused | Default m = 0; prefer nongrowing simplification and replacement |

Zero growth still permits concrete caller entries, mandatory separation and reuse. Backend optimization is controlled separately; this multiplier does not prohibit every final machine-code duplication or set an exact binary-size limit.

### B.7.4. Implementation efficiency and reporting

Before new duplication, propagate Fixed facts, directly resolve calls and remove unused branches, transfers and slots. Reuse or hoist context loads only where valid, without unconditional loads on null paths or excessive register pressure.

Place fixed-size temporaries in the body frame. Align scratch to its maximum requirement; the first positive-size region can then have offset zero. Use stable greedy storage coloring or a similar bounded reuse algorithm; optimal coloring is unnecessary.

Make small entries easy to inline, without blanket alwaysinline. Verify that entry fusion reduces calls/frames while preserving scratch lifetime, loop stack behavior and accounting for body expansion.

Reuse requirements, TypeLayout, FunctionAbi and CleanupPlan. Store candidates as differences from baseline plans rather than full copies. Create needed nodes with a worklist and reuse array/search-buffer capacity.

Generation reports should explain sharing, fixed facts and required separation; candidate costs, benefits and rejection; budget consumption; entry/body/adapter counts; context/metadata bytes; and frame reservations. Resource diagnostics should identify the limit kind, configured limit, consumption, definition location and growth path in bounded output.

Measure runtime, code/frame size, compilation time and peak memory, and compare results, effect order and cleanup. Check flattening's data cost, concrete-entry adaptation, semantic-cache reuse, context loads, stack probes/unwind and post-optimization code. Conformance cases are centralized in Appendix A.14; this guidance makes no claim of completed implementation or measured gains.
