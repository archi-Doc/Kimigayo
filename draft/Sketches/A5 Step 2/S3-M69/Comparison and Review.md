# S3 and M69: option comparison and independent review

Date: 2026-10-20. Source baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8`.
Scope: the owner's requested implementation plans, not compiler implementation or new language rules.
One agent authored each option after inspecting surrounding code; separate agents reviewed the candidates.
The planning session selected and revised the formal plans. Reviewers authored none of the candidate designs.

## Candidates and selection

| Problem / option | Author agent | Final document | Decision |
| --- | --- | --- | --- |
| S3 / 1 | `/root/s3_option1` | [Existing Binding owners](S3%20Owner%20Local%20Design.md) | Reject as completion: cannot publish the missing result/receiver contract within its local-only boundary. |
| S3 / 2 | `/root/s3_option2` | [Inner binders and substitution](S3%20Inner%20Binder%20Design.md) | Coherent but wider: additional imported atoms, inner Types, slot/substitution migration and lifecycle costs; same result-formation proof remains. |
| S3 / 3 | `/root/s3_option3` | [Formal S3 plan](../../../Proposals/2026-10-20%20S3%20Closure%20Origin%20Publication%20Plan.md) | Select a body-relative common view, preserving imported identities and using fresh formals only for result contracts. |
| M69 / 1 | `/root/m69_option1` | [Scope-only proof or hold](M69%20Scope%20Only%20Design.md) | Reject as completion: G171 contradicts its necessary premise. Incorporate its ordinary-source evidence bridge. |
| M69 / 2 | `/root/m69_option2` | [Pointwise caller effects](M69%20Caller%20Effect%20Design.md) | Insufficient alone: effects still need ownership. Incorporate existing CFG/A4 site checking; reject final-callee union/replay graph. |
| M69 / 3 | `/root/m69_option3` | [Formal M69 plan](../../../Proposals/2026-10-20%20M69%20Default%20Loan%20Correspondence%20Plan.md) | Select issue-context ownership at candidate selection, with existing scope ancestry and a universal correspondence gate. |

S3 option 3 removes repeated capture/result inference from consumers without cloning imported identities. M69 option 3 restores the
missing producer input before first-candidate selection, handles all current A5 failure paths, and adds no registry or second solve.
These directions best fit one canonical owner, explicit contracts, local reasoning and verifiable source-to-result correspondence.
Their expected size/performance advantage is structural, not measured. Compare complete equivalent work; the smaller incomplete
options' line counts are not competing complete implementations. Each plan states estimated additions/deletions, retained memory,
scaling dimensions and future checks; no timing or allocation estimate is presented as a result.

## Independent review and corrections

Independent reviewers: `/root/review_s3` and `/root/review_m69`. They inspected source and the three candidates for their problem,
started from the governing rule, considered smaller/general alternatives, and performed no compiler execution or candidate edits.
Initial review is r0; correction rounds are r1/r2. No candidate has more than two correction rounds.

| Review finding | Correction / resolution |
| --- | --- |
| S3 B1: options 2/3 omitted BU 0c's AB-2 merge barrier | r1 adds the branch dependency on 2c-3, same G:A5-F7 merge, pair-case checks and preserved A3-closure rejection. Separate verified commits are allowed; independent dev/main merges are not. |
| S3 B2: new signature/import atoms could reach old call readers prematurely | r1 makes C2 publication initially unconsumed; active signature/body Types stay coherent. First-consumer promotion coordinates Types, counts, substitution, call readers and retention; combine the unit if no behavior-preserving seam exists. Option 2 also requires actual inner-body checking, not merely a shadow projection. |
| S3 B3: selected draft activated 2c-6 before 2c-3 | r1 moves retention first, preserves 0c coordination, then activates closure-result F4 relations. |
| S3 Gate R was underspecified | r1 requires lossless conditioned source rows, a named common solver, independent body-side result binding, no-bound raw target/caller image, all variance/cycles/cases, failed-definition lifecycle and call-binding timing. These remain unexecuted blocking proof obligations, not assumed facts. |
| S3 citation/lifecycle cautions | r2 corrects method locations, limits stale-reference prohibition to live views/scratch, and explicitly preserves original source facts for legacy CallLocalOrigin through H11. No design change. |
| M69 C1: pre-Add survival does not prove distinct public records | r1 records the current `(code, subject, body)` merge/first-facts behavior. U11/common-publication identity or an explicitly justified existing merged expectation must pass before completion. No new copy ID or location relocation. |
| M69 C2: Keys boolean merges Unknown/Limit/Derived | r1 names 2b-1 class publication as prerequisite; no consumer reconstruction from the boolean. |
| M69 C3: SourceInfo may duplicate existing statement data | r1 selects the reviewer's smaller issuing-statement index, deriving SourceInfo from immutable MIR. All current Issue calls are Assign statements. Measure struct layout and lookup cost; no side table. |
| M69 source-selector clarification | r1 corrects `ReservedMethod` to the data row of `ReceiverDefaultTest.SharedCallsInspectPreparedSlots`. |
| M69 estimate arithmetic | r2 reconciles the independent addition/deletion ranges to net +23–73 production lines; still an unmeasured estimate. |

S3 r1 independently passed as a gated plan with B1/B2/B3 resolved; its nonblocking clarifications are the r2 edits above.
M69 r0 independently recommended option 3 plus the evidence/effect constraints of 1/2, with no blocking defect in the gated plan;
its clarifications and smaller representation are the r1 edits above. Final rechecks cover those edits and the intake record.

The option-local correction counts are S3 1: zero; S3 2: one; S3 3: two; M69 1/2: one each; M69 3: two.
A remaining blocking family after a second correction must go to the owner for representation/whitelist/hold, not be relabeled.

## What this review does and does not establish

Both selected plans are concrete gated work sequences. Their first units collect ordinary-source evidence and prove the proposed
common contract. Later units name owners, dependency/merge barriers, deletions, verification and completion conditions.

- S3 Gate R is not run or passed. The full result-inference/binding contract still needs its reviewed prototype before implementation.
- M69's template/copy simulation and lifecycle theorem is not proved. G171's precise normal-input chain is not newly reproduced.
  F5/F9 and record/publication prerequisites remain; current Unsupported/hold records do not prove the desired conflict survives.
- Applicable R4-M74 OQ2 family approvals, new vocabulary/exception approvals if needed, and first-implementation DECISIONS remain.
- SPEC, IMPL, SETTLED, frozen proposal text and verified support boundaries are unchanged. No default-pipeline acceptance is claimed.

Documentation verification is diff/reference review. This task runs no build, compiler, model, test, native check or benchmark.
`verify-commit.ps1` associates successful executable Verify inputs and has no documentation-only mode; do not manufacture such a run
or claim that association. The commit and this record identify the documentation evidence and unverified implementation obligations.

The formal intake and existing-rule mappings are in [INTEGRATED](../../../INTEGRATED.md). Baseline comparison authority is
[A5 step 2](../../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) with DECISIONS/SETTLED precedence.
