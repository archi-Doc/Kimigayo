# M69 Default Loan Correspondence Plan

Status: 一部取り込み (open). Selected direction: option 3, producer-owned template coverage.
Adoption: the owner adopted this plan for implementation on 2026-10-20 (PLAN). M69-1 landed as DECISIONS R4-M84 (`37184417`);
M69-0, M69-2 and M69-3 keep the gates below. Adoption establishes no support.
Baselines: inspected at `f373cd67`; revised at `338831f7` against the Principles (§9). Line numbers name `338831f7`.

## 1. Required meaning and root cause

SPEC rules:

- §7.2.3: a default is checked at its declaration, an omitted default is evaluated in parameter order, and its inspection Loans
  end before callee entry.
- §15.6.4: an effect is checked against the Loans live where it occurs; §15.6.7 keeps reservation and activation order.
- §8.15: every admitted Semantics case is checked.
- §23: a language violation, an unimplemented rule and an internal contract violation are distinct.

Design chain:

1. **Meaning.** A template owns the failures that every valid copy of it repeats. The caller owns every interaction with a Loan
   issued outside that template evaluation.
2. **Gap.** A5 decides ownership from the access scope alone. The issuing location was lost until M69-1 restored it (R4-M84).
3. **Owner.** `MirBorrows`, which issues Loans and selects the conflicting candidate, decides ownership from all participating
   events before it selects a reportable conflict.
4. **Removed.** A5's unconditional Inlined fact and the drop it causes in `MirReport.Add`. A1's and A2's Inlined contracts stay.

Current control flow (`src/Kimi/Compiler/Mir/Analysis/`):

- `MirBorrows.Checks.Conflict` (`:97`) returns the first clean conflicting candidate, else the first tainted one.
- `MirBorrows.Reports.Report(code, anchor, via, at, reservation)` (`:43`) takes `Inlining` of the access scope only. Inside a
  copy it adds `MirFactKind.Inlined`, held view or not, and `MirReport.Add` drops the record; outside copies a held view reports
  `Unsupported_Kd`.
- Hence a caller-owned conflict inside a copy is dropped, and a template-owned first candidate can hide a later caller-owned
  candidate at the same access. The second consequence follows from the control flow; it has no normal-input witness yet.

R4-M69's scope-only premise is conditional. PLAN G171 meets its revisit trigger through ordinary source: a caller's whole-static
reservation lies outside an inline default copy whose known write conflicts with it, the template issues no Loan, and the final
callee's summary has no static key (§6, M69-0). F5 is unimplemented (`Accessing.Effect`, `Checks.cs:236`, is empty) and ordinary
analysis reports `Unsupported_Kd`, so G171 is an integration hazard, not verified wrong acceptance.

## 2. Owners and influence range

| Owner/site | Responsibility and disposition |
| --- | --- |
| `Build/MirBuilder.Defaults.cs`, `.Defaults.Copies.cs` | Prologue borrows, omitted defaults, templates and copies; scope/local/block remapping, composed substitution, case reacquisition, dropped instance-free Drops. Source of the copy simulation invariant; no change planned. |
| `MirBody.Scopes`, `MirBody.Inlining` (`MirBody.cs:96`), `MirScope` (record, `MirForms.cs:199`) | The scope tree and expansion ancestry, distinct even for copies with equal syntax. Gains the lowest-common-scope query (§5). |
| `MirCases` (`Views`, `Layer`, `Reborrows`, `Adapt`) | Case correspondence for the proof; never equate case bits of different bodies. |
| `MirBorrows.Loans.cs` | `Loan.Statement` (R4-M84). Both `Issue` callers are Assign statements: a non-raw Ref (`:64`) and a reborrowing PairAcquire (`:73`). |
| `MirBorrows.Checks.cs`, `.Candidates.cs`, `.CheckSets.cs` | Decide ownership in `Conflict` and `Ended`. Preserve one candidate per Loan and mode, the clean-over-tainted priority and all check sets. |
| `MirBorrows.Activations.cs` | Activation and observing destruction also involve the reservation being activated. |
| `MirBorrows.Calls.cs` | Owns P13's `held` flag (R4-M73, `:59`); unchanged. |
| `MirBorrows.Reports.cs`, `.Witnesses.cs` | Format the selected owned failure. Lose the access-scope Inlined disposition. |
| `MirBorrows.Flow.cs` | `Placed` (`:66`) applies R4-M70's default taint with its own Inlined comparison; it stays until the F9 unit removes the taint. |
| `MirEffects.Keys.cs` | Keys at actual Call/Drop sites. `Keys(..., out bool unknown)` (`:37`) coalesces Unknown, Limit and Derived; 2b-1 publishes those classes before F5 integration. Never aggregate defaults into the final callee. |
| `MirAnalysis.cs`, `MirBorrows.Solve.cs` | Each template view the proof relies on is analyzed or keeps its blocking limitation (audit). |
| `MirReport.cs`, `MirInit.cs` (`:332`), `MirDrops.cs` (`:308`) | A1's and A2's Inlined facts and `MirReport.Add`'s drop stay. |

