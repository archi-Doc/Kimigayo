# M69 option 2 — Pointwise caller-side default effects

Status: unselected standalone alternative; its pointwise integration constraint is incorporated in option 3. Correction round 1.
Baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8` (2026-10-20).
Scope: M69/G171 and their F5/F9 integration. No production edits or checks ran for this plan.

## 1. Conclusion and limits of this option

Publish and check a default's effects in the calling body's existing MIR control-flow graph,
at each operation that runs them. Do not union those effects into the final callee call.
`MirBuilder.Defaults.Copies` already supplies that ordered graph; `MirEffects.Keys` already
supplies the indirect effects of each Call/Drop. Reuse both rather than invent an effect schedule.

This is the strongest coherent interpretation of option 2, but it is **not a standalone M69
resolution**. The check still needs to distinguish a caller interaction from a duplicate of a
template-owned failure. That distinction requires Loan issuance/copy provenance and a proof of
template coverage, the core of option 3. Treat the work below as the effect-integration route
under the chosen ownership contract, not a second independent soundness solution.

The independent alternatives considered within this option are:

| Design | Result |
| --- | --- |
| Union omitted-default keys into the final callee's summary | Reject: evaluation order, intermediate liveness, initialization and reservation modes differ there. Explicitly supplied defaults would also need a different summary. |
| Build a separate ordered default-effect graph and replay caller Loans | Reject: duplicates the existing MIR CFG and A5 transfer/liveness semantics; no demonstrated missing fact justifies it. |
| Check each actual copied operation or recursive template call in the caller CFG | Choose as the integration route; requires the common M69 ownership contract before it can remove the hold. |

## 2. Required meaning and root cause

SPEC 7.2.3 checks a generic default at its declaration and evaluates it for each omitting call.
SPEC 15.6 identifies a default Loan by the omitting call, the default operation and Semantics
case; sharing a source span or a static Field does not identify one Loan.
SPEC 15.6.4 checks direct static accesses and each executed callable's effects against live
mutable-static Loans. SPEC 15.6.7 locates the receiver first, evaluates explicit arguments in
textual order and defaults in parameter order, then activates reservations.

Required meaning → the caller's Loan remains protected while the default runs → source-only
report suppression cannot establish that the declaration checked this particular conflict →
`MirBorrows` must decide ownership using the issued Loan and the evaluation context, while
`MirEffects` supplies only effects → no default-specific effect graph or final-call union is needed.

R4-M69 assumes every caller interaction is caught at a caller-scope prologue operation.
Its own mutable-static exception is now reachable according to PLAN G171 through implicit
receiver acquisition. `MirBorrows.Reports.Report` instead classifies by the access's nearest
Inlined scope alone, adds `MirFactKind.Inlined`, and `MirReport.Add` drops the record.
Neither operation establishes that the template has the Loan that made the conflict.

PLAN G171 records a normal-source static reservation meeting a known write in an inline
default, whose final callee has no such key. The exact source/evidence is absent from this
checkout: recover it or reproduce it before claiming an integration regression.
F5 is not implemented here and the normal path retains `Unsupported_Kd`; no production
wrong acceptance is claimed. The source below is an unexecuted probe, not a reproduced defect.

## 3. Inspected owners and blast radius

| Owner / file | Verified current fact and consequence |
| --- | --- |
| `Mir/Build/MirBuilder.Defaults.cs` | `Defaults` prepares shared slot borrows in caller scope and emits copies or `Fn` calls with `Arg > 0`; recursive omission components are finite template calls. Keep this selection. |
| `Mir/Build/MirBuilder.Defaults.Copies.cs` | `Inline` maps scopes/locals/blocks, folds Types and substitutions, retains operation syntax and gives copies distinct Inlined scopes. This is already an ordered evaluation schedule. |
| `Mir/MirBody.cs` | `Inlining(scope)` returns the nearest inline-copy scope; its Source is the omitting call. Nested scope ancestry exists, but no general template-coverage proof is published. |
| `Mir/Analysis/MirEffects.cs` | `Function` enters a recursive default template when `Arg > 0`; ordinary final-callee summaries enter only that callee. `Local` includes actual copied operations in the caller summary. |
| `Mir/Analysis/MirEffects.Keys.cs` | `Keys(body, view, block, out bool)` evaluates a Call/Drop's own inputs; its bool coalesces Unknown/Limit/Derived. Approved 2b-1 class publication precedes integration. Output buffer is reused; no per-default key cache is needed. |
| `Mir/Analysis/MirBorrows.Loans.cs` | `loanAt` identifies the issued Loan by MIR location; Loans are stably renumbered by group. `Loan.Site` is a diagnostic SourceRef and loses the issuing scope; it is not sufficient copy identity. |
| `Mir/Analysis/MirBorrows.Solve.cs`, `.Flow.cs` | Replay plans a location on its incoming state, checks it and commits transfer/endings. Preserve this order. |
| `Mir/Analysis/MirBorrows.Checks.cs`, `.CheckSets.cs`, `.Activations.cs` | Ordinary accesses, live/taken/stored closures and activations have different checking rules. An effect query must use F5's approved check set, not reuse an arbitrary direct-write or activation set. |
| `Mir/Analysis/MirBorrows.Endings.cs` | Destruction endings are published per location; static roots are not ended. R-a is a separate F9 ending of inspection Loans, not of copied caller Loans. |
| `Mir/Analysis/MirBorrows.Reports.cs`, `MirReport.cs` | The current scope-only Inlined fact is consumed unconditionally. The ownership decision belongs before presentation, not in another report-deduplication pass. |

Shared-cause search: `Inlined`, `Inlining`, `Keys`, `DefaultArgument`, `loanAt`, `DefaultTemplate`
across the named owners, DECISIONS and tests. A1 and A2 also produce Inlined facts; audit them
under the selected shared ownership contract, but change them only if their template proof fails.
Do not mechanically remove their valid once-at-declaration checks.

Tests inspected: `MirEffectsTest.KeysAreTheStaticFieldsASiteRuns`, `KeysKeepTheEvaluationsLinear`,
`MirBorrowsTest.ADefaultsTaintMakesItsConflictsUnsupported`, its inline/held cases,
`ReceiverDefaultTest.SharedCallsInspectPreparedSlots`, `InspectionsEndBeforeTheCalleeAndCleanup`,
`RecursiveDefaultTest.RecursiveReferenceResultKeepsTheCallersLoan`, `DefaultAccessTest`.
The R19 model and findings are in `draft/Sketches/A5 Step 0/A5RulesPrototype.cs` and `prototype.md`.
R19's `Err` and effect reporting still drop on `Node.InScope`; their agreement is not independence.

## 4. Concrete design

### 4.1 Effect publication remains A4's

At a copied Call/Drop, query the existing `MirEffects.Keys` using the actual caller body, view
and block. At a recursive template Call, the same query composes the template summary under
its call substitution. Keep the known/unknown/limit/derived distinctions required by 2b-D/2b-1.
Do not store the returned span: its next query overwrites it.

Direct copied static accesses remain ordinary `MirUses` accesses with their full Place and
projection precision. Do not turn them into coarse whole-Field summary accesses: that can
reject disjoint direct operations the Place rule accepts. Effects of calls retain P5 Field keys.
Static initialization and cleanup stay the existing Call/Drop operations and A4 rules.

### 4.2 Caller checking uses the actual point

F5's canonical `MirBorrows.Effects` hook runs in the existing check replay at each Call/Drop.
It reads the solved incoming state and F5's approved W6/call check set, including the actual
reservation modes there. A copied call is not checked again at the final omitting call.
Do not activate the final receiver early, end a Loan early, or pull later-default effects earlier.
Do not flatten conditional branches, loops, check-only paths or noncompletion into a union.

The final callee still has its own effect and activation checks. An activation failure and
an effect failure on the same Loan remain independent failures (2b-D/2b-3).
A4's body summary may contain both actual copies and the callee as it does today; that summary
is for callers of this body and does not replace checks within this body's evaluation.

### 4.3 Necessary ownership contract; not supplied by effects

The same provenance decision must cover ordinary direct conflicts, Call/Drop effects and
activation/destruction conflicts. It receives the actual issued Loan identity and access
location before `MirReport`; it must not infer identity from SourceRef or Field equality.

For a conflict with a Loan issued outside an inline copy, the inner template alone cannot
justify dropping it. For a nested copy, walk outward to the context containing the required
Loan and access facts: an outer template may own the failure, but a top-level caller Loan
requires a caller-owned record. A scope match is only a necessary condition, not a complete
proof of template correspondence under substitution, PreparedInput mapping and Semantics cases.

A model of this contract must handle a local duplicate encountered before a caller-owned
conflict at the same access. Selecting the first candidate and then dropping it must not hide
the later independent candidate. Decide evidence ownership during conflict selection, not
through a second diagnostic sweep. The selected M69 design owns the exact representation.

Minimal plausible input is the issuance `SourceInfo.Scope` retained beside each Loan, plus
existing body/view/location and scope ancestry. Confirm that this actually suffices before
adding it. If template/copy substitution correspondence needs a producer-published fact, that
is a shared-representation decision with owner approval, not an optional reporting parameter.
Do not introduce a table used only to rescue static defaults.

Caller-owned failures retain the ordinary Language requirement/code and documented access
location and typed evidence. If final diagnostics should move a copied access to its omitting
call, obtain an explicit DIAGNOSTICS design decision first; this option does not silently move it.
Held views and genuine limits follow their existing contracts after ownership is established.
Missing proof is a located `Unsupported_Kd` only for an unimplemented analysis boundary, never
an invented conservative Language error and never successful acceptance.

### 4.4 Template responsibilities and deletion

F9 continues to check PreparedInput Move/Access/Borrow rules in template bodies. R-a ends only
inspection Loans formed by that omitting call's defaults, on pending slots and reserved temps;
a caller Loan copied into a default result is retained. Raw-pointer copying keeps raw authority.

Delete the unconditional A5 inference "access inside copy implies template-owned" when its
replacement proves coverage. Do not credit this as an option-2-only deletion: every sound M69
solution needs it. Delete no A4 key solver, builder copy path, template checker or direct Place
check. Remove R4-M70's taint only with the separately verified F9 units, not to make this plan pass.

SETTLED "Instance-evaluated generic defaults" is respected: no instance evaluator or second
interpretation context is introduced. No reason to reopen that decision has been established.

## 5. Normal-input bridge and reproducible evidence

Proposed unexecuted G171 probe (refine only after inspecting its actual selected operations):

```kimi
struct Cell
    public var value: i32 = 0
    public func touch(self: uniq/Self, amount: i32 = State.write()) => ()
