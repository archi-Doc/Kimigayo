# G82 Generic Default Context Repair Plan

Date: 2026-10-08. Status: proposed implementation plan; implementation and execution verification are pending.

## 1. Purpose and authority

Repair G82 by making every consumer of declaration-derived information use the context of the operation being analyzed. Combine G82o's cross-phase context design with G82a's lifecycle, ownership and context-isolation checks.

This is a non-normative implementation proposal. [SPEC](../../docs/SPEC.md) and its chapters remain authoritative. No language-rule change is proposed. Creating this document does not implement the repair, establish new support, close G82 or integrate a specification proposal.

The required behavior already follows from default evaluation and universal declaration checking (§7.2.3), generic/Semantics checking (§8.10), acquisition (§3.5 and §15.1), pattern ownership (§15.1.6), destruction (§16.2), effect bounds (§8.4.10) and concrete generation (§21.3.1). Consult [SETTLED](../../docs/SETTLED.md), [CODEMAP](../../docs/dev/CODEMAP.md) and [verification scope](../../docs/dev/VERIFICATION.md#verification-scope) before implementation.

The design supports the Kimigayo Principles:

- One canonical interpretation of declaration-derived information, shared across phases.
- Local reasoning through explicit operation/context identity and published constraint evidence.
- Explicit acquisition and cleanup responsibility; neither exceptions nor uncertain proofs are silently accepted.
- Checks and measurements tied to exact source/configuration, with failures and uncertainty retained.

## 2. Evidence and baseline

### 2.1 Sources and confidence

The user supplied `G82a.md` and `G82o.md` under `C:/App/Downloads/Fix/`. They are comparison inputs, not authoritative compiler contracts or independently verified execution evidence. This plan is self-contained and does not require their local paths at implementation time.

G82a identifies acquisition/remainder-context mismatches and explicitly reports static investigation only. G82o reports six failure families from scratch probes and proposes a broader context API repair. Its probe sources and exact build inputs must be recovered or recreated in U0 before treating those outcomes as reproduced facts.

Planning snapshot: `35bbc6ba4a501ec7942e6ac3452313d4c4562f33`. The worktree was clean when this plan was prepared. G84 associated-Type inference is committed in that snapshot; the earlier warning about uncommitted G84 edits is historical. Recheck the current tree and active work before implementation.

The current source supports the structural diagnosis:

| Location | Observed mismatch |
| --- | --- |
| `OwnershipAnalysis.Instances.Concrete` and `OwnershipAnalysis.Place` | Type resolution includes the active default context, but committed `CopyOrMove` normalization is gated by `Substituting`, which only recognizes instance/case analysis. |
| `OwnershipAnalysis.Enums.CheckAcquisition` | The same body-wide flag controls whether an unresolved expected acquisition may match Copy or Move. |
| `OwnershipBody.Comparisons.CallableReceiverType` | An analyzed Place Type is compared with a declaration-derived receiver Type resolved without the operation's default context. |
| `OwnershipBody.LocalRegions` and `OwnershipBody.MovePaths` | Projection Types are reconstructed from syntax with body-wide `Concrete`. |
| `Binding.EffectBounds.LeftParts` and `EffectSummary.Type` | Pattern Types and analyzed plan Types share a query that does not carry the relevant default context. |
| `BodyLowering.Closures.LowerValueCall` | Declaration-derived receiver, signature, result and parameter Types are passed to instance-only `SignatureType`. |

Existing `OwnershipBody.ConcreteAt`, default-context parent chains, Place `DefaultContext` and match lowering already provide useful infrastructure. Extend these responsibilities instead of adding an independent substitution engine.

### 2.2 Failure families to reproduce

| ID | Input family | Prior report | Required repaired behavior |
| --- | --- | --- | --- |
| D1 | Omitted generic default constructs `Option<T>.Some(factory())` | `CheckFaulted_Kd`: committed payload acquisition disagrees with source Place | Consistent acquisition for Copy and Non-Copy substitutions; no internal fault. |
| D2 | Default constructs `.None` and matches `.Some(let value)` | Ownership succeeds; composite-acquisition generation fails | Pattern plan and analyzed acquisition agree, including empty-case execution. |
| D3 | Default invokes a generic `F: Callable` value | Callable Loan validation fails; a later lowering mismatch is predicted | Receiver Loan, signature, arguments and result share the operation context. |
| D4 | Default partially moves `(Option<T>, string)` | Spurious Loan conflict; ordinary-body counterpart reportedly succeeds | Correct projection Types, retained Loans, remaining initialization and cleanup. |
| D5 | `confined` implementation evaluates a generic default with `.Some(_)` | Spurious unknown/destruction effect for `T = i32` | Copy/borrow residuals add no owning destruction effect; effectful owner residuals are rejected. |
| D6 | Default invokes a generic requirement such as `a.equals(a)` with `T = string` | Reference-argument Loan/Origin validation fails | Declaration-derived and resolved-call Types are interpreted at their correct stages. |

These are investigation families, not six independently confirmed regressions in the planning snapshot. Do not infer runtime corruption from a path that currently stops at checking or generation.

The saved original input is `artifacts/verify/c1-default-pattern.kimi`: `Helpers.evaluate<T, F>` receives `sample: T` and `factory: ref/F`; its omitted `marker` default constructs and moves/matches `Option<T>`, returning 1 or 0. `Worker.run`, implementing a `confined` Contract requirement, invokes it with an `i32` factory. There is no object adaptation. Its top-level `()` does not exercise `Worker.run` at runtime: retain this checking reproducer and add an executable counterpart that calls `run` and asserts its result.

Existing `GenericDefaultTest` does not consist entirely of `T is Copy` examples; Function Item examples use `T is Owned`. The relevant gap is the interaction between default contexts, unresolved acquisition, generic Callable invocation and residual ownership, not the absence of all Non-Copy-capable declarations.

### 2.3 Historical uncertainty

`c0e079db` introduced generic-default context support; `0134ba69` recorded G82 during C1. The supplied probes used a build with other work mixed in, and the older comparison build rejected the relevant forms as Unsupported. Neither source-line age nor that rejection establishes the first failing commit or excludes a C1 contribution.

U0 compares clean current and pre-C1 sources. If attribution remains useful, compare the introduction boundary separately. Record supported, unsupported and faulting outcomes distinctly. Historical attribution is not a prerequisite for a correct repair; unresolved attribution must remain explicit.

## 3. Scope and invariants

The repair covers declaration-derived Types and acquisition information used by default expansion, ownership solving, cleanup effects and generation. It includes nested defaults, body instances/Semantics cases, generic forwarding where otherwise supported, and reuse of shared syntax across calls.

Preserve these invariants:

1. Check every default universally in its declaration environment, even if all calls supply that parameter. A successful concrete call cannot legalize an invalid generic declaration.
2. Acquire explicit arguments first; evaluate omitted defaults once in declaration order. Preserve prepared-slot protection, abandonment cleanup and the existing prohibition on escaping new Loans of prepared arguments.
3. Binding's shared syntax and plans remain declaration-level information. Never overwrite them with one call's concrete Types or acquisition results.
4. `CopyOrMove` is resolved from the analyzed Type and valid proof environment. Unknown remains unresolved; Error retains the existing failure path. Explicit `@move` retains its transfer semantics for Copy Types.
5. Keep acquisition, Loan, Origin and lowering consistency checks. Do not remove a check or accept both Copy and Move merely to suppress a mismatch.
6. Wildcards leave responsibility with the Subject; acquired parts and residual parts have distinct cleanup. Guard paths retain their pre-transfer responsibility. A reference payload does not own its referent.
7. Apply each substitution stage once, in the established order. A resolved plan must not be reinterpreted as declaration syntax.
8. Preserve contextual identity across reanalysis, edits, reload, sibling calls and recursive evaluators. Context IDs and operation IDs belong to their owning body and source snapshot.

General forwarding/Origin limitations that persist after context repair remain separately tracked implementation gaps. In particular, G82o's reported `InstantiateForwardedCall` null result is a candidate boundary to reproduce, not an established out-of-scope fact. If it is required for the original G82 path, treat it as a prerequisite rather than closing G82 around it.

## 4. Context and plan design

### 4.1 Distinguish information by its interpretation stage

| Information | Required interpretation |
| --- | --- |
| Type or acquisition attached to declaration syntax, including `BoundMatch`, `BoundEnumConstruction`, `BoundValueCall` and adaptations | Resolve using the current operation's default chain and owning body's instance or Semantics case; use the corresponding valid proof environment for unresolved capabilities. |
| Place, projection or move-path Type recorded after analysis | Consume as the analyzed plan Type. Apply only an additional outer context explicitly required by the consumer's contract. |
| Call plan returned by `CallAt` / default-call substitution | Respect the substitutions already performed; apply only remaining body/caller stages established by that API's contract. |
| The lowered function's own signature | Use its signature/instance context. Do not accidentally apply an unrelated inlined default context. |

A declaration read follows the existing inner-to-outer default parent chain, then the body instance or Semantics case. Effect summary composition may additionally need an outer call context; classify its input stage before composing it. Already-analyzed plan Types must not be fed through the default chain again.

### 4.2 API responsibilities

- Give declaration reads an operation-aware entry point based on `ConcreteAt`. During construction, the builder may use its explicit active default context. Where only a Place/context is available, provide a clearly named context-aware equivalent instead of inventing an unrelated operation ID.
- Generalize emission's `Matched` handling into a shared declaration-Type query, provisionally `Declared(body, type, operation)`. Reuse it for syntax-attached plans. Keep body-signature and resolved-plan queries distinct and narrowly named.
- Make body-only `Concrete` private where possible, or restrict/rename it so its callers declare why no default context applies. Audit callers before reducing visibility; some signature uses are legitimate.
- Record an analyzed Type on `OwnershipProjection`, or reuse an existing analyzed record if it provides the same stable contract. Loan-part and move-path consumers should not independently reconstruct that Type from shared syntax.
- Cache operation-context lookup only within the owning body/snapshot. Prefer existing index/storage reuse and avoid per-query allocation. Include any new record fields in retained-storage accounting.

Names are provisional; the required distinction is not. Avoid a broad replacement of every `Concrete` or `SignatureType` call. Classify each caller by source of information, completed substitution stages and available operation/context identity.

### 4.3 Acquisition and proof context

Introduce one helper, provisionally `ExactAcquisition`, for resolving committed `CopyOrMove`. Share its decision with Place construction and acquisition validation. Preserve primitive fast paths and existing treatment of Never result markers. Fixed acquisitions and explicit transfers are not reselected merely from the Type's Copy capability.

| Copy proof | Resolution of `CopyOrMove` |
| --- | --- |
| Proven | Copy |
| Refuted | Move |
| Unknown | CopyOrMove |
| Error | Preserve the normal unsupported/error path; never convert it to success. |

Proof is a function of Type **and valid assumptions**. `ProveCopy(type, source)` uses the source's lexical Constraint scope, which may differ from the scope of a substituted caller parameter. U1 must establish the correct proof behavior for concrete, universal, forwarded and Semantics-case analysis. Do not assume that lost proof precision cannot change acceptance. Preserve universal declaration checking while resolving instance-specific evidence at the appropriate stage.

Do not extend `Substituting` globally: pair follow, pointers and instance entry also use it. Audit those uses independently. If universal behavior changes, compare Binding's committed acquisition and the proof environment; neither a flag nor mere declared/analyzed Type inequality is a sufficient repair criterion.

## 5. Implementation units

Implement in dependency order. Every code unit combines a minimal reproducer, a repair and focused positive/negative tests. Formal Unit verification is required before committing; known later failures remain recorded and must not be mislabeled as G82 completion. If a unit cannot be tested independently because another repair is essential, combine the dependent units rather than weakening its acceptance check.

### U0 — Establish reproducible baselines

**Work:** inspect current changes and use an isolated checkout when necessary. Recover the original input and recreate the D1–D6 families with ordinary-body, nongeneric and explicitly supplied-argument controls. Promote essential sources into tracked tests in their repair units so ignored evidence is not the only reproduction source.

Build each investigated revision from its own source/configuration; record the commit, source/DLL identity, toolchain state, commands, source snippets and stage outcomes. Compare current HEAD with `0134ba69^`; use `c0e079db` and its parent only if needed for attribution. Do not reuse an unidentified `temp/vwt` or copied DLL as a clean baseline.

**Exit:** each family has a source and an observed failure stage, passes already, or has an explicit unreproduced/unsupported finding. Save all outcomes under `artifacts/verify/g82-baseline-<identity>/`. Choose independent reproducers for later units; distinguish checking evidence from actual execution.

### U1 — Normalize acquisition with the correct evidence

**Targets:** `OwnershipAnalysis.Place`, `OwnershipAnalysis.Enums.CheckAcquisition`, related default/instance proof helpers and pattern acquisition consumers.

**Work:** implement the shared normalization contract from §4.3. Check normalized expectations against actual acquisition, retaining explicit transfer rules and invalid-plan rejection. Establish how caller evidence is represented before adding a new context mechanism. Do not mutate Binding plans or bypass declaration checks.

**Checks:** Copy scalar, Non-Copy string/owner, reference-bearing payload, unknown generic Type, Semantics cases and explicit `@move` of a Copy value. Separate enum construction from pattern binding. Verify both empty and populated cases, using a value-producing path that U0 has shown does not depend on an unfixed Callable/lowering path.

**Exit:** D1/D2's acquisition mismatch is removed on the isolated paths; plan assertions and invalid counterparts pass. Native O0/O2 covers those paths, including exactly-once owner destruction. Keep the full original input pending if D3/D5 still blocks it.

### U2 — Preserve projection Types through ownership solving

**Targets:** `OwnershipProjection`, `OwnershipAnalysis.Elements`, `OwnershipBody.LocalRegions`, `OwnershipBody.MovePaths` and the corresponding retention consumers in `OwnershipBody.Borrows`.

**Work:** record/reuse the analyzed projection Type at construction. Make loan-part, move-path and retention processing consume that same Type. Confirm its meaning for borrowed projections, nested tuples/fields and static array elements before sharing it.

**Checks:** D4 and a genuine competing-Loan counterpart; whole/partial Move, remaining initialized parts, borrowed payloads and reverse cleanup. Use independent creation paths where D3 remains unresolved. Include retained-capacity/storage-accounting regressions.

**Exit:** valid partial transfers succeed, invalid conflicts still fail, and O0/O2 confirms that moved parts are not destroyed twice and residual owners are not lost.

### U3 — Compose residual destruction effects explicitly

**Targets:** `Binding.EffectBounds` (`Cleanups`, `SubjectDestroyed`, `LeftParts`, Type queries and case destruction collection); inspect `Binding.VirtualEffects` for the same context omission.

**Work:** carry the Subject's default context or an operation proven to identify that context into remainder analysis. Do not assume a later cleanup operation necessarily lies inside the original default interval. Separate declaration-Type resolution from analyzed plan-Type consumption and outer effect-context composition.

**Checks:** `.Some(_)`, `.Some(let value)`, `.None`, nested Case/Tuple patterns, guard paths and partial acquisition. `i32` and borrowed payloads add no owning destruction effect; an output-producing owner destructor causes the expected effect-bound rejection. Isolate the residual effect from unrelated inputs and result cleanup, for example by borrowing unrelated sample inputs. Cover body cases and nested/forwarded calls.

**Exit:** D5's false rejection disappears without suppressing real effects, double-counting acquired parts or omitting residual owners. Record any independent virtual-effect limitation separately.

### U4 — Align Callable validation and lowering

**Targets:** `OwnershipBody.Comparisons.CallableReceiverType`, `BodyLowering.Closures.LowerValueCall` and the shared declaration-Type query.

**Work:** resolve receiver expectations at the Loan's read operation. Resolve syntax-attached signature, result and argument Types in the value-call context. Validate both sides of comparisons at the same interpretation stage; generation continues to consume the ownership plan.

**Checks:** D3 through concrete callers, supported generic callers and nested defaults; shared/exclusive Callable receivers and consuming forms where supported; independent and borrowed results. Assert Loan lifetime/identity as well as Type agreement. If another implementation boundary blocks a form, retain its source and distinguish it from a context failure.

**Exit:** positive/negative Callable cases pass, native output/cleanup is correct and the executable original G82 input reaches O0/O2 successfully once its remaining required units are complete. Do not promise full G82 execution at U4 if U5 still supplies a prerequisite.

### U5 — Audit remaining declaration-Type consumers

**Targets:** remaining `BodyLowering` call/reference, comparison, array, dictionary, storage, raw/pointer, element, sequence, formatting/interpolation, value and graph paths; remaining body-only context queries and pair-layer `Substituting` uses.

**Work:** classify every candidate consumer using §4.1. Migrate only declaration-derived reads that require operation context. Preserve function signatures and resolved call plans at their proper stage. Keep a short audit record of changed callers, justified unchanged callers, unsupported source forms and unresolved findings.

**Checks:** D6; representative default/body controls for each changed family; nested and same-symbol recursive substitutions to detect double application. Invalid reference/Origin/Loan counterparts must remain invalid. Split large groups into coherent verified units.

**Exit:** no unclassified context omission remains on the original G82 route or a verified D1–D6 route. An audit label alone is not execution evidence. Wider unrelated unsupported forms receive a named follow-up and do not silently broaden the completion claim.

### U6 — Enforce API boundaries, lifecycle correctness and completion

**Targets:** query visibility/naming, reusable plan state, tracked regression tests and implementation documentation.

**Work:** complete the caller audit before restricting body-only APIs. Add context-isolation and lifecycle regressions; remove only redundant interpretation paths whose consumers have migrated. Prepare support/documentation changes before final verification.

**Checks:** the matrix in §6, related allocation/storage regressions and fixed-condition measurements in §7. Verify the original CLI checking input and executable counterpart on the final source. Run one final Session, with explicit native fixture and milestone selection.

**Exit:** every completion condition in §9 has evidence, or G82 remains open with the remaining prerequisite identified. This unit does not certify general generic-default support beyond the verified matrix.

## 6. Regression matrix

Use representative combinations that exercise distinct paths rather than a Cartesian product. New class names such as `GenericDefaultContextTest` and `DefaultReplicaParityTest` are proposed, not existing selectors.

| Dimension | Required coverage |
| --- | --- |
| Type/evidence | Copy scalar; Non-Copy string and destructor owner; direct/stored borrow; unresolved generic capability; valid caller constraints; Semantics case. |
| Context | Nongeneric caller with only a default substitution; body instance/case; nested defaults; supported generic forwarding; recursive evaluator; repeated expansion of the same syntax. |
| Pattern/storage | Empty/populated enum; wildcard/binding; explicit Move/borrow; nested Case/Tuple; guard failure/exit; partial Move; construction abandoned before completion. |
| Evaluation | Explicit-argument order, declaration-order defaults, exactly-once evaluation, explicitly supplied default not evaluated, abandoned pending values destroyed in reverse order. |
| Invalid behavior | Move/change of a protected prepared slot; escaping a new prepared-input Borrow; conflicting Loan; real destructor effect; invalid universal default despite only concrete or explicit-argument calls. |
| Isolation | Same default used with different Types in both call orders; sibling/nested contexts do not leak; shared Binding plans remain unchanged by one concrete expansion. |
| Lifecycle | Reanalysis of one Compilation; Type/constraint edits followed by reBind; serialization/reload; invalid edit then repaired input; old contexts, acquisitions, Loans and cleanup plans are not retained. |
| Execution | O0/O2 result, stdout, stderr and exit status; destructor count/order, partial transfer and referent survival; actual invocation of the original reproducer's worker. |

For default/body parity, compare only semantically equivalent programs. A default has prepared-slot restrictions and declaration-scope lookup that an ordinary body does not; preserve those intentional differences with separate rejection tests. Equivalent valid programs should agree in acceptance and runtime behavior, not necessarily source locations or internal operation numbering.

Reuse focused tests from `GenericDefaultTest`, `NestedDefaultTest`, `RecursiveDefaultTest`, `DefaultAccessTest`, `SemanticsDefaultTest`, `GenericStorageEmissionTest`, `EnumOwnershipTest`, `PatternBindingTest`, `MatchOwnershipTest`, `GenericCleanupCaseTest`, `GenericAdaptationEffectTest`, `CallableEffectBoundTest`, `VirtualEffectObligationTest`, `FunctionDefaultTest`, `CallablePlaceResultTest`, `InputDependentValueCallTest`, `ConcreteClosureTest` and the affected local-region/Loan suites. Select actual classes/methods after checking their current coverage.

## 7. Verification, performance and evidence

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). This document-only planning unit requires diff/reference review, not builds or tests. The implementation units require focused Unit verification and one Session at each implementation session's end; the final Session may also satisfy the final Unit when it covers identical inputs and all required checks.