R-a (inspection Loans end before callee entry) is F9's future rule; no MIR code implements it yet. `MirBorrows.Endings.cs` holds
E8's in-place destruction endings and is unrelated.

Failure-path census (repeat it at the implementing head):

- `Conflict` callers: ordinary access (`Checks.cs:73`), activation (`Activations.cs:73`), observing destruction (`Activations.cs:101`).
- `Ended` (`Checks.cs:163`) reports a guard anchor with no Loan candidate.
- `Loan.Statement` reporting reads: candidate anchor (`Reports.cs:34`), reservation fact (`Reports.cs:63`).
- The other `Unsupported_Kd` paths (`Checks.cs:68`, `:86`; `Activations.cs:95`) carry no Inlined fact and are unchanged.

All four failure paths take ownership in one behavior unit; no effect-only exception.

Existing tests (`tests/xUnitTest/Tests/`): `MirAnalysisTest.AnInlinedProblemIsDropped`, `.ADefaultsProblemsAreItsTemplates`;
`MirBuildTest.RecursiveDefaultsAreTemplateCallsAndFailedOnesOpaque`, `.ACopyLiesInItsInlinedScopeAtItsTemplatesLocations`,
`.DefaultCopiesScaleLinearly`; `GenericDefaultTest.ReplicasAgreeWithTheirBody`; `MirBorrowsTest.ADefaultsTaintMakesItsConflictsUnsupported`,
its P13 hold test `AListedLossIsUnsupported` and its scaling and allocation tests (`TheWorkScalesWithTheProgram` to
`AWarmRunAllocatesNothing`); `ReceiverDefaultTest`, `RecursiveDefaultTest`, `DefaultAccessTest`, `CallReservationTest`, `MirEffectsTest`.

## 3. Representation: the issuing statement (landed)

DECISIONS R4-M84 is authoritative. In short:

- `Loan.Statement` indexes `body.Statements`. The issuing scope is that statement's `SourceInfo.Scope`; the display anchor is
  `body.Subject(source)`. `Order` copies the index with the Loan; `loanAt` stays the only location-to-number map.
- Tests identify a Loan by (body, selected case, issuing MIR location) and follow `loanAt` through the ordering. The label is test
  evidence, not a second production identity.
- A future producer that issues a Loan outside an Assign first redesigns the common issue-location representation. 2b-3's
  protection of a static's own destruction (R3) may issue its Loan at a Drop or issue none; 2b-D decides which, and with it R3's
  participating scopes. No Loan is invented.

Not added: an expansion registry, a template-certificate cache, a per-template completed-view table or `SupportsDefaultConflict`
gates. Builder provenance is a universal invariant, not recomputed by diagnostic consumers. Retaining a template ID or Subst per
expansion needs a case that the scope tree and the copy invariants cannot cover; none is known. If existing completion and failure
publication prove insufficient for §4, stop before integration and return the counterexample for a shared design.

## 4. Coverage rule and its proof gate

A failed comparison's **participating scopes** come from its producer facts:

| Failure | Participating scopes |
| --- | --- |
| Ordinary access or effect | The access scope and the conflicting Loan's issuing scope. |
| Activation, observing destruction | Those two and the activated reservation's issuing scope. |
| Guard ending | The `EndBorrow` scope and the guard local's `SourceInfo.Scope`; no synthetic Loan. |
| 2b-3's R3 | Its Loan's issue scope if 2b-D issues one at the Drop, else the scopes 2b-D's producer contract names. |

The **template boundary** is the deepest Inlined scope that contains every participating scope. With no boundary the current body
owns the failure. The nearest Inlined ancestor of the access alone is insufficient; equal SourceRef spans are insufficient.

Before this criterion omits anything, prove the simulation property for the builder and the A5 rules:

> Restrict a valid copy's states to the Loans and guard markers issued within one enclosing template evaluation. For operations
> inside that evaluation, its selected template case simulates those states and each failed comparison, including retention,
> liveness, effects, reservation mode and nested copied evaluations. Caller-supplied Loans stay outside the restriction.

- **Simulation half.** Cover case reacquisition, Type/projection substitution, shared-cut non-issuance, composed nested calls,
  omitted Drops, join/loop liveness and copied references that retain a caller Loan. Show that a caller value cannot introduce a
  template-local Loan before its issue, or keep one across an invalid template lifetime without the template's own failure.
  `GenericDefaultTest` corroborates; it is not the proof.
- **Lifecycle half.** Every template view the argument relies on is analyzed, or compilation keeps the located failure that stops
  that view from counting as checked. Audit A1 storage truncation, A5 storage limits, P13-held views, `CallRetention.Limit`,
  destruction bounds, Opaque templates and folds, case-bound rejection and recursive template calls. A held or limited template is
  no proved Language failure: the theorem must show that the dependent failure is owned by that incomplete template, whose own
  limitation survives. An unrelated template limitation never certifies an independent caller failure.

If the theorem fails for the complete operation family, the behavior unit does not land: keep the existing limitations and record
the obstacle. Do not accept extra duplicate diagnostics as support or add shape-specific suppression. A decidable caller-owned
conflict stays a Language problem; uncertainty about suppression is not `Unsupported_Kd`.

Nested cases (`O` an outer copied default, `I` a copy nested inside it):

| Loan or guard issued | Access or ending | Owner |
| --- | --- | --- |
| Caller, outside O | Inside I | Caller: no common copy. |
| O, outside I | Inside I | O's template: O contains both. |
| I | O, after I | O's template, subject to the lifetime simulation; I alone does not contain the access. |
| One sibling copy inside O | Another sibling inside O | O's template if the value flows legitimately; otherwise its lifetime rule rejects it. |
| I | Inside I | I's template. |
| First top-level copy | Second top-level copy | Caller: equal source text does not make the copies one scope. |

An activation whose reservation was issued outside the copy is caller-owned even when its competing Loan and the activation lie in I.

## 5. Producer algorithm and deletions

The scope-tree query belongs to the scope tree's owner, beside `Inlining`. `MirValidator` (`MirValidator.cs:221`) guarantees
`Outer < index` for every scope but the root, so the walk is linear and needs no matrix or cache:

```csharp
// MirBody: the innermost scope containing both scopes; the template boundary is Inlining(Common(a, b)).
internal int Common(int a, int b)
{
    while (a != b)
    {
        if (a > b) a = this.scopes[a].Outer; else b = this.scopes[b].Outer;
    }

    return a;
}
```

The ownership rule is A5's and goes into a new partial `MirBorrows.Defaults.cs` with a short contract. Ownership depends only on
the Loan and the operation's scopes, so both mode candidates of one Loan agree. Production needs only whether a boundary exists;
the deepest boundary matters to the proof.

- `Conflict` takes the operation's scope as semantic input: the access scope, or, for activation and observing destruction,
  `Common(access, reservation issue)`. It is an input to the same comparison, not a mode selecting a default algorithm.
- An access outside every copy (`Inlining(access) < 0`, computed once per operation) covers no candidate, and neither does its
  `Common` with a reservation, so bodies and operations without copies skip every walk; an activation computes that `Common` only
  for an access inside a copy. `Covered` adds one `Work` step per walk, as `Flow.Placed` does; §8's nesting-depth workload
  measures the O(depth) term.
