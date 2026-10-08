# G82 Generic Default Context Repair Plan

Date: 2026-10-08. Status: proposed implementation plan, revised after design review; implementation and execution verification are pending.

## 1. Purpose and authority

### 1.1 Purpose

Repair G82 by giving declaration-derived information one canonical interpretation wherever it is used: the **interpretation context** of the operation being analyzed, which fixes both the Type substitution and the proof environment. Judge the repair against one correctness oracle derived from the specification (§3), not against the list of reported symptoms.

### 1.2 Authority

This is a non-normative implementation proposal. [SPEC](../../docs/SPEC.md) and its chapters remain authoritative. No language-rule change is proposed. §3.4 proposes one non-semantic clarification of SPEC §7.2.3 that makes an existing §8.10 consequence visible where defaults are defined; it takes effect only through a specification intake (U1). Creating or revising this document does not implement the repair, establish new support, close G82 or integrate a specification proposal.

The required behavior already follows from default evaluation and universal declaration checking (§7.2.3), generic and Semantics checking and instantiation (§8.10), requirement effect bounds (§8.4.10), acquisition (§3.5, §15.1), match acquisition (§15.1.6), call borrow reservations (§15.6.7), destruction (§16.2) and concrete generation (IMPL §21.3.1). Consult [SETTLED](../../docs/SETTLED.md), [CODEMAP](../../docs/dev/CODEMAP.md) and [verification scope](../../docs/dev/VERIFICATION.md#verification-scope) before implementation.

### 1.3 Principle alignment

- **One Concept, One Canonical Form:** one interpretation context per operation and one resolver family keyed by it replace the current hand-composed substitutions (§5.1). Default substitution stages are composed once per context (§5.3). The inline default replica and its universal declaration check compute one meaning, and their agreement is a checked invariant (§3, §7.1, §8).
- **Local Reasoning:** a call's legality depends only on the callee's published contract and verified default plus caller-side facts (§3). Every resolution names its context at the use site, and the root context is written explicitly (§5.1).
- **Explicit Semantics:** whether a Type is declared or already analyzed is explicit in the API (§5.2). `CopyOrMove` is resolved by one helper under one proof environment, and an unknown proof in a closed context is an internal failure, never a silent acceptance (§5.4).
- **Compiler Server Protocol:** each code unit is a small, reviewable switch of identified consumers from the root context to their actual context (§6). Generated context replicas make parity checkable and report uncovered combinations as remaining uncertainty (§7.1). Evidence is tied to exact source and configuration (§7.3).

## 2. Problem and evidence

### 2.1 Sources and confidence

The user supplied `G82a.md` and `G82o.md` under `C:/App/Downloads/Fix/`. They are comparison inputs, not authoritative compiler contracts or independently verified execution evidence. This plan is self-contained and does not require their local paths at implementation time.

G82a identifies acquisition and remainder-context mismatches and reports static investigation only. G82o reports six failure families from scratch probes and proposes a cross-phase context API repair. Its probes used a build that mixed in other uncommitted work, so its outcomes must be recovered or recreated in U0 before they are treated as reproduced facts.

Planning snapshot: `35bbc6ba4a501ec7942e6ac3452313d4c4562f33`; this revision was prepared at `22a1a958`, which adds only documentation. Both worktrees were clean. Recheck the current tree and active work before implementation.

### 2.2 Structural diagnosis

Declaration-derived information passes through up to three substitution stages: the call substitution of each enclosing default context, from the innermost outward, and then the body's instance or Semantics case. The default stage applies to operation intervals inside one body, but many consumers still assume a single body-wide interpretation. Three properties of the current code let that omission recur:

1. **Several spellings of one interpretation.** The builder's context-aware `Concrete`, `OwnershipBody.ConcreteAt(type, operation)`, `Concrete(SubstituteDefaultType(type, Place.DefaultContext))` in value lowering, `Matched` over `SignatureType` in match lowering, body-only `OwnershipBody.Concrete`, instance-only `SignatureType` and the effect-summary `Type` query each compose the stages by hand. The body-wide forms are the easiest to call, so new code selects them.
2. **Declared and analyzed Types behind one query.** `Binding.EffectBounds.Type` receives both analyzed Place Types, already resolved when the Place was created, and declaration Pattern Types such as `MatchedType`. In a default context it can be correct for at most one of them.
3. **Proof environment taken from syntax position.** Place construction proves Copy with `ProveCopy(type, source)`. Inside a default, `source` is callee syntax, so a substituted caller Type is proved under the callee's premises. A repair that proves the expected acquisition under a different environment from the actual Place reintroduces D1 (§5.4).

The current source shows these mismatches:

| Location | Observed mismatch |
| --- | --- |
| `OwnershipAnalysis.Instances.Concrete`, `OwnershipAnalysis.Place` | Type resolution includes the active default context, but committed `CopyOrMove` normalization is gated by `Substituting`, which recognizes only instance and case analysis. |
| `OwnershipAnalysis.Enums.CheckAcquisition` | The same body-wide flag decides whether an unresolved expected acquisition may match Copy or Move. |
| `OwnershipBody.Comparisons.CallableReceiverType` | An analyzed Place Type is compared with a declaration-derived receiver Type resolved without the operation's default context. |
| `OwnershipBody.LocalRegions`, `OwnershipBody.MovePaths` | Projection Types are reconstructed from shared syntax with body-wide `Concrete`. |
| `Binding.EffectBounds.LeftParts`, `EffectSummary.Type` | Declared Pattern Types and analyzed plan Types share one query that carries no default context. |
| `BodyLowering.Closures.LowerValueCall` | Declaration-derived receiver, signature, result and parameter Types are passed to instance-only `SignatureType`. |

Existing default-context intervals and parent chains, `Place.DefaultContext`, `OwnershipBody.ConcreteAt` and match lowering provide the infrastructure. The repair consolidates them (§5) instead of adding an independent substitution engine.

### 2.3 Failure families

| ID | Input family | Prior report | Oracle class (§3.2) |
| --- | --- | --- | --- |
| D1 | Omitted generic default constructs `Option<T>.Some(factory())` | `CheckFaulted_Kd`: committed payload acquisition disagrees with the source Place | Defect: internal fault. |
| D2 | Default constructs `.None` and matches `.Some(let value)` | Ownership succeeds; composite-acquisition generation fails | Defect: generation refuses a verified plan. |
| D3 | Default invokes a generic `F: Callable` value | Callable Loan validation fails; a later lowering mismatch is predicted | Defect: Loan failure absent from the universal check and the call's own checks. |
| D4 | Default partially moves `(Option<T>, string)` | Spurious Loan conflict; the ordinary-body counterpart reportedly succeeds | Defect: spurious conflict. |
| D5 | `confined` implementation evaluates a generic default with `.Some(_)` | Spurious unknown/destruction effect for `T = i32` | Defect: spurious exact effect. The same form with an output-producing owner is a permitted rejection. |
| D6 | Default invokes a generic requirement such as `a.equals(a)` with `T = string` | Reference-argument Loan/Origin validation fails | Defect: Loan/Origin failure absent from the universal check. |

These are reported investigation families, not six independently confirmed regressions in the planning snapshot. U0 confirms each classification. Do not infer runtime corruption from a path that currently stops at checking or generation.

### 2.4 Original reproducer

The saved input is `artifacts/verify/c1-default-pattern.kimi`. `Helpers.evaluate<T, F>` receives `sample: T` and `factory: ref/F`; its omitted `marker` default constructs and moves/matches `Option<T>`, returning 1 or 0. `Worker.run`, implementing a `confined` Contract requirement, invokes it with an `i32` factory. There is no object adaptation. Its top-level `()` never runs `Worker.run`, so native execution of this file does not exercise the repaired path: retain it as the checking reproducer and add an executable counterpart that calls `run` and asserts its result.

`GenericDefaultTest` is not limited to `T is Copy`; its Function Item examples use `T is Owned`. The coverage gap is the interaction of default contexts with unresolved acquisition, generic Callable invocation and residual ownership.

### 2.5 Historical uncertainty

`c0e079db` introduced generic-default context support; `0134ba69` recorded G82 during C1. The supplied probes used a build with other work mixed in, and the older comparison build rejected the relevant forms as Unsupported. Neither source-line age nor that rejection establishes the first failing commit or excludes a C1 contribution.

U0 compares clean current and pre-C1 sources; compare the introduction boundary separately only if attribution remains useful. Historical attribution is not a prerequisite for a correct repair, and unresolved attribution must remain explicit.

## 3. Correctness oracle

### 3.1 Default evaluation is an instantiation

SPEC §7.2.3 checks every default at declaration time as a generic body is checked (§8.10). The Instantiation stage of §8.10 checks the call's declared contract and ordinary argument/Loan validity, substitutes verified plans and resolves exact layout, representation and effects without adding semantic use conditions. §8.10 also requires that a call satisfying the public contract never fails later because its callee newly discovers a semantic body requirement.

The inline replica of a default at a call is therefore an instantiation of the declaration-verified default. Declaration checking already builds a complete ownership body for each default (`OwnershipAnalysis.Build` with a declaration default), in addition to the definite forbidden-acquisition visitor, so the universal result is available as a reference.

### 3.2 Outcome classes

For a default accepted by declaration checking, a call that omits it has one of these outcomes:

| Class | Meaning | Treatment |
| --- | --- | --- |
| Accepted | The call checks and runs as the instantiated plan specifies. | Verify the result and cleanup. |
| Permitted rejection | (a) The call's declared contract or ordinary argument, receiver and Loan validity, including call borrow reservations (§15.6.7); (b) the caller's effect bounds against the exact effects the default contributes (§8.4.10); (c) target representation obligations. Documented compiler resource exhaustion remains distinct and is not semantic invalidity (§8.10). | Keep as a negative test with its ordinary diagnostic. |
| Recorded gap | A located Unsupported result for a form the implementation has not reached. | Allowed only with a STATUS/PLAN entry and an owner; never counted as repaired. |
| Defect | Anything else: an internal fault; an acquisition, Loan, Origin or conflict diagnostic that neither the universal check nor the call's own checks produce; generation refusing a verified plan; a missing or spurious exact effect. | Repair. |

Exact effects are neither missing nor spurious. A `T = i32` residual contributes no destruction effect; an owner whose destructor produces output does.

### 3.3 Applying the oracle

- Classify each D family and every later finding with §3.2 before designing its repair.
- Derive expected test outcomes from the classification (§7.1), not from the prior reports.
- A finding classified as a Defect cannot be closed by relabeling it Unsupported. A Recorded gap that lies on the original G82 route is a prerequisite, not a reason to close G82 around it.

### 3.4 Proposed SPEC clarification

Append to the paragraph of §7.2.3 that begins "Every default is checked at declaration time":

> Each evaluation of an omitted default instantiates that verified default (§8.10): the call's bindings are substituted and its exact acquisitions, cleanup and effects are resolved without adding a semantic use condition. Beyond the call's declared contract and ordinary argument and Loan checks, it can reject the call only through the exact effects it contributes to effect bounds (§8.4.10) or through representation obligations; documented compiler resource exhaustion remains distinct (§8.10).

- **Rationale:** a reader of §7.2.3 learns locally, without deriving it through §8.10, that omitting an argument never adds a hidden requirement (Local Reasoning). The sentence states no new rule; it links default evaluation to the single instantiation rule (One Concept, One Canonical Form).
- **Disposition:** integrate through U1 and record it in `draft/INTEGRATED.md`. If review finds the cross-reference redundant, record the rejection and its reason there; the oracle of §3.2 still follows from §8.10 alone.

## 4. Scope and invariants

The repair covers declaration-derived Types, calls, acquisitions and capability proofs used by default expansion, ownership solving, cleanup effects and generation. It includes nested defaults, body instances and Semantics cases, generic forwarding where otherwise supported, and reuse of shared syntax across calls.

G82o reports that `InstantiateForwardedCall` returns null for a generic caller's requirement call inside a default. Treat this as a candidate boundary to reproduce in U0, not an established out-of-scope fact. If it lies on the original G82 route, it is a prerequisite (§3.3).

Preserve these invariants:

1. **Universal declaration checking.** Check every default in its declaration environment, even if all calls supply that parameter. A successful concrete call cannot legalize an invalid generic declaration.
2. **Evaluation order.** Acquire explicit arguments first; evaluate omitted defaults once in declaration order. Preserve prepared-slot protection, abandonment cleanup and the prohibition on escaping new Loans of prepared arguments.
3. **Declaration-level Binding plans.** Shared syntax and Binding plans remain declaration-level information. Never overwrite them with one call's concrete Types or acquisition results.
4. **Explicit context.** Every Type, call, acquisition and capability resolution names its interpretation context. The root context is written explicitly; no context-free resolver remains (§5.1).
5. **Single application.** Each substitution stage is applied exactly once, in the fixed order of §5.3. Analyzed plan Types and resolved call plans are never resolved again (§5.2).
6. **One proof environment per context.** Place construction, acquisition normalization and acquisition validation prove capabilities of a Type under the same environment, taken from its context (§5.4).
7. **Acquisition resolution.** `CopyOrMove` is resolved from the analyzed Type under that environment. Unknown stays unresolved only while the Type has free generic parameters of the analyzed body; Error keeps the existing failure path; explicit `@move` keeps its transfer semantics for Copy Types.
8. **Consistency checks remain.** Do not remove an acquisition, Loan, Origin or lowering check, and do not accept both Copy and Move merely to suppress a mismatch.
9. **Cleanup responsibility.** Wildcards leave responsibility with the Subject; acquired and residual parts have distinct cleanup. Guard paths keep their pre-transfer responsibility. A reference payload does not own its referent.
10. **Context identity.** Context IDs, composed substitutions and operation IDs belong to their owning body and source snapshot, and stay correct across reanalysis, edits, sibling calls and recursive evaluators.

## 5. Design

### 5.1 Interpretation context

An interpretation context identifies, within one owning body and source snapshot:

1. the composed default substitution of a default-context interval, or none for the body's root context;
2. the body's instance or Semantics case;
3. the proof environment for capability proofs about Types resolved in it (§5.4).

Represent it as a small value, provisionally `InterpretationContext`, keyed by the existing default-context index. A consumer obtains it from exactly one of:

- an operation, through `DefaultContextAt` (emission looks it up once per operation and reuses it);
- a Place, through `Place.DefaultContext`;
- a plan record that stores the context or an already analyzed Type.

One resolver family takes the context as a required argument:

| Resolver (provisional) | Replaces |
| --- | --- |
| `Resolve(declaredType, context)` | `ConcreteAt`, hand-written `Concrete(SubstituteDefaultType(…))`, `Matched`, body-only `Concrete` and instance-only `SignatureType` |
| `ResolveCall(declaredCall, context)` | The substitution inside `CallAt` and `SubstituteDefaultCall` |
| `ExactAcquisition(committed, analyzedType, context)` | `Substituting`-gated normalization in `Place` and `CheckAcquisition` |
| Capability proofs such as `ProveCopy(analyzedType, context)` | `ProveCopy(type, source)` on resolved Types |

No overload omits the context. Code outside default intervals passes the root context, provisionally `InterpretationContext.Root`, so a reader sees locally that no default substitution applies, and the audit can enumerate every such site (U7). The lowered function's own signature uses the root context. The old entry points are removed once their callers migrate, so body-only resolution is unavailable rather than merely discouraged.

Each phase obtains contexts as follows:

- **Builder:** passes its explicit active default context.
- **OwnershipBody consumers:** derive the context from the operation or Place they validate.
- **Binding effect summaries:** receive the context of the owning Place or operation and compose any outer call context separately (§5.5).
- **Emission:** caches the context per operation.

Names are provisional; the distinctions are not.

### 5.2 Declared and analyzed information

| Information | Stage | Interpretation |
| --- | --- | --- |
| Type or acquisition attached to declaration syntax or Binding plans: `BoundMatch` (including Pattern `MatchedType`), `BoundEnumConstruction`, `BoundValueCall`, adaptations | Declared | `Resolve` or `ExactAcquisition` with the operation's context. |
| Place, projection or move-path Type recorded by the builder | Analyzed | Consume directly. Apply only an outer call context that the consumer's contract requires, such as effect composition. |
| Call plan returned by `ResolveCall` | Resolved | Consume directly; never resolve again. |
| The lowered function's own signature | Declared | `Resolve` with the root context. |

API rules:

- Only declared information is passed to a resolver. A query that receives both stages is split. The known case is `Binding.EffectBounds.Type`: it becomes an analyzed-plan query that composes only the outer call context, and a declared-Type query that resolves in the Subject's context and then composes.
- The builder records analyzed Types wherever a later consumer would otherwise reconstruct them from shared syntax. Add an analyzed Type to `OwnershipProjection`, or reuse an existing record with the same stable contract, and use it in loan-part, move-path and retention processing.
- Count new record fields in retained-storage accounting.
- **Escalation:** if U7 finds a stage misclassification that naming and tests did not catch, introduce a zero-cost declared-Type wrapper at the resolver boundary instead of adding further conventions.

### 5.3 Composed substitution

At context creation, compose the default chain once: substitute the context's call through its parent contexts, the composition `SubstituteDefaultCall` already performs, and store the result with the context. `Resolve` then applies exactly two steps in a fixed order: the composed default substitution, then the root stage (instance or Semantics case). This makes invariant 5 structural, removes per-query parent walks, and keeps the stage order in one place.

- **Equivalence evidence:** before the chained form is removed, a checked mode computes both forms and fails on any difference. Enable it through an explicit switch recorded in the evidence, run the full functional suite under it once, and then remove the mode together with the chained form.
- **Coverage:** include same-symbol recursion, nested defaults whose inner call targets the outer callee, length arguments and Origins. If a component does not compose exactly, keep chained application for that component only and record why.
- **Cost:** allocate composed calls once per context, reuse storage across reanalysis, count them in retained-storage accounting and measure them (§7.3).

### 5.4 Proof environment and acquisition

**Rule.** After full default substitution, every free generic parameter of a resolved Type belongs to the analyzed body or its enclosing declarations. Capability proofs about Types resolved in a default context therefore use the analyzed body's environment at the **root default invocation**, the outermost call that started the context chain, recorded with the context. Root-context proofs keep the operation's own source. Universal declaration checking of the default itself keeps the declaration environment (invariant 1).

**Consistency.** Place construction, `ExactAcquisition`, `CheckAcquisition` and every other proof about a resolved Type in one context take their environment from the context, never from syntax position. Otherwise a generic caller with `U is Copy` that passes `U` to an unconstrained `T` would resolve the expectation to Copy while the Place stays `CopyOrMove`.

| Copy proof | Resolution of committed `CopyOrMove` |
| --- | --- |
| Proven | Copy |
| Refuted | Move |
| Unknown, and the Type has free generic parameters of the analyzed body | `CopyOrMove`: the finite conditional plan of §8.10 |
| Unknown, and the Type is closed | Internal failure reported at the operation; never silently kept |
| Error | The existing error path; never converted to success |

- Preserve primitive fast paths and the treatment of Never result markers.
- Fixed acquisitions and explicit transfers are not reselected from the Type's Copy capability.
- `CheckAcquisition` compares the normalized expectation with the actual acquisition and keeps the explicit-transfer rule. It no longer accepts either Copy or Move for `CopyOrMove` under a body-wide flag.
- Do not extend `Substituting`: pair follow, pointers and instance entry use it for other purposes. Audit those uses separately (U7).
- If the root-invocation environment proves insufficient, for example because a call site has conditional-member premises that the body does not, refine the recorded anchor. Never fall back to syntax position or to a declared/analyzed Type-inequality heuristic.

### 5.5 Effect composition

`SubjectDestroyed` and `LeftParts` receive the Subject Place's context (`Place.DefaultContext`), which is provenance recorded at construction, not inferred from the cleanup operation's position. Declared Pattern Types resolve in that context; analyzed Place Types are consumed as analyzed; the outer call context of a transitive summary is composed afterward. Inspect `Binding.VirtualEffects` for the same omission and record any independent limitation separately.

### 5.6 Performance

- Look up each operation's context once in emission and compose each context's substitution once. Avoid per-query allocation and reuse retained capacity.
- Stored Types and composed calls enlarge records. Measure them under fixed conditions; zero allocation after warm-up alone does not show unchanged retained memory or runtime cost.
- `ExactAcquisition` may now prove Copy in library generic bodies where it previously did not; measure warm allocation and binding cost.
- Make no speedup claim without matched before/after measurements.

## 6. Implementation units

Implement in dependency order. Every code unit combines a minimal reproducer, a repair and focused positive and negative tests, and states which §3.2 class each affected family reaches. Formal Unit verification is required before committing; known later failures stay recorded and are not labeled G82 completion. If a unit cannot be tested independently because another repair is essential, combine the dependent units rather than weakening an acceptance check.

### U0 — Establish reproducible baselines

- **Work:** inspect current changes and use an isolated checkout when necessary. Recover the original input and recreate the D1–D6 families with ordinary-body, nongeneric and explicitly supplied-argument controls. Promote essential sources into tracked tests in their repair units, so that ignored evidence is not the only reproduction source.
- **Identity:** build each investigated revision from its own source and configuration, and record the commit, source/DLL identity, toolchain state, commands, source snippets and stage outcomes. Compare current HEAD with `0134ba69^`; use `c0e079db` and its parent only if needed for attribution. Do not reuse an unidentified `temp/vwt` or copied DLL as a clean baseline.
- **Exit:** each family has a source, an observed outcome and a §3.2 classification, or an explicit unreproduced finding. Save all outcomes under `artifacts/verify/g82-baseline-<identity>/`. Choose independent reproducers for later units, and distinguish checking evidence from execution.

### U1 — Specification clarification (documentation only)

- **Work:** finalize the §3.4 wording, update SPEC §7.2.3, and record the intake in `draft/INTEGRATED.md` as 一部取り込み, integrating and freezing only §3.4 of this proposal. Alternatively, record a rejection with its reason.
- **Checks:** diff and reference review only.
- **Exit:** the clarification is integrated or rejected with a recorded reason. This unit is independent of the code units and may land at any point before U8.

### U2 — Interpretation context, behavior-preserving

- **Targets:** `OwnershipBody.Defaults`, `OwnershipModel`, `OwnershipAnalysis.Instances`, the emission Type queries and the effect-summary Type query.
- **Work:** introduce the context value, the resolver family with an explicit root context, and composed substitution (§5.1, §5.3). Re-express every existing composition through the family without changing which context any caller uses: today's body-only callers pass the root context. Run the §5.3 equivalence mode, then remove the chained form and the context-free entry points.
- **Checks:** a Session with no functional or allocation-regression difference; equivalence evidence over the full functional suite; fixed-condition measurements of the new records.
- **Exit:** every resolution site passes an explicit context. The list of root-context sites is recorded; it is the work list for U3–U7.

### U3 — Acquisition and proof environment (D1, D2)

- **Targets:** `OwnershipAnalysis.Place`, `OwnershipAnalysis.Enums.CheckAcquisition`, pattern acquisition consumers and the proof anchor recorded with each context.
- **Work:** implement §5.4. Place construction and acquisition validation use the context's proof environment and the shared `ExactAcquisition`. Retain explicit transfer rules and invalid-plan rejection. Do not mutate Binding plans or bypass declaration checks.
- **Checks:**
  - Types: Copy scalar, string and destructor owner, reference-bearing payload, unknown generic Type, Semantics cases, and explicit `@move` of a Copy value.
  - Callers: concrete callers, plus generic callers with and without `U is Copy`, including a nested default whose outer call is generic.
  - Forms: enum construction separately from pattern binding, empty and populated cases, and a value-producing path that U0 shows is independent of unfixed Callable and lowering paths.
- **Exit:** D1 and D2 reach Accepted on their isolated paths, with plan assertions, invalid counterparts and native O0/O2 coverage, including exactly-once owner destruction. The full original input stays pending while D3 or D5 blocks it.

### U4 — Projection Types (D4)

- **Targets:** `OwnershipProjection`, `OwnershipAnalysis.Elements`, `OwnershipBody.LocalRegions`, `OwnershipBody.MovePaths` and the retention consumers in `OwnershipBody.Borrows`.
- **Work:** record the analyzed projection Type at construction and consume it in loan-part, move-path and retention processing (§5.2). Confirm its meaning for borrowed projections, nested tuples and fields, and static array elements before sharing it.
- **Checks:** D4 and a genuine competing-Loan counterpart; whole and partial Move, remaining initialized parts, borrowed payloads and reverse cleanup. Use independent creation paths while D3 remains unresolved. Include retained-capacity and storage-accounting regressions.
- **Exit:** valid partial transfers are Accepted, invalid conflicts remain Permitted rejections, and O0/O2 confirms that moved parts are not destroyed twice and residual owners are not lost.

### U5 — Residual destruction effects (D5)

- **Targets:** `Binding.EffectBounds` (`Cleanups`, `SubjectDestroyed`, `LeftParts`, its Type queries and case-destruction collection), and `Binding.VirtualEffects` for inspection.
- **Work:** split the Type query by stage and carry the Subject's context (§5.2, §5.5).
- **Checks:**
  - Patterns: `.Some(_)`, `.Some(let value)`, `.None`, nested Case and Tuple patterns, guard paths and partial acquisition.
  - Effects: `i32` and borrowed payloads add no owning destruction effect, and an output-producing owner destructor causes the expected effect-bound rejection. Isolate the residual effect from unrelated inputs and result cleanup, for example by borrowing unrelated sample inputs.
  - Contexts: body cases, and nested and forwarded calls.
- **Exit:** D5's false rejection disappears without suppressing real effects, double-counting acquired parts or omitting residual owners.

### U6 — Callable validation and lowering (D3)

- **Targets:** `OwnershipBody.Comparisons.CallableReceiverType` and `BodyLowering.Closures.LowerValueCall`.
- **Work:** resolve receiver expectations in the context of the Loan's read operation. Resolve syntax-attached signature, result and argument Types in the value-call context. Validate both sides of each comparison at the same stage; generation continues to consume the ownership plan.
- **Checks:** D3 through concrete callers, supported generic callers and nested defaults; shared and exclusive Callable receivers and consuming forms where supported; independent and borrowed results. Assert Loan lifetime and identity as well as Type agreement. If another implementation boundary blocks a form, keep its source and classify it as a Recorded gap.
- **Exit:** positive and negative Callable cases pass, native output and cleanup are correct, and the executable original G82 input reaches O0/O2 once its remaining required units are complete. Do not promise full G82 execution here if U7 still supplies a prerequisite.

### U7 — Remaining consumers and context audit (D6)

- **Targets:** the root-context sites recorded in U2: remaining `BodyLowering` call/reference, comparison, array, dictionary, storage, raw/pointer, element, sequence, formatting/interpolation, value and graph paths; and pair-layer `Substituting` uses.
- **Work:** for each root-context site that is reachable from a default interval and reads declared information, switch it to its actual context or record why the root context is correct. Keep resolved call plans and function signatures at their stage. Apply the §5.2 escalation if a stage misclassification appears. Keep a short audit record of changed sites, justified root sites, unsupported source forms and unresolved findings.
- **Checks:** D6; representative default and body controls for each changed family, generated where possible (§7.1); nested and same-symbol recursive substitutions to detect double application. Invalid reference, Origin and Loan counterparts remain Permitted rejections. Split large groups into coherent verified units.
- **Exit:** no unclassified root-context site remains on the original G82 route or on a verified D1–D6 route. An audit label alone is not execution evidence. Wider unrelated unsupported forms receive a named follow-up and do not broaden the completion claim.

### U8 — Decision record, documentation and completion

- **Work:** record the model decision of §8 in SETTLED. Update CODEMAP for the interpretation context entry points and phase responsibilities. Update STATUS only for verified support-boundary changes, keep PLAN concise with any remaining prerequisite, and add a short PLAN_HISTORY entry. Prepare all documentation before final verification.
- **Checks:** the generated replica matrix (§7.1), the regression matrix (§7.2), related allocation and storage regressions, and the fixed-condition measurements (§7.3). Verify the original checking input and its executable counterpart on the final source. Run one final Session with explicit native fixture and milestone selection.
- **Exit:** every completion condition in §9 has evidence, or G82 remains open with the remaining prerequisite identified. This unit does not certify general generic-default support beyond the verified matrix.

## 7. Verification design

### 7.1 Generated context replicas

A test helper, provisionally the `GenericDefaultContextTest` generator, places one snippet into each context shape mechanically, so the replicas stay equivalent by construction rather than by hand maintenance.

| Shape | Placement |
| --- | --- |
| Body | The snippet in an ordinary generic function body, with its preceding inputs as ordinary parameters |
| Default | The snippet as an omitted default of a generic function invoked by a concrete caller |
| Nested default | The snippet's default evaluation itself calls a function whose generic default is omitted |
| Forwarded | A generic caller, with and without `U is Copy`, forwarding its parameter |
| Semantics case | A pair-binder caller, where supported |
| Repetition | One default used with two Types, in both call orders |

- **Type arguments:** `i32`, `string`, a destructor-recording owner, a borrowed payload, and an unresolved generic Type in forwarding.
- **Snippets:** the D1–D6 families and their controls.
- **Expected outcomes:** derived from §3.2. When the Body shape and the universal default check accept a snippet, every Default shape is Accepted with the same result, stdout, exit status and destructor count and order, or it is a Recorded gap.
- **Excluded differences:** intentional §7.2.3 differences, such as prepared-slot restrictions and declaration-scope lookup, are excluded from generation and covered by separate rejection tests.
- **Coverage record:** the generator writes executed and skipped cells, each skipped cell with its reason, to `artifacts/verify/`. Skipped cells are reported as remaining uncertainty.
- **Execution:** every cell runs through checking. A recorded representative subset runs natively at O0 and O2.

### 7.2 Regression matrix

Use representative combinations that exercise distinct paths, not a Cartesian product. Class names such as `GenericDefaultContextTest` and `DefaultReplicaParityTest` are proposed, not existing selectors.

| Dimension | Required coverage |
| --- | --- |
| Type/evidence | Copy scalar; Non-Copy string and destructor owner; direct and stored borrow; unresolved generic capability; caller Constraints with and without Copy evidence; Semantics case. |
| Context | Nongeneric caller with only a default substitution; body instance or case; nested defaults; supported generic forwarding; recursive evaluator; repeated expansion of the same syntax. |
| Pattern/storage | Empty and populated enum; wildcard and binding; explicit Move and borrow; nested Case and Tuple; guard failure and exit; partial Move; construction abandoned before completion. |
| Evaluation | Explicit-argument order; declaration-order defaults; exactly-once evaluation; an explicitly supplied default is not evaluated; abandoned pending values are destroyed in reverse order. |
| Invalid behavior | Move or change of a protected prepared slot; escaping a new prepared-input Borrow; conflicting Loan; real destructor effect; an invalid universal default despite only concrete or explicit-argument calls. |
| Isolation | Sibling and nested contexts do not leak; shared Binding plans remain unchanged by one concrete expansion. |
| Lifecycle | Reanalysis, edit-and-reBind, and invalid-then-repaired input, limited to the state this repair adds or changes: context records, composed substitutions, proof anchors, analyzed projection Types and caches, including their reset in `OwnershipBody.Reset`. Test serialization and reload only if one of these records is persisted. |
| Execution | O0/O2 result, stdout, stderr and exit status; destructor count and order, partial transfer and referent survival; actual invocation of the original reproducer's worker. |

Reuse focused tests from `GenericDefaultTest`, `NestedDefaultTest`, `RecursiveDefaultTest`, `DefaultAccessTest`, `SemanticsDefaultTest`, `GenericStorageEmissionTest`, `EnumOwnershipTest`, `PatternBindingTest`, `MatchOwnershipTest`, `GenericCleanupCaseTest`, `GenericAdaptationEffectTest`, `CallableEffectBoundTest`, `VirtualEffectObligationTest`, `FunctionDefaultTest`, `CallablePlaceResultTest`, `InputDependentValueCallTest`, `ConcreteClosureTest` and the affected local-region and Loan suites. Select actual classes and methods after checking their current coverage.

### 7.3 Verification, performance and evidence

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). Documentation-only units, including this revision and U1, require diff and reference review, not builds or tests. Code units require focused Unit verification and one Session at the end of each implementation session; the final Session may also satisfy the final Unit when it covers identical inputs and all required checks.

