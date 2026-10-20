# M69 Default Loan Correspondence Plan

Date: 2026-10-20. Selected planning direction: option 3, producer-owned template coverage.
Intake only; not implementation approval. Two correction rounds after independent review;
round 2 only reconciles the line-count estimate with its component ranges.
Inspected baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8`.
No compiler source was changed, built or executed for this candidate.

## 1. Required meaning and root cause

SPEC 7.2.3 checks a default at its declaration, evaluates an omitted default in parameter order, and ends its inspection Loans before callee entry. SPEC 15.6.4 checks the actual effect location against live Loans; 15.6.7 preserves reservation/activation order. SPEC 8.15 checks every admitted Semantics case. SPEC 23 distinguishes an actual language violation, an unimplemented rule, and an internal contract violation.

The design chain is:

1. A template owns failures invariant under its valid copies; a caller owns interactions with Loans supplied from outside that template evaluation.
2. Current A5 retains a Loan's display anchor, but loses the issuing scope. Its recorder sees the access scope alone and therefore cannot distinguish these cases.
3. `MirBorrows`, which already owns issued Loans and conflicting candidates, must decide this ownership from the participating semantic events before selecting a reportable conflict.
4. Its unconditional copy marker and the resulting A6 drop become unnecessary for A5. A1/A2's distinct contracts remain unchanged.

`MirBorrows.Loans.Issue` receives `SourceInfo` but stores only `body.Subject(source)` in `Loan.Site`. `Order` moves complete Loan records and renumbers `loanAt`; numerical IDs are therefore not persistent source identities.

`MirBorrows.Checks.Conflict` returns the first clean conflicting candidate. `MirBorrows.Reports.Report` then marks every Language problem inside an `Inlined` scope, and `MirReport.Add` drops that record. This can hide both a caller-owned conflict and a later caller-owned candidate at the same access behind the first template-owned candidate. The latter is a consequence of the current control flow, not a separately reproduced defect.

The scope-only premise in DECISIONS R4-M69 is explicitly conditional. PLAN G171 records a normal implicit receiver acquisition of a mutable static, a known write effect inside an omitted default, and no corresponding effect in the final callee's summary. The template has no caller-issued Loan. This invalidates the premise that every caller interaction is detected outside the copy.

This is a recorded integration hazard, not verified production wrong acceptance: F5 is incomplete, `Accessing.Effect` is empty, and ordinary compilation still has `Unsupported_Kd`. The exact G171 source and its evidence must be recovered or recreated; neither was present in the inspected PLAN row.

## 2. Verified owners and influence range

| Owner/site | Relevant responsibility and proposed disposition |
| --- | --- |
| `MirBuilder.Defaults.cs` | Shared prologue borrows; omitted defaults; recursive templates versus copies; preserve evaluation order. |
| `MirBuilder.Defaults.Copies.cs` | Scope/local/block remapping, composed substitutions, case reacquisition, dropped instance-free Drops; establish the copy simulation invariant. |
| `MirBody.Inlining`, `MirScope.Outer`, `SourceInfo` | Existing expansion ancestry, distinct even for copies with the same syntax anchor; use, do not replace. |
| `MirCases.Views`, `.Layer`, `.Reborrows`, `.Adapt` | Existing case correspondence; use in the proof harness, never equate body-local case bit numbers. |
| `MirBorrows.Loans.cs` | Preserve the issuing statement index with each actual Loan, including after ordering; obtain its SourceInfo from the immutable body. |
| `MirBorrows.Checks.cs`, `.Candidates.cs`, `.CheckSets.cs` | Decide ownership before first-candidate selection; preserve holder mode, clean/tainted priority and all check-set semantics. |
| `MirBorrows.Activations.cs` | Activation and observing destruction depend on both the conflicting Loan and the reservation being activated. |
| `MirBorrows.Reports.cs`, `.Witnesses.cs` | Format the selected owned failure; do not infer ownership from diagnostic text or syntax. |
| `MirBorrows.Flow.cs`, `.Endings.cs` | R-a is a separate inspection-lifetime rule; no Loan-wide diagnostic suppression and no ending of caller-carried Loans. |
| `MirEffects.Keys.cs` | Already supplies keys at an actual Call/Drop CFG site. Its current boolean coalesces Unknown/Limit/Derived; 2b-1 must publish their classes before F5 integration. Never aggregate defaults into the final callee. |
| `MirAnalysis.cs`, `MirBorrows.Solve.cs` | Every required template case is checked or has its recorded blocking limitation; audit this prerequisite. |
| `MirReport.cs`, `MirInit.cs`, `MirDrops.cs` | Keep A1/A2's existing Inlined contract. A5 no longer sends the blanket Inlined fact. |

Concrete census: `Conflict` has one ordinary-access caller and two activation/destruction callers; `Loan.Site` has two reporting reads; `Ended` reports a guard anchor directly and has no Loan candidate. Those four failure paths all need ownership treatment in the same behavior unit. Census again at implementation head; do not add an effect-only exception.

Relevant existing tests are `MirAnalysisTest.AnInlinedProblemIsDropped`, `ADefaultsProblemsAreItsTemplates`, `MirBuildTest.ACopyLiesInItsInlinedScopeAtItsTemplatesLocations`, `RecursiveDefaultsAreTemplateCallsAndFailedOnesOpaque`, `DefaultCopiesScaleLinearly`, `GenericDefaultTest.ReplicasAgreeWithTheirBody`, `MirBorrowsTest.ADefaultsTaintMakesItsConflictsUnsupported`, its P13 hold tests, and its allocation/scaling tests. Verify the exact method selectors again at the implementation head before running them.

## 3. Chosen representation: preserve one existing fact

Replace private `Loan.Site: SourceRef` with an issuing statement index into `body.Statements`; obtain scope from that statement's
`SourceInfo` and the display anchor with `body.Subject(source)` at the two current reporting consumers. Every current `Issue` caller
is an Assign statement in `Loans`, which already has this index. The validated body remains immutable throughout analysis. Record
the integer directly; do not retain both Site and SourceInfo or add a location-to-source table.

The independent review proposed this refinement to the author's SourceInfo field: it preserves the same semantic input and the exact
issue location with less duplicated storage. M69-0 verifies the complete issuance census and M69-1 measures layout/lookup cost.
If a future producer issues outside this domain, redesign the common issue-location representation before adding that producer.

`Order` copies this field with the Loan. `loanAt` remains the sole issued-location-to-number map. Tests identify a Loan by `(body, selected case, issuing MIR location)` and then follow `loanAt` through the ordering; the model receives a label derived from that relation. The label is test evidence, not a second production identity.

Do not add an expansion registry, a template-certificate cache, or `SupportsDefaultConflict` gates. Builder provenance is established as a universal invariant, not recomputed by diagnostic consumers. Retaining template ID/Subst per expansion would be justified only by a demonstrated case the current scope tree and published copy invariants cannot cover; no such counterexample was established in this inspection.

Similarly, do not add a per-template completed-view table merely to infer coverage from the absence of diagnostics. Existing pipeline completion and failure publication must first be shown sufficient; if they are not, stop this candidate before integration and return the counterexample for a new shared design.

## 4. Coverage rule and its hard proof gate

Define a failed comparison's *participating scopes* from its actual producer facts:

- Ordinary access/effect: access scope and conflicting Loan's issuing scope.
- Activation/observing destruction: those scopes plus the activated reservation's issuing scope.
- Guard ending: `EndBorrow` scope and the candidate local's `SourceInfo.Scope`; no synthetic Loan identity.

The candidate template boundary is the deepest common `Inlined` ancestor of all participating scopes. No common boundary means the current body owns the failure. The nearest Inlined ancestor of the access alone is insufficient; equal SourceRef spans are insufficient.

Before using this criterion to omit anything, prove the following simulation property for the builder and A5 rules:

> Restrict a valid copy's states to Loans/guard markers issued within an enclosing template evaluation. For operations inside that same evaluation, its selected template case simulates those states and each failed comparison, including retention, liveness, effects, reservation mode and nested copied evaluations. Caller-supplied Loan identities remain outside this restriction.

The proof must cover case reacquisition, type/projection substitution, shared-cut non-issuance, composed nested calls, omitted Drops, join/loop liveness, and copied references that retain a caller Loan. It must explain why caller values cannot introduce a template-local Loan before its issue or keep it across an invalid template lifetime without the template's own failure. `GenericDefaultTest` is corroboration, not the proof itself.

The lifecycle half is equally required: every template view on which the argument relies must be analyzed, or compilation must retain the existing located failure that prevents that view being treated as checked. Audit A1 storage truncation, A5 storage limits, P13-held views, `CallRetention.Limit`, destruction bounds, Opaque templates/folds, case-bound rejection and recursive template calls. A held or limited template is not evidence of a proved Language failure. The copy ownership theorem must instead show its dependent failure is already owned by that incomplete template, whose real limitation survives. Do not turn an unrelated template limitation into proof for an independent caller failure.

If this theorem cannot be established for the complete current operation family, the behavior unit does not land. Keep existing preintegration limitations and record the exact obstacle. Do not silently accept extra duplicate diagnostics as complete support, nor add shape-specific suppression permissions. A decidable genuinely caller-owned conflict remains its normal Language problem; uncertainty about suppression is not itself `Unsupported_Kd`.

### Nested examples the proof and tests must distinguish

Let `O` be an outer copied default and `I` a nested copied default inside it.

| Loan/guard issue | Access/ending | Ownership |
| --- | --- | --- |
| Caller outside O | Inside I | Caller; no common expansion. |
| O, outside I | Inside I | O template, not I template; the O template contains the corresponding interaction. |
| I | O after I | O template, subject to the lifetime simulation above; I alone does not contain the access. |
| One sibling copy inside O | Another sibling inside O | O template, if the value legitimately flows between them; otherwise its lifetime rule rejects it. |
| I | Inside I | I template. |
| First top-level copy | Second top-level copy | Caller; equal source text does not make the expansion scopes equal. |

For activation, an externally issued reservation makes the failure caller-owned even when its competing Loan and activation are in I. This third participating scope is mandatory.

## 5. Producer algorithm and deletions

Add the ownership rule in `MirBorrows.Defaults.cs` as part of the same existing component, with a short contract. `MirValidator` already guarantees `Outer < scope index`, so advance the larger of two scope indices to its Outer until they agree, then use `Inlining` to select their deepest common Inlined ancestor; an ordinary body has none. Count traversed edges in `Work`. Reuse the current scope tree; no ancestor matrix or cache.

Pass the operation's ownership scope into `Conflict` as semantic input. For activation/destruction, first intersect the operation scope with the reservation's issuing scope. This is an input to the same comparison rule, not a bool/mode selecting a special default algorithm.

Within `Conflict`, after a candidate is known to conflict and before selecting it, determine whether the validated template-ownership invariant owns that Language failure. Skip covered clean candidates and continue the existing root list, so a later caller-owned clean candidate survives. Preserve the existing first clean over tainted rule for remaining candidates. Do not erase a taint-only Unsupported result using a Language-coverage proof.

For `Ended`, use the candidate local's real source context and the ending context before choosing the report. Do not send its anchor through the Loan path. R4-M68's shared-only activation check and the check-set dataflow remain unchanged.

The recorder receives only failures this body owns. Remove A5's unconditional `body.Inlining(...)`, Inlined fact emission and `held && inlined < 0` exception. The held rule then applies uniformly to remaining owned failures. Keep A1/A2, `MirReport.Add`, and the four-fact array size until a separate justified change proves their contract or capacity differs.

Future F5 effects use the same rule at their actual call/drop, including known static and unknown-environment effects. Approved
2b-1/2b-D class publication is a prerequisite: the current `Keys(..., out bool)` cannot distinguish Unknown, Limit and Derived.
No source-span matching, final-callee effect union, diagnostic-code-based ownership or cached "Loan already reported" state.

Record identity is a separate existing owner. `MirReport.Add` currently merges by `(code, subject, body)` and keeps the first facts;
two copies in one body can share a SourceRef while interacting with different caller Loans. Capture and compare the selected owned
conflicts before Add. Do not infer that all survive as distinct public records from that evidence. U11's Reason/record/publication
contract must cover these cases, or the existing merged expectation must be explicitly justified and verified under DIAGNOSTICS.
This is a completion gate for M69/G171, not a new copy ID in the report key or invented relocation to the omitting call.

This supersedes only R4-M69/R4-M73's unconditional A5 copy disposition. It preserves R4-M68, A1's PreparedInput Move mapping, A2's incomplete-drop ownership, and R4-M70's temporary taint until the independently approved F9 lifetime work removes it.

## 6. Small implementation units

### M69-0 — Ordinary-source correspondence and theorem, no behavior change

Recover/recreate the G171 source through the normal parser/binder/builder. Start from existing static-receiver and default-call tests, not internal Ref injection. Save source, exact revision/configuration, MIR dump and A4 per-site keys. Demonstrate caller reservation before the copy, conflicting write inside it, absence of that write in the final callee, and the existing Unsupported boundary.

Concrete starting probe from option 1, unexecuted and not claimed accepted syntax or recovered G171 evidence:

```kimi
struct Counter
    public var value: i32
    public init() => self.value = 0
    public func touch(self: uniq/Self, n: i32 = rewrite()) => ()