- After a candidate is known to conflict and its clean sets take the operation, skip it when its Loan's issue scope shares a
  template boundary with that scope, and continue the root list, so a later caller-owned candidate survives. Keep the first-clean
  over tainted rule for the rest. A taint-only candidate keeps R4-M70's `Unsupported_Kd`; no coverage proof erases it.
- `Ended` applies the same test to the `EndBorrow` scope and the guard local's scope before it reports. R4-M68's shared-only
  activation check and the check-set dataflow are unchanged.

Illustrative shape of the `Conflict` change (not normative):

```csharp
if (candidate.Clean.Takes(taking))
{
    if (this.Covered(scope, candidate.Loan)) // Inlining(Common(scope, issue scope)) >= 0: the template's failure (M69).
    {
        continue;
    }

    return c;
}
```

Deletions in the same unit:

- `Report`'s access-scope `Inlining` call, its Inlined fact and the `held && inlined < 0` exception. The held rule then applies
  uniformly to the remaining owned failures.
- The Inlined clauses of `MirBorrows.cs`'s entry contract and `Reports.cs`'s comments (`:7`–`:14`, `:38`–`:42`). The superseding
  DECISIONS entry replaces R4-M73's "R4-M69 governs inline default copies" sub-bullet.
- A5's fact buffer is sized by what the landing group emits: three facts (Anchor, Use/Store, Reservation) alone, or with 2b-3's
  static-key and call facts (PD31) when M69-2 lands in one group with it.

Kept: `Flow.Placed`'s taint comparison (until F9), A1/A2's Inlined facts and `MirReport.Add`'s drop. A1/A2 apply §4's test with
the problem location as the only participating scope (`Inlining(Common(s, s)) = Inlining(s)`). Moving them to producer-side
omission, which deletes `MirFactKind.Inlined` and the drop, is a staged removal M69-2 records in PLAN unless their contracts are
shown to differ.

F5 effects use the same rule at their actual Call or Drop, including known static and unknown-environment effects. The approved
2b-1/2b-D class publication comes first. No source-span matching, final-callee effect union, code-based ownership or cached
"Loan already reported" state.

**Record identity (U11 gate).** `MirReport.Add` merges by `(code, subject, body)`, keeps the first record's facts and ORs views
(`MirReport.cs:24`, `:53`). Two copies in one body can share a SourceRef while meeting different caller Loans. Capture the selected
owned conflicts before `Add`; their survival there proves no public record. U11's Reason/record/publication contract covers these
cases, or the existing merged expectation is justified and verified under DIAGNOSTICS. No copy ID in the key and no relocation
to the omitting call.

This supersedes only R4-M69/R4-M73's unconditional A5 copy disposition. It preserves R4-M68, A1's PreparedInput Move mapping,
A2's incomplete-drop ownership and R4-M70's taint until the approved F9 lifetime work removes it.

## 6. Units

### M69-0 — Normal-source correspondence and theorem, no behavior change

Start from the recovered, independently reviewed G171 evidence, observed at `71984552` in the git-ignored
`artifacts/handoff/2026-10-20-r4-resume/2e/inline-effect-probe/` (run `run-20261020-014734`; sources in `actualsources/`;
SHA-256 `B6621422…` for the default source, `1F603953…` for its explicit twin). The block below is the default source with
annotations added:

```kimi
// receiver-group-default.kimi (Binding complete, MIR valid; ordinary Unsupported_Kd at both static accesses).
struct Counter
    public var count: i32 = 0
    public func add(self: uniq/Self, x: i32 = State.touch()) -> i32
        self.count += x
        return self.count
group State
    public var counter: Counter = Counter.init()
    public func touch() -> i32
        counter.count = 1
        return 0
    public func caller()
        _ = counter.add()          // Twin receiver-group-explicit.kimi: counter.add(State.touch()).
public func main() => State.caller()
```