- **Feedback vs evidence:** use incremental test-project builds and selected methods for intermediate feedback. Only formal Verify's non-incremental Release warning-as-error build establishes completion evidence.
- **Fixture and milestone selection:** select related native `.ll` fixtures and milestones explicitly. Use a `GenericDefaultContext*.ll` prefix consistently for new fixtures, and include changed existing fixture families. P20 and P22 cover existing generic-default and monomorphization paths; add others only when affected.
- **Allocation and reuse:** preserve functional and allocation/reuse coverage, fixed warm-up and repetition conditions, zero-allocation assertions, retained-capacity checks and existing isolation. Never turn an uncertain proof into acceptance to satisfy a performance target.
- **Measurements:** measure `--local-regions`, `--callable-plans` and, for shared acquisition or effect changes, `--adaptation-plans` under the conditions in [CallablePlans](../../src/Benchmark/CallablePlans.md) and [Adaptations](../../src/Benchmark/Adaptations.md). Add a bounded G82 workload if those inputs do not exercise default contexts; include nesting depth, sibling expansions and the new records in cost review.
- **Diagnostics:** keep ordinary diagnostics: factual cause, code and category, and the relevant source location. This plan does not activate the full diagnostic-development workflow. Review any required snapshot differences rather than accepting them wholesale.
- **Exclusions:** do not run NativeAOT, and never edit verification inputs while a build or verification is running.