group State
    public var cell: Cell = Cell.init()
    public func write() -> i32
        cell.value = 1
        return 0
State.cell.touch()
```

Expected hypothesis: implicit receiver acquisition reserves `State.cell` before the omitted
default invokes `State.write`; the write key is `cell`; `touch` itself has no static write key.
The explicit twin `State.cell.touch(0)` omits that default evaluation. A read-only default,
a write to a distinct Field, and removal of the reserved receiver provide rule-specific twins.
If Binding rejects the probe or emits a different chain, record that result; do not hand-edit
MIR to claim source reachability. Recover G171's original source or construct another normal form.

For each bridge case save the source/configuration hash and compiler revision, Binding outcome,
validated MIR, body/view, exact issue location, copy-scope chain, reservation holder/activation,
access location, key/class query result, and the live/taken/stored path carrying that same Loan.
Translate these facts into the model's inputs, not a hand-selected result. Model Loan names map
to `(body, case, MIR issue site including copy context)`; dense IDs may be renumbered.

Compare pre-disposition conflicts and post-disposition records independently. An old-path error,
body hold or unrelated Unsupported record is not evidence that the target conflict survived.
R19's 47 cases and existing source tests supply starting points, not blanket integration proof.

## 6. Small implementation units and exit gates

| Unit | Work, prerequisites and required exit |
| --- | --- |
| E0 — Source bridge | Recover/reproduce G171 and at least one R19 copied-caller-Loan chain. Extend existing MIR/borrow tests with source-to-site assertions; preserve baseline failure/hold evidence. No rule change. Exit: identities and effect timing established or concrete blocker recorded. |
| E1 — Shared design gate | Compare the independent ownership options; review the chosen M69 contract plus E0. Obtain owner approval for new vocabulary/shared representation and 2b-D/2e-D package changes. Exit: nested copies, candidate selection, held views and location ownership have one rule, no blocking finding. |
| E2 — Restructure if needed | Before a behavior change, split by an actual responsibility only if current structure requires it; no split is needed merely because a historical plan named one. Compare diagnostic/semantic snapshots and affected IR on identical inputs per VERIFICATION. Commit separately. |
| E3 — Ownership prerequisite | Implement the chosen common M69 ownership contract in Loans/check selection/reports, with A1/A2 audit. Add direct-access, nested and first-duplicate/later-independent regressions. No claim that effects alone did this work. Exit: a caller conflict survives without duplicating template failures. |
| E4 — Pointwise effects | After approved 2b-D and 2b-1, integrate F5 once at actual Call/Drop sites, using A4's query. Add inline/default-template-call rows to `MirEffectsTest` and `MirBorrowsTest.Effects`. Exit: G171 pre/post-disposition behavior and order twins agree with SPEC; no copied final-call union. |
| E5 — F9 interaction | After approved 2e-D and its PreparedInput rules, verify R-a/P9 against the same bridge. Delete temporary taint only in its owning F9 unit. Exit: copies retain caller Loans while inspection Loans end before final activation, including recursive and reserved-temp forms. |
| E6 — Completion | Independent final review, focused Unit checks and end-of-session Session; save measurements and source-associated bridge/model evidence. Close only the verified M69/G171 boundary, not unrelated G160/P13/Transferable work. |

E0 is executable preparation, not this documentation task. At baseline step 1 and G148 are
landed, while F5 and 2e-D integration are still gated. Respect the live PLAN on the implementation
branch and serialize each unit's editing/builds/verification/commit. No NativeAOT runs are implied.

## 7. Required checks and measurements

Use the owning tests and shared helpers; do not add an independent compiler or duplicate model
interpreter. Required matrix: known read/write, unknown effect, limit, derived effect; mutable
versus immutable static; implicit reservation versus already-live borrow; copy versus recursive
template call; nested copies; same default called twice; two Semantics cases; direct Place
projection versus call key; template-local duplicate and independent caller conflict; last use
before an effect versus use after it; noncompletion/cleanup ordering; raw and borrowed inputs.

R19 ending twins: pending-slot and reserved-temp inspection Loans both end; a returned copied
reference still protects its original storage; j1/ joined/copied keep independent failures.
Observe the same Loan at issue, carries, check and disposition. Test two different conflicts
on the same Loan so deduplication cannot accidentally mean "one report per Loan".

Focused selections include `MirBuildTest`, affected `MirBorrowsTest` methods, `MirEffectsTest`,
`DefaultAccessTest`, `ReceiverDefaultTest`, `RecursiveDefaultTest`, `MirAnalysisTest` and
`MirCensusTest`, plus the selected model checks and ledger required by the applicable F5/F9 units.
Use `scripts/verify.ps1` with verified selectors; Session covers final unchanged Unit inputs.
Builder changes, if E1 requires any, also take the prescribed MIR-codegen selection. Native
fixtures are only for changed runtime behavior, explicitly selected; no emitter change is planned.
Associate implementation evidence using `verify-commit.ps1` before each push. This plan itself
requires only reference/diff review, not compilation.

Size hypotheses, excluding common already-planned F5/F9 work: +20–60 production lines for
wiring/contract assertions and 150–240 test lines; −0 production lines attributable only to
option 2. Shared provenance may cost +40–120/−15–35, but its chosen design must estimate that
separately. A separate schedule would add at least one region/event storage and traversal path
with no demonstrated deletion; reject it before implementation.

Time hypothesis: no extra CFG traversal; effect work stays F5's sites × relevant static Loans
plus key enumeration. Zero effect work for bodies/views with no mutable-static Loans. Caller
copies already exist, so no extra code-generation work or native O2 allocation is expected.
Allocation hypothesis: zero warm allocations using existing buffers; no additional retained
summary per default/call. If issuance scope enlarges Loan, measure actual layout and capacities
rather than assume four bytes. Record time, allocated bytes and retained memory at 1×/2×/4×
call sites, static keys, nested copies and recursive omission components under fixed conditions.
Keep failed evidence, all existing allocation assertions and bounds; do not relax a ratchet.

## 8. Selection recommendation

Adopt pointwise evaluation as a constraint on every solution. Do not select "publish more
effects at the final caller call" as the formal M69 fix: its proposed summary is insufficient,
and the correct pointwise form is largely the already-planned F5 route. Prefer the ownership
proposal that proves template coverage with the smallest common representation, then include
E0/E4/E5 as its mandatory bridge and integration checks. This conclusion is based on code and
contracts; performance estimates and the proposed source probe remain unverified.