Observed chain: the caller issues a whole-static TwoPhaseUniq Loan into `_2` at `bb1:0` in scope s1; the copied `touch` call lies in
s2, inlined in s1, with A4 key `counter!`; `_2` stays live until `move _2` into `add` at `bb2:0`; the template issues no Loan; the
final `add` has no static key. The qualified forms `State.counter.add(...)` and an unqualified top-level `touch` fail Binding; keep
them as recorded failures, not witnesses. `slice-default.kimi` is a second producer route (a RawSlice backing); the
compound-assignment sources are negative controls. The earlier 69-case model was not accepted, and its conformance repair stopped
after two blocking rounds pending the owner's direction; neither is M69-0 evidence, and M69-0's adapter does not continue that
family without the owner's decision.

1. Re-execute at the implementing head in the owning test class; the probe's reflection observer is not the deliverable. Preserve
   exact source, revision and configuration, ordinary diagnostics, validated MIR, A4 per-site keys and all A5 records. Trace each
   issued Loan through its statement, post-`Order` `loanAt`, holder and check-set membership and conflict selection, using existing
   internal test access; no production tracing table, nothing inside timing windows.
2. Supply the §4 simulation and lifecycle argument. A test-side model adapter may project actual facts but never invents a Loan,
   scope, continuation or matching edge; SPEC expectations are written by hand. Model success alone is no integration result.
3. Find normal witnesses for the six nested relations, the external activation reservation, a covered first candidate with a later
   caller candidate, guard crossings, two-view reacquisition, incomplete-template/independent-caller twins, distinct copies that
   share syntax and record publication. An unreachable chain is removed from the integration claim, never forced into lowering.

Exit: an independent reviewer accepts the argument, and every intended chain has a normal-input witness or a documented
unreachable/unimplemented classification. Replaying the historical counterexample alone does not satisfy this exit. F5 and F9
are prerequisites that this unit never implements.

### M69-1 — Issuing statement (landed, R4-M84)

Behavior-preserving: `Loan.Statement` replaced `Loan.Site`; reports derive anchors from it. The planned extraction of the copy
disposition was not needed: `MirBorrows.Reports.cs` has 86 lines, and M69-2 replaces that code in place.

### M69-2 — Producer ownership and candidate selection

Implement §5 across ordinary access, activation, observing destruction and guard ending; delete A5's Inlined emission and its
held exception in the same unit. Add failing-before/passing-after tests at the lowest normal-input layer that observes the change.

Prerequisites: M69-0's accepted proof and the applicable 2b-D/2e-D package approvals (R4-M74 OQ2). No R3 path lands with this
unit until 2b-D states R3's participating scopes. If the effect regression needs the approved F5 producer, land this unit with it
in one reviewed integration group, or after a publication-only producer that leaves effects gated. The unsafe effect path never
ships in between.

Exit: caller-owned failures survive; template-owned failures have one owning report; a covered first candidate cannot hide an
uncovered later one; A1/A2 and held/limited behavior stay justified; no branch selects individual default forms.

### M69-3 — F5/F9 integration proof and deletion gate

After the approved F5 and F9 units supply their real inputs, replay M69-0's sources and model correspondence on the integrated
head. Exercise R-a endings just before final call checking, including reserved explicit argument temporaries, and show that a
reference copied by a default keeps its caller Loan after inspection Loans end. Only the F9 unit removes the default taint, its
markers and tests. Test inline and recursive-template paths, rebind/reset, generic case selection and two independent conflicts
on one issued Loan.

Exit: exact source → MIR → issued Loan → per-site effect/activation → producer decision → model/record/publication correspondence,
with the U11 record gate met. Model-only assertions are no integration evidence. Close G171 only then. Nothing here authorizes
R6-check publication or changes the default pipeline.

## 7. Required verification matrix