- Use incremental test-project builds and selected methods for intermediate feedback. Formal Verify's non-incremental Release warning-as-error build establishes completion evidence.
- Select related native `.ll` fixtures and milestones explicitly. Use a proposed `GenericDefaultContext*.ll` prefix consistently for new fixtures; include changed existing fixture families as well. P20 and P22 cover existing generic-default/monomorphization paths; add others only when affected.
- Preserve functional and allocation/reuse coverage. Keep fixed warm-up/repetition conditions, zero-allocation assertions, retained-capacity checks and existing isolation. Never turn an uncertain proof into acceptance to satisfy a performance target.
- Measure `--local-regions`, `--callable-plans` and, for shared acquisition/effect changes, `--adaptation-plans` under the conditions in [CallablePlans](../../src/Benchmark/CallablePlans.md) and [Adaptations](../../src/Benchmark/Adaptations.md). Add a bounded G82-specific workload if those inputs do not exercise the changed context path. Include nested depth, sibling expansions and new retained record fields in cost review.
- Avoid a speedup claim without matched before/after measurements. Storing Types can reduce repeated work but also enlarge records; zero allocation after warm-up alone does not establish unchanged retained memory or runtime cost.
- Keep ordinary diagnostics: factual cause, code/category and relevant source location. This plan does not activate the full diagnostic-development workflow. Run any independently required snapshot check and review its differences rather than accepting them wholesale.
- Do not run NativeAOT. Never edit verification inputs while a build/verification is running.