Example final selection, to be finalized after fixture creation:

```powershell
./scripts/verify.ps1 -Mode Session -Fixtures 'GenericDefaultContext*.ll' -Milestone 20,22 -Name g82-final
```

This example is not the complete selection if a unit changes other fixture families. A Session label does not imply native, milestone or measurement coverage that was not selected.

Evidence handling:

- Store successes, failures, generator coverage records and audit records under `artifacts/verify/`, measurements under `artifacts/benchmarks/`, and disposable work under `temp/`. Preserve exact source and configuration identities, selected checks and remaining uncertainty.
- Commit only each unit's files after its required checks pass, associate formal evidence with the commit using `verify-commit.ps1`, and push the current branch to `origin` without force.
- Back up ignored evidence separately.
- Do not commit failing tests alone as a completed repair unit.

## 8. Model decision: inline replicas and universal checking

The current model computes a default's meaning twice: universally at declaration time, and again as an inline replica at each call (G72). Every G82 family is a disagreement between the two computations.

| Option | Description | Assessment |
| --- | --- | --- |
| A — keep inline replicas | One interpretation context per operation; agreement with the universal check is enforced by the oracle (§3) and the generated replicas (§7.1). | Adopted. A bounded change that preserves G72's evaluation, prepared-slot and cleanup model. |
| B — instance-evaluated defaults | Evaluate generic defaults as instantiated plans or instance evaluators, which removes the per-operation default stage. | Deferred. It removes the third stage at the cost of rebuilding G72's execution model. |