| Rule | Normal-input witness and paired case |
| --- | --- |
| G171 | Caller static receiver reservation + default write; pair with a disjoint static, a read-only default and the default supplied explicitly. |
| Loan identity | One caller Loan carried through a copied reference, a holder join and a default result; pair with a new reborrow at another issue location. |
| Independent conflicts | One issued Loan meets two later independent accesses; neither is removed because the other is covered. |
| Candidate priority | A covered internal candidate first, a caller candidate later at one access; reorder independent declarations to reverse Loan IDs without changing the verdict. A covered clean candidate first and a taint-only one later inside a copy: today dropped, after M69-2 R4-M70's `Unsupported_Kd`. |
| Nested ownership | All six §4 rows, including equal syntax in separate copies and an externally issued activation reservation. |
| Guard ending | Guard candidate and `EndBorrow` in one copy versus crossing in and out; no invented Loan. |
| Generic substitution | Concrete and two-view generic copies, composed nested substitutions, PairAcquire Copy/Reborrow, instance-elided Drop. |
| Completion | P13-held template or caller, A1/A5 resource bound, CallRetention/destruction limitation, Opaque or failed template; an unrelated limitation never certifies an independent caller conflict. |
| R-a | Inspection Loan on a PendingSlot and a reserved argument temporary ends; a caller Loan in a copied reference and a later independent write remain. |
| Recursion | Recursive template call and its inline equivalent; each call's effects and retention stay at that site. |
| Stability | Rebind that changes a default or substitution, same-version reuse, reset; no stale scope or Loan identity. |
| Record identity | Two copies sharing a source node with different caller Loan anchors; inspect both pre-Add decisions and the contractually correct merged or separate records. |

Test owners: `MirBuildTest`, `MirBorrowsTest` (a new `.Defaults` partial for the ownership cases), `MirAnalysisTest`,
`GenericDefaultTest`, `ReceiverDefaultTest`, `DefaultAccessTest`, `CallReservationTest`, `MirEffectsTest`, plus the required
census/ledger selections. A synthetic validator or model test may pin a contract boundary but never satisfies a normal-source row.

Final diagnostics alone are insufficient: old analysis or `Unsupported_Kd` can hide an A5 miss. Preserve raw candidate facts, the
chosen owner and A5 records before publication, then compare final diagnostics with exact locations and requirements.

Compiler units run `scripts/verify.ps1 -Mode Unit` with validated selectors, architecture checks, affected allocation tests and the
census/ledger rows, then the Session. Builder changes also run the sel check; native fixtures only for an actual lowering change; no
NativeAOT. Associate evidence with `verify-commit.ps1` before each push and keep failures. This planning revision needs only
diff/reference review.

## 8. Size, performance and comparison criteria