Example final selection, to be finalized after fixture creation:

```powershell
./scripts/verify.ps1 -Mode Session -Fixtures 'GenericDefaultContext*.ll' -Milestone 20,22 -Name g82-final
```

This example is not the complete selection if an implementation unit changes other fixture families. A Session label does not imply native, milestone or measurement coverage that was not selected.

Store successes, failures and audit records under `artifacts/verify/`; measurements under `artifacts/benchmarks/`; disposable work under `temp/`. Preserve exact source/configuration identities, selected checks and remaining uncertainty. Commit only each unit's files after its required checks pass; associate formal evidence with the commit using `verify-commit.ps1`, then push the current branch to `origin` without force. Back up ignored evidence separately. Do not commit failing tests alone as a completed repair unit.

## 8. Documentation and change boundaries

At implementation time, update `docs/dev/CODEMAP.md` for changed phase responsibilities and query entry points. Update `docs/STATUS.md` only for verified support-boundary changes, and keep `docs/dev/PLAN.md` concise with remaining prerequisites. Record short session outcomes in `docs/dev/PLAN_HISTORY.md`; detailed evidence belongs in commits and artifacts.

No SPEC, STYLE or LIBRARY change is currently needed. If a real semantic contradiction emerges, state the rationale and principle alignment, update affected formal chapters/examples/tests/milestones together and record the intake in `draft/INTEGRATED.md`. Do not narrow required language behavior to match incomplete implementation.