Reconsider B, as a separate design decision presented to the user, when any of these measurable triggers holds:

1. A consumer cannot obtain its context from an operation, a Place or a plan record, and would have to infer it from syntax or cleanup position.
2. One context needs more than one proof environment, so §5.4 cannot be expressed per context.
3. The generator finds a Defect whose repair requires re-running analysis semantics rather than selecting a context.
4. A fixed-condition measurement regresses beyond run-to-run variation, and bounded context reuse cannot remove the regression.

Never replace G72's model inside a repair unit. At G82 closure, U8 records the outcome in SETTLED: the dual-computation problem with a short example, Option B as the proposed fix, the reason it was not applied, and these reopening triggers. Later reviews then do not re-raise it without new evidence.

## 9. Completion conditions

G82 can be closed only when all applicable conditions hold:

1. **Original input:** the original checking input is tracked in a regression and no longer faults, and an executable counterpart runs at O0/O2 with the intended result.
2. **Classification:** D1–D6 and every later finding are classified by §3.2 with concrete evidence. Every Defect is repaired; every Recorded gap has an owner and lies off the original G82 route.
3. **Explicit context:** every Type, call, acquisition and capability resolution names its context, no context-free resolver remains, and every root-context site is audited.
4. **Single substitution:** composed-substitution equivalence is evidenced, and nested, repeated, forwarded and recursive paths in the verified scope show no missing or duplicate substitution.
5. **Proof environment:** Place construction and acquisition validation share the proof environment; generic callers with and without Copy evidence are verified; a closed Unknown is never silently kept.
6. **Cleanup:** residual destruction is correct for Copy, owner and borrow payloads, including partial transfers, guards and abandoned evaluation.
7. **Parity:** every executed cell of the generated replica matrix agrees with §3.2, and skipped cells are listed as remaining uncertainty.
8. **Specified protections:** declaration checking and prepared-slot protections keep their specified behavior; invalid acquisition and Loan plans remain rejected.
9. **Verification:** focused units and the final Session pass with the required native, milestone, allocation and storage coverage, and measurements are tied to the final inputs. All failures remain retained.
10. **Records:** the §3.4 clarification is integrated or rejected with a recorded reason; the §8 decision is in SETTLED; support boundaries and navigation match the verified result. History claims no unproven first-bad commit, no full generic support and no unmeasured speedup.

