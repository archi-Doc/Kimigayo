# M69 option 1: prove the current scope rule, or hold integration

Date: 2026-10-20
Baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8`
Status: unselected alternative; rejected as a complete M69 solution on the recorded evidence. Correction round 1: test-selector clarification.
Scope: plan only; no compiler, prototype, test or policy changes were executed.

## 1. Decision and general rule

A declaration-time default check may replace a caller-side check only when it
establishes that caller-side obligation under the same relevant assumptions.
The fact that an operation was copied from a default does not itself establish
that equivalence. A caller-created Loan is not present in the template's body.

This option retains R4-M69's current implementation: the recording owner adds an
`Inlined` fact from the access scope, and `MirReport.Add` drops that problem.
Its necessary premise is that every conflict involving caller state is detected
outside the copied scope. The option adds no per-Loan provenance representation,
no scope exception, no effect summary wrapper and no diagnostic post-filter.

PLAN G171 records a normal-input counterexample to that necessary premise.
Consequently this option cannot be the formal implementation plan for completing
F5/F9. Its useful deliverable is a small, reproducible evidence package which
closes the model-to-source question and preserves the existing integration hold.
Passing tests of template-local conflicts cannot override the counterexample.

## 2. Authority and decisions consulted

- SPEC 7.2.3, in `docs/spec/07-functions-and-callable-values.md`: defaults are
  checked at declaration and evaluated after explicit arguments, in order.
- SPEC 15.6, 15.6.3, 15.6.4 and 15.6.7, in
  `docs/spec/15-ownership-and-lifetime-analysis.md`: Loan issuance, carrying,
  static effects and reservations; equal Origin does not establish equal Loan.
- SPEC 23.1: Language failures, unimplemented valid forms and internal faults.
- DECISIONS R4-M68–M74: activation coverage, Inlined disposition, default taint,
  per-body view identity, F5 codes and held-view rules.
- `docs/SETTLED.md`, "Instance-evaluated generic defaults": preserve inline
  evaluation order, prepared-slot protection and cleanup; do not replace all
  defaults by evaluator functions to make this option work.
- CODEMAP's MIR construction and MIR analyses rows; ARCH's construction,
  Loan-stratum, effect-summary and MIR-problem owners and migration gates.
- A5 Step 2 plan 2b-D/2b-3 and 2e-D/2e-1–3; PLAN G171.

## 3. Verified code facts and root cause

| Owner/site | Observed behavior and consequence |
| --- | --- |
| `MirBuilder.Defaults.cs:Defaults` | Explicit inputs precede defaults. Shared inspection borrows are made in caller scope; nonrecursive templates are copied and recursive templates called. |
| `MirBuilder.Defaults.Copies.cs:Inline` | Each copy gets an Inlined root scope. `FoldInfo` changes Scope while preserving Syntax. Static Places are folded too; the template is not isolated from static storage. |
| `MirBody.cs:Inlining` | Returns the innermost enclosing Inlined scope, or -1. It is a location classification, not a proof of obligation equivalence. |
| `Core/SourceRef.cs:SourceInfo` | Carries syntax and Scope. `SourceRef` alone carries only module/node identity. |
| `MirBorrows.Loans.cs:Loans/Issue/Order` | Issues per MIR assignment location under one case; `loanAt` maps locations to renumbered Loan ids. `Loan.Site` keeps the subject only, with no issue Scope or issue MirLocation. |
| `MirBorrows.Flow.cs:Placed` | Adds temporary default taint when a default fills a caller PendingSlot. Inspection and inherited Loans are not interchangeable. |
| `MirBorrows.Candidates.cs:Consider` | Candidate identity is Loan index plus mode; the same Loan may be shared in its reservation holder and exclusive elsewhere. |
| `MirBorrows.Checks.cs:Check/Conflict` | Selects a conflicting candidate by actual check sets. `Accessing.Effect` is empty: the F5 effect conflict producer is not implemented here. |
| `MirBorrows.Activations.cs` | Checks reservations at calls/writebacks. M68 removes already-covered exclusive activation conflicts by semantic rule, not by later report suppression. |
| `MirBorrows.Reports.cs:Report` | Receives an anchor, losing candidate identity at its second overload; classifies Inlined from the failing access's Scope. A held view inside a copy still follows the Inlined drop. |
| `MirReport.cs:Add` | Drops any input bearing an Inlined fact before normal key merging. It neither has a Place nor proves template coverage. |
| `MirEffects.Keys.cs:Keys` | Publishes Call/Drop static keys; the existing boolean out parameter coalesces Unknown, Limit and Derived. It does not describe an enclosing default's whole evaluation window. |

Required meaning -> representation deficiency -> owner -> removal:
A copied default may affect a caller's live Loan -> access Scope does not tell
whether the template proved that conflict -> MirBorrows owns the conflict and
MirReport consumes the fact it is given -> retaining the current representation
removes nothing and cannot justify the required new effects. This is a failed
premise, not a missing branch for static syntax in the report adapter.

R4-M69 already records mutable static storage as a revisit trigger, previously
thought unreachable while explicit group-storage borrowing was rejected.
G171 records that implicit receiver acquisition reaches it. This invalidates the
unreachability argument without establishing production unsound acceptance.
No G171 source/evidence artifact was available in this checkout; the observation
here is the tracked PLAN record plus the code path, not a rerun of that evidence.

## 4. Small ordinary-input reproducer to establish first

The following is an **unexecuted proposed fixture**, not claimed accepted syntax
or recovered G171 evidence. First recover the recorded fixture if available;
otherwise minimize this shape using the existing compile helper:

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

The required shape is an implicit exclusive receiver acquisition on a mutable
static, followed by a default's copied call with a known write to that static,
then the receiver's activation. The final `touch` body need not write the static;
its summary therefore cannot replace checking the earlier default evaluation.
Do not change Binding or build synthetic MIR merely to force this shape.
If required operations become Opaque, record that exact blocker and retain it.
Unrelated acceptance/codegen refusals must be listed separately from raw MIR.

Required trace, in order:
1. Normal source parses/binds far enough to publish the required operations.
2. One case's receiver Ref issues a Loan at a caller MIR location outside the copy.
3. The same issued id survives stable renumbering into the relevant carrying set.
4. The copy's write key names the overlapping static and its scope is Inlined.
5. That Loan is live in the check set at the write; it is not a new reborrow merely
   sharing source text or referent.
6. The equivalent template body contains no such caller Loan.
7. The would-be F5 conflict would be dropped by scope-only disposition, whereas
   an independently SPEC-derived expectation requires it to remain.

A current raw production conflict cannot be claimed at step 7 while F5 is absent.
Capture lowering/A4 facts from actual compilation; compare a faithful prototype
projection separately. Name the missing production check explicitly.

## 5. What "same Loan" must mean in this evidence

Within a body/case analysis, compare `loanAt` after `Order`/`Renumber` with the Loan
carried in sets and selected in a Candidate. Retain the version, MirDefKey/body,
case, issue MirLocation, SourceInfo and enclosing copy chain in the evidence.
Numbers from different bodies, views or compilations are unrelated identities.
A syntax node may be copied twice, so SourceRef equality is insufficient.
A borrowed input may carry a pre-existing Loan; ending an inspection Loan must
not end that parent's Loan. Two modes of one candidate are not two Loans.

Use existing test helpers for public MIR/A4 facts. If private solver state is
needed, collect it using a disposable debugger/instrumented snapshot with source
hashes and the instrumentation diff; do not add a production trace/provenance
system or infer identity from formatted source. Such evidence is not a shipped
regression until a future chosen design exposes a proper observable contract.

## 6. Model evidence versus compiler integration

`draft/Sketches/A5 Step 2/records-model/Report.cs` accepts supplied symbolic rows
and applies current copied-Language disposition before held classification.
Its README explicitly excludes proving template ownership and M69 caller effects.
Its domain checks and permutation/negative controls prove the model's protocol,
not that Binding/MIR generates those inputs. Do not add expected output derived
from the model under test.

`draft/Sketches/A5 Step 0/A5RulesPrototype.cs` R19 creates explicit Borrow ids,
`Node.Inlined`, flows and inspection endings. Its `dc-p15`, `dc-reserved-inspect`,
`dc-reserved-bad`, J1/joined, raw and recursive cases are valuable obligations.
`R19InlinedDrop` is the alternative dropping default-tainted conflicts; it is
not a replacement name for M69's scope-only drop. R-a ends inspection Loans;
it does not repair caller-static effect suppression.

A valid projection maps every issue/access/copy/case needed by a model case to
actual MIR facts. If a hand-authored case cannot be produced, classify it as
invalid encoding, an actual unsupported boundary, or a genuinely unreachable
state with a proof. Do not manufacture a compiler path to rescue the model.

## 7. Units, prerequisites, checks and exits

| Unit | Concrete work | Required checks and exit |
| --- | --- | --- |
| O1-A: evidence intake | Recover G171 source/hash/configuration and trace, or minimize the proposed normal input. Inspect builder, A4 keys and baseline diagnostics. No product code changes. | Record all diagnostics/Unsupported sites, exact source and normal MIR. Exit with the shape proved or a named lowering blocker, never a model-only success. |
| O1-B: correspondence | Project the compiler's relevant locations, case, static key, issued Loan and carrying path into one isolated R19-style fixture. Add independent expected results and a deliberate scope-only drop negative control. | Full expected and actual rows plus mapping table; prove both missing caller Loan in template and survival to conflict. An unavailable trace remains an explicit unverified gate. |
| O1-C: bounded regression evidence | Extend the owning test classes for normal lowering/A4 facts and existing template-local behavior; preserve the failing future integration obligation in evidence, not a weakened current expectation. | `MirBuildTest`, `MirEffectsTest.KeysAreTheStaticFieldsASiteRuns`, `MirAnalysisTest.ADefaultsProblemsAreItsTemplates`; paired source variants below. No permanent failing test committed. |
| O1-D: decision | Compare with the independently designed alternatives. Record why the current premise fails and retain G171 before F5/F9 effect integration. | Independent review; no product release gate lifted by passing O1-A–C. If full scope-only rule remains requested, stop for owner choice between representation, complete whitelist or hold. |

Do O1-A before O1-B; the projection must follow actual input. O1-C follows the
verified facts only. Each compiler-test unit uses selected Unit verification and
a Session at session end under `scripts/verify.ps1`, then `verify-commit.ps1`.
Planning/document-only units use diff/reference review without builds or tests.
NativeAOT is not requested. No native fixture is needed for these analysis facts.

Paired cases: remove the write; write a distinct static; supply the default
explicitly; make the receiver local; use two omissions of one template; nest an
omission; use a recursive default template call; use an actual generic two-view
caller; remove the caller Loan. Preserve independent write/activation obligations
on one Loan, template-local errors, Limit records and held-view behavior.
R19's copied-reference, inspection-ending and J1 cases must not lose parent Loans
or independent errors. A1/A2's existing copied-template behavior remains covered.

## 8. Scope, size, performance and suitability

Affected investigation sites are the owners listed above and existing default,
MIR analysis, A4 key and Borrow tests; no new canonical component is proposed.
Existing `DefaultAccessTest`, `ReceiverDefaultTest.SharedCallsInspectPreparedSlots` (ReservedMethod data row),
`RecursiveDefaultTest`, `SemanticsDefaultTest` and `BorrowedDefaultTest` supply
normal-input anchors; they are not themselves proof of G171's complete chain.

Expected product change: +0/-0 LOC; no deletion. Test/evidence work is estimated
at +90–160/-0 test LOC plus a compact isolated fixture/mapping artifact. Estimates
are unmeasured. Product time, allocation and retained memory should be unchanged
because this option changes no product behavior; no speed claim is made.
A future revised implementation must measure those costs on equivalent inputs;
model collections and logical reset are not allocation/retention evidence.

This is the shortest code option only because it leaves the integration problem
unresolved. It preserves one owner and current behavior, but cannot meet Explicit
Semantics or Verifiable Changes for the new caller-effect path. A static-specific
exception, an Inlined boolean mode, blanket suppression, or accepting a narrow
subset would disguise the failed premise. No such patch is proposed.

Recommendation: reject option 1 as the formal complete solution; retain its
source-to-model evidence units as prerequisites of whichever sound alternative
wins. Keep current Unsupported/open-issue boundaries until that design's checks
pass. Do not claim closure of M69 or change SPEC/SETTLED to fit this option.