This proposal stays in `draft/Proposals` while unresolved. Preparing it is not a specification intake, so it does not update the integration register or freeze earlier inputs. Any later closure follows the repository's disposition/register/folder rules; frozen proposal text is not rewritten.

## 9. Completion conditions

G82 can be closed only when all applicable conditions hold:

1. The original checking input is tracked in a regression and no longer faults; an executable counterpart runs at O0/O2 with the intended result.
2. D1–D6 are independently classified, with repaired cases verified and any already-correct or separate unsupported cases supported by concrete evidence and a named disposition.
3. Acquisition, projection Types, Callable Loans, cleanup effects and lowering agree on the operation context. Invalid acquisition and Loan plans remain rejected.
4. Residual destruction is correct for Copy, owner and borrow payloads, including partial transfers, guards and abandoned evaluation.
5. Nested, repeated, forwarded and recursive paths in the verified scope have no missing or duplicate substitution. Shared syntax survives multi-Type call order and lifecycle checks.
6. Declaration checking and prepared-slot protections retain the specified behavior. Proof-scope limitations that affect the intended repair are resolved rather than assumed harmless.
7. Focused units and the final Session pass, with required native/milestone coverage, allocation/storage checks and measurements tied to the final inputs. All failures remain retained.
8. Support boundaries and navigation match the verified result; remaining unrelated limitations have explicit owners. History does not claim an unproven first-bad commit, full generic support or a measured speedup.

## 10. Conditions for revising the plan

- If U0 does not reproduce a family, update its evidence classification and avoid speculative edits solely to match the supplied report.
- If Binding's declaration plan is wrong before any default expansion, create a minimal reproducer and repair that responsibility; do not compensate in lowering.
- If proof scope changes universal acceptance, resolve the missing assumptions or stage distinction before broadening acquisition normalization.
- If cleanup operations cannot identify the Subject's original context, carry that provenance explicitly in the owning plan rather than guessing from syntax or the current cleanup position.
- If a path requires an independent forwarding/Origin repair, determine whether it blocks G82 completion and track the dependency explicitly.
- If operation-context interpretation cannot express the required semantics or keeps expanding into unrelated machinery, compare the current inline model with instance-based default evaluators in a separate design decision. Do not silently replace G72's execution model as part of a local fix.
- If performance regresses, prefer bounded context reuse and recorded plan facts; do not relax semantics, tests or allocation assertions.
- If concurrent work changes the same inputs, rebase the working assumptions and rerun affected verification on the actual final source. Historical warnings about a particular worktree are not current coordination evidence.