## 10. Documentation and change boundaries

At implementation time:

- update `docs/dev/CODEMAP.md` for the interpretation context entry points and changed phase responsibilities;
- update `docs/STATUS.md` only for verified support-boundary changes;
- keep `docs/dev/PLAN.md` concise with any remaining prerequisite;
- record short session outcomes in `docs/dev/PLAN_HISTORY.md`, keeping detailed evidence in commits and artifacts.

The only proposed SPEC change is the §3.4 clarification, made through U1 with its `draft/INTEGRATED.md` record. No STYLE or LIBRARY change is needed. If a real semantic contradiction emerges, state the rationale and principle alignment, update the affected formal chapters, examples, tests and milestones together, and record the intake. Do not narrow required language behavior to match an incomplete implementation.

This proposal stays in `draft/Proposals` while unresolved. Preparing or revising it is not a specification intake, so it does not update the integration register. U1's partial intake freezes only §3.4. Later closure follows the repository's disposition, register and folder rules, and frozen text is never rewritten.

## 11. Conditions for revising the plan

| Condition | Response |
| --- | --- |
| U0 does not reproduce a family. | Update its classification; make no speculative edit solely to match the supplied report. |
| A Binding declaration plan is wrong before any default expansion. | Create a minimal reproducer and repair that responsibility; do not compensate in later phases. |
| The root-invocation proof environment is insufficient. | Refine the recorded anchor (§5.4); never fall back to syntax position or a Type-inequality heuristic. |
| Cleanup cannot identify the Subject's original context. | Carry that provenance explicitly in the owning plan rather than guessing from syntax or cleanup position. |
| Composed substitution is not equivalent for some component. | Keep chained application for that component only and record why (§5.3). |
| U7 finds a stage misclassification that naming and tests missed. | Introduce the declared-Type wrapper (§5.2). |
| A path requires an independent forwarding or Origin repair. | Decide whether it blocks G82 completion and track the dependency explicitly. |
| A trigger in §8 holds. | Stop and present the A/B comparison to the user as a separate design decision. |
| Performance regresses. | Prefer bounded context reuse and recorded plan facts; never relax semantics, tests or allocation assertions. |
| Concurrent work changes the same inputs. | Rebase the working assumptions and rerun affected verification on the actual final source. Historical warnings about a particular worktree are not current coordination evidence. |