group State
    public var counter: Counter = Counter.init()
func rewrite() -> i32
    State.counter = Counter.init()
    return 0
func entry() => State.counter.touch()
```

Recover the original fixture if available, otherwise minimize this shape through ordinary compilation. If it fails to bind or lowers
to an Opaque path, preserve that result and its limitation; never modify MIR or weaken lowering merely to force the desired chain.

Produce a test-side trace from normal MIR through actual issue location, `loanAt` ordering, holder flow, conflict check and record disposition. Reuse existing internal test access or an allocation-free diagnostic snapshot at the owning layer; no production tracing table. Keep trace helpers in the owning test class and out of benchmark timing/allocation windows.

Run the selected template/copy/nested/case examples through a test-side model adapter. Handwrite SPEC expectations separately; model input may project actual facts but may not invent a Loan, scope, continuation or matching edge. Model fixture success alone is not an integration result.

Exit: reviewer accepts the simulation/lifecycle argument and every intended processing chain has a normal-input witness or a documented non-reachable/unimplemented classification. An unreachable modeled chain is removed from the integration claim, not forced into compiler lowering. Missing F5/F9 is an explicit prerequisite; this unit must not implement either secretly.

### M69-1 — Necessary structural preparation, behavior preserved

Before changing a member/file beyond the architectural limit, extract the existing A5 copy-disposition responsibility into its owner file without changing decisions. Retain the issuing statement index instead of display-only Site and derive SourceInfo at its consumers. No coverage decision changes yet.

Only perform an extraction justified by measured limits/hotspots; do not mechanically split to create a new wrapper. This is a separate behavior-preserving commit when restructuring is needed. Verify exact current problem/fact output, template/copy diagnostics, ordering stability, and zero warm allocation. Record the representation decision and net size.

### M69-2 — Producer ownership and candidate selection

Implement the common-scope rule across ordinary access, activation, observing destruction and guard ending. Delete A5's blanket Inlined emission and its copy-before-hold recorder exception in the same unit. Add failing-before/passing-after tests at the lowest normal-input layer that observes the changed behavior.

M69-0's proof and the applicable 2b-D/2e-D package approval (R4-M74 OQ2) are blocking prerequisites. The effect regression may require
the approved F5 effect producer first; if so land the behavior unit with that producer in one reviewed integration group, or after a
publication-only producer that leaves effects gated. Do not ship the unsafe effect path while waiting for this unit.

Exit: caller-owned failures survive; template-owned failures have one owning report; a covered first candidate cannot hide an uncovered later candidate; existing A1/A2 and held/limited behavior remains justified; no branch selects individual default forms.

### M69-3 — F5/F9 integration proof and deletion gate

After separately approved F5 and F9 units supply their real inputs, replay M69-0's sources and model correspondence on the integrated head. Exercise R-a inspection endings immediately before final call checking, including reserved explicit argument temporaries. Prove that a reference copied by a default retains its caller Loan after inspection Loans end.

Only the F9 unit removes default taint, its markers and associated tests; M69 does not independently duplicate that change. Test inline and recursive-template paths, rebind/reset, generic case selection and paired independent conflicts on the same issued Loan.

Exit: exact source -> MIR -> issued Loan -> per-site effect/activation -> producer decision -> model/record/publication correspondence,
with the distinct-copy/merged-record gate above satisfied. No model-only assertion is integration evidence. Close G171 only then.
Nothing here authorizes R6-check publication or changes the default pipeline.

## 7. Required verification matrix

| Rule | Normal-input witness and paired case |
| --- | --- |
| G171 | Caller static receiver reservation + default write; pair with disjoint static/read-only/default supplied explicitly. |
| Loan identity | One caller Loan carried through copied reference, holder join and default return; pair with new reborrow at a different issue location. |
| Independent conflicts | Same issued Loan meets two later independent accesses; neither is removed because the other is covered. |
| Candidate priority | Covered internal candidate appears first, caller candidate later at one access; reorder independent declarations to reverse IDs without changing verdict. |
| Nested ownership | All six rows in the nested table, including equal syntax reused in separate copies and externally issued activation reservation. |
| Guard ending | Guard candidate source and EndBorrow in one expansion versus outside/inside crossing; no invented Loan. |
| Generic substitution | Concrete and two-view generic copies, nested composed substitutions, PairAcquire Copy/Reborrow, instance-elided Drop. |
| Completion | P13-held template/caller, A1/A5 resource bound, CallRetention/destruction limitation, opaque/failed template; independent caller conflict not certified by an unrelated limitation. |
| R-a | Inspection Loan on PendingSlot and reserved argument temp ends; caller Loan in a copied reference and later independent write remain. |
| Recursion | Recursive template call and nonrecursive inline equivalent; each actual call's effects/retention stay at that site. |
| Stability | Rebind changes default/substitution, same-version analysis reuse, reset; no stale scope or Loan identity. |
| Record identity | Two copies sharing a source node but different caller Loan anchors; inspect both pre-Add decisions and the contractually correct merged/separate records, requirements and related sites. |

Use `MirBuildTest`, `MirBorrowsTest`/its planned `.Defaults` partial, `MirAnalysisTest`, `GenericDefaultTest`, `DefaultAccessTest`, `CallReservationTest`, `MirEffectsTest`, plus required census/ledger selections. A standalone synthetic validator/model test may test a contract boundary, but cannot satisfy a normal-source integration row above.

Baseline final diagnostics are insufficient because old analysis or Unsupported can conceal an A5 miss. Preserve raw candidate facts, the chosen owner and A5 records before publication, then compare final diagnostics and their exact locations/requirements.

Compiler units use `scripts/verify.ps1 -Mode Unit` with validated class/method selectors, architecture checks, affected allocation tests and the plan's census/ledger. End the session with its required Session. Builder changes also run the mandated sel check; add native fixtures only for generated behavior affected by an actual lowering change. No NativeAOT. Associate evidence with `verify-commit.ps1` before pushing each unit; keep failures.

This proposal-only task requires diff/reference review, not builds or tests.

## 8. Expected size and performance; comparison criteria

Estimates, not measurements:

- Production: issue-context replacement +5/-5; owner scope walk and candidate routing +35 to +65; reporting deletion -12 to -22; guard/activation integration +10 to +20. Expected net +23 to +73 lines, excluding behavior-preserving moves and documentation.
- Tests/evidence adapters: +180 to +300 lines shared across the existing owners, not a duplicate test framework. F5/F9 implementation is outside this estimate.
- Loan storage: replace SourceRef with one statement index; no extra SourceInfo copy. Measure actual padded struct size and both pooled Loan arrays; no guessed allocation baseline change or unmeasured saving claim.
- Runtime: no extra dataflow solve, no new per-program dictionary/table; scope walking adds O(depth) per actually conflicting candidate, guard ending or activation context. Ordinary non-copy cases should exit quickly. Record full scope edges in Work.
- Allocation: expected 0 B on existing warm repeated analysis; retained memory changes by the measured Loan-record delta. Source lookup adds body-array reads to compare against the author's SourceInfo variant. The candidate scan may visit previously hidden candidates; this is required work, not a regression to disguise.
- Measure equivalent normal-source workloads at N/2N/4N defaults, nested depth, same-root candidate fan-out, generic cases and copied-reference flows. Compare time, allocation and retained memory against the same baseline; timing loops belong in `src/Benchmark` and keep failed evidence.

An ancestor matrix or cache is rejected until measurements show the linear parent walk matters; its retained memory and invalidation would exceed the current need. A runtime template/Subst/coverage registry is rejected until a concrete counterexample defeats the universal simulation contract.

Compared with an in-place recorder exception, this fixes first-candidate loss and all four A5 producer paths. Compared with lifting default effects to a final call, it preserves the actual evaluation points and existing A4 ownership. Its risk is the required simulation proof: if that proof fails, the plan must stop and compare a stronger shared representation, not add per-form exemptions.

This follows One Concept/One Form by using the existing scope tree and actual Loan issuance, Local Reasoning by deciding at the comparison producer, Explicit Semantics by separating caller and template ownership, and Verifiable Changes by requiring normal-input correspondence on the exact integrated revision.

## 9. Comparison, adoption and review status

Three options were independently authored, one agent per option, then reviewed by a separate reviewer:

| Option | Assessment | Decision |
| --- | --- | --- |
| [1: prove existing scope-only rule](../Sketches/A5%20Step%202/S3-M69/M69%20Scope%20Only%20Design.md) | G171 contradicts its required premise; template-local examples cannot establish caller coverage. Useful normal-source/model bridge. | Reject as completion; retain its evidence procedure and hold. |
| [2: pointwise caller effects](../Sketches/A5%20Step%202/S3-M69/M69%20Caller%20Effect%20Design.md) | Correctly reuses CFG/A4 sites, but still needs an ownership rule for mixed template/caller Loans. | Incorporate its F5 integration constraint; insufficient alone. |
| 3: producer-owned coverage | Actual issue context plus existing scope tree decides before first-candidate selection, across all four A5 paths. No registry, replay graph or second dataflow solve. | Select subject to simulation/lifecycle and source-correspondence gates. |

The selected plan fixes the missing input at its owner, preserves evaluation order and avoids a diagnostic filter. Its expected
size/performance advantage is structural and unmeasured. The [review record](../Sketches/A5%20Step%202/S3-M69/Comparison%20and%20Review.md)
records corrections. Independent review accepted this direction only as a gated plan; the universal proof and normal-source witnesses
remain implementation prerequisites, not facts established by the model or this documentation task.

Maximum two correction rounds. A rejected family is redesigned as a whole. A remaining blocker after round two is reported to the owner for representation/whitelist/hold; it is not reclassified as a nonblocking concern.

Adoption would record the limited R4-M69/R4-M73 supersession in DECISIONS with its first implementation, update affected architecture/diagnostic contracts, and update PLAN/STATUS only as their actual facts change. This candidate does not edit SPEC, SETTLED, frozen proposals or translated guides.

Authorities: [SPEC](../../docs/SPEC.md) 7.2.3, 8.15, 15.6.4, 15.6.7 and 23;
[DECISIONS](../../docs/dev/DECISIONS.md) R4-M68–M74; [PLAN G171](../../docs/dev/PLAN.md);
[A5 step 2](2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) 2b-1/2b-D/2e-D/U11;
[DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md), [SETTLED](../../docs/SETTLED.md),
[CODEMAP](../../docs/dev/CODEMAP.md) and [VERIFICATION](../../docs/dev/VERIFICATION.md).
Proposal selection supplies no new vocabulary, exception, package approval or verified compiler support.