Estimates for the remaining units, not measurements (M69-1's actual change was +10/-8):

- Production: `MirBody.Common` and the ownership rule with candidate routing +35 to +65; reporting deletion -12 to -22;
  guard/activation integration +10 to +20. Net about +23 to +73 lines, excluding behavior-preserving moves and documentation.
- Tests and evidence adapters: +180 to +300 lines in the existing owners, no duplicate framework. F5/F9 are outside this estimate.
- Runtime: no extra dataflow solve and no per-program table. An operation outside every copy adds one `Inlining` walk and no
  candidate walk; inside a copy, each conflicting clean candidate adds O(depth). The scan may now visit previously hidden
  candidates; that is required work, not a regression to disguise.
- Allocation: 0 B on warm repeated analysis; retained memory unchanged beyond R4-M84's measured Loan layout.
- Measure equivalent normal-source workloads at N/2N/4N defaults, nesting depth, same-root candidate fan-out, generic cases and
  copied-reference flows: time, allocation and retained memory against one baseline, timing loops in `src/Benchmark`.

Rejected until measured or disproved: an ancestor matrix or cache (retained memory and invalidation exceed a linear walk), and a
template/Subst/coverage registry (needs a counterexample to the simulation contract).

Compared with a recorder exception, this fixes first-candidate loss on all four failure paths. Compared with lifting default
effects to the final call, it keeps actual evaluation points and A4's ownership. Its risk is the simulation proof; if it fails,
compare a stronger shared representation instead of adding per-form exemptions.

Principles: one representation of issuance and one scope tree (One Concept); ownership decided at the comparison producer from
local facts (Local Reasoning); caller and template ownership separated explicitly (Explicit Semantics); normal-input
correspondence on the exact integrated revision (Verifiable Changes).

## 9. Comparison and review record

Three options were independently authored, one agent per option, and reviewed by a separate reviewer:

| Option | Assessment | Decision |
| --- | --- | --- |
| [1: prove the scope-only rule](../Sketches/A5%20Step%202/S3-M69/M69%20Scope%20Only%20Design.md) | G171 contradicts its premise; template-local examples cannot establish caller coverage. Useful normal-source/model bridge. | Reject as completion; keep its evidence procedure. |
| [2: pointwise caller effects](../Sketches/A5%20Step%202/S3-M69/M69%20Caller%20Effect%20Design.md) | Reuses CFG/A4 sites but still needs an ownership rule for mixed template/caller Loans. | Keep its F5 constraint; insufficient alone. |
| 3: producer-owned coverage | Issue location plus the scope tree decides before first-candidate selection on all four paths; no registry, replay graph or second solve. | Selected, gated by §4 and §6. |

The [review record](../Sketches/A5%20Step%202/S3-M69/Comparison%20and%20Review.md) lists the original corrections. Adoption
records the limited R4-M69/R4-M73 supersession in DECISIONS with its first behavior change and updates architecture/diagnostic
contracts and PLAN/STATUS only as their facts change. This plan edits no SPEC, SETTLED, frozen proposal or translation.

Authorities: [SPEC](../../docs/SPEC.md) §7.2.3, §8.15, §15.6.4, §15.6.7 and §23; [DECISIONS](../../docs/dev/DECISIONS.md)
R4-M68–M74 and R4-M84; [PLAN G171](../../docs/dev/PLAN.md); [A5 step 2](2026-10-18%20A5%20Step%202%20Implementation%20Plan.md)
2b-1/2b-D/2e-D/U11; [SETTLED](../../docs/SETTLED.md) (instance-evaluated generic defaults: inline copies, no registry);
[DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md); [VERIFICATION](../../docs/dev/VERIFICATION.md).

### Revision (2026-10-20)

The owner asked for the plan to be scrutinized against the Principles. Claims were checked at `338831f7`:

| Finding | Principle / rule | Correction |
| --- | --- | --- |
| §1 and §6 said the G171 source was missing and offered an unexecuted sketch whose qualified receiver and top-level forms fail Binding. | Verifiable Changes | §6 starts from the recovered, reviewed source and its observed chain; failed forms stay recorded failures. |
| §3 restated the representation after R4-M84 landed it; §6 planned an extraction that was not needed. | One canonical form (DECISIONS authoritative) | §3 points to R4-M84; M69-1 marked landed; §8 drops its estimate. |
| §5 put the scope-tree walk in A5. | One owning component | `MirBody.Common` beside `Inlining`, owner of the scope tree; the ownership rule in the new A5 partial. |
| §2 omitted `MirBorrows.Calls.cs` (owner of `held`) and `Flow.Placed`'s taint comparison, and gave R-a to code that does not exist. | Local Reasoning (census) | Rows added; R-a named as F9's future rule; `.Endings.cs` marked unrelated. |
| §5 kept the four-fact buffer after deleting its fourth fact; contract comments and R4-M73's sub-bullet were not in the deletion list. | Short (remove what becomes redundant) | Buffer sized by the landing group; comments and the superseded sub-bullet listed. |
| Candidates are per Loan and mode; the skip rule did not say both agree; copy-free operations paid the walk. | Explicit Semantics; Fast | Ownership is a function of the Loan and operation scopes; one `Inlining` check skips the walk outside copies. |
| A1/A2 keep a second mechanism (record-then-drop) for the same ownership concept. | One Concept, One Canonical Form | Named as the single-scope case; its removal is staged in PLAN by M69-2. |
| R3 (2b-3) may issue no ordinary Loan; the unaccepted 69-case model was not distinguished from M69-0 evidence. | Explicit Semantics; Verifiable Changes | §3/§4: 2b-D decides R3's Loan and scopes, and M69-2 lands no R3 path before it; §6 separates the model family. |

No compiler, build, test or model ran. The selection, gates and holds are unchanged.

Independent reviewer `review_m69_revision` (author of neither) checked every anchor and both sketches. Round 1's blocking findings
(`Work` counting the sketch could not do; §8's net, +23 to +73 as the original's round 2 had fixed) were corrected. Round 2's one
blocking finding, R3 stated as Loan-free before 2b-D decides, was corrected with the reviewer's wording, as were its wording points
(activations skip the walk outside copies, A1/A2's phrasing, the INTEGRATED bullet).
