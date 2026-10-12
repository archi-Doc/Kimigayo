# R4 Completion Plan and Budget

Draft requested by the owner on 2026-10-13. This document proposes the remaining R4 scope, order and budget; it does not
approve a representation, change a cap, grant a Binding exception or claim implementation support. The owner authorized
including cap revision among the options. The separate M39 required-access/capability redesign decision remains pending.

The adopted [compiler reduction plan](2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md) remains the
stage plan. Its integrated text is unchanged. [DECISIONS](../../docs/dev/DECISIONS.md), especially R4-M24 through R4-M30,
governs over historical estimates and handoff notes. All sizes below are non-trivia C# tokens under the A1 counter.

## 1. Decision and evidence

Recommend option C below: complete the original R4 shadow-analysis scope with proposed caps of **Mir 143,000, Binding
405,000, Core 14,000 and total 981,000**. These fund the costed uncertainty and restructuring peak; they are not a target
implementation size. The reference central exit estimate is **926,146** total. No cap has been changed. Do not
choose a smaller incomplete representation merely to satisfy a cap. Further consolidation remains required within the
chosen coherent design; the current studies do not demonstrate that every possible smaller design is impossible.

The current dev compiler has A1-A4 and A5-1, the difference harness and T1. U8 is preserved locally, including the intrinsic
contract correction, but is not ready to land. The complete retained-instance/call correspondence and source-publication
contracts are missing. Independent U7b and U9 designs exposed additional work omitted by the earlier 85,900/89,400 forecast.
That forecast is superseded as a planning estimate, not as an owner-approved cap.

Evidence at `ddcab448` (compiler inputs identical to the measured `e28e2eed` and verified `b141939a`):

| Area | Measured dev | Current cap | Available headroom |
| --- | ---: | ---: | ---: |
| Mir | 70,906 | 93,651 | 22,745 |
| Binding | 375,060 | 377,051 | 1,991 |
| Core | 8,166 | 8,187 | 21 |
| Other compiler areas combined | 416,914 | Separate area caps | No planned growth |
| Total | 871,046 | 894,362 | 23,316 |

The measured frozen U8 branch/WIP adds 5,320 Mir tokens and zero Binding tokens. This is an actual unlanded delta, not an
approved design or an additional 5,320-token allowance for its replacement. Subsequent estimates start from that exact WIP
and include their replacement deletions once. No parked U7b or R6 deletion is counted as a saving.

Current evidence: successful legacy Session `20261012-154908-322-session-r4-resume-20261013-access` (whole solution, 20,792
tests); V13 `r4-resume-20261013-v13-access` (identical ledger-on/off outcomes, the same known fixed-array MIR failure);
P9 **143**, open (b) **47**. U8 feedback's unclassified 167 is a different, unlanded tree and is not the dev baseline.
The area measurement, exact WIP delta, designs and reproducers are retained under `artifacts/benchmarks/` and
`artifacts/handoff/r4/`; the tables here retain the decision inputs in Git.

## 2. Complete remaining scope and ownership

Required semantic facts remain distinct: physical storage, retained value dependencies, access authority, genuine proof
premises/requirements and transitive effects. Shared operation correspondence can serve their consumers without merging
their meanings. Binding decides and publishes source/selection facts; the builder translates them; analyses consume
validated MIR and published views. MirValidator is the single MIR validator in every configuration.

The budget and order below are conditional on closing these design gates:

1. Complete ordinary, phantom, escaping and weak-store retained-instance transfers, kill/join rules and a finite sparse
   representation. The reproduced repeated-site parent/child failure rules out blanket self-authority by static Loan ID.
2. Publish complete capture/result/receiver occurrences, erasure and fixed-Function target contracts and the selected Callable
   signature. Keep declaration/WF premises distinct from obligations; preserve old readers under the R6 freeze.
3. Publish every committed fit/clause/WF requirement, including successful, immediate-error, unreachable and no-operation
   forms. Decide source-chain eligibility before actual retention, without a second variance/meet/proof implementation.
4. Establish a finite recursive boundary/result/effect domain and witnesses that do not assume their own unverified bound.
   Include hidden abstract regions, through-reference replacement, caller conflicts and earlier-result exclusions.
5. Close producer and diagnostic contracts across all sibling forms, including M39 access/capability/rejection publication,
   Slice handle versus backing Origin, safe-range Loans, ReferenceRead, cleanup order and Binding-rejected bound children.

Current A5 liveness reads A2 and structural `MirTypes.Observes`, not solved A4 summaries. A possible schedule is Binding facts
→ A1/A2 → Origin formation/chain judgments → eligible retained contributions → boundary export → effect closure → witness
and caller checks. This avoids assuming a liveness/effect cycle; it does not supply the still-missing finite algorithms.

Scope dispositions follow current decisions rather than old placeholders:

| Item | R4 disposition |
| --- | --- |
| U10 old-body test reads | R4-M13a replaces the old 110 target: 183 currently, approximately 174 after T2; retain the 150 R5 and 24 R6 residual reads until their real observables exist. Ratchet to measured counts. |
| Published Kimi-body versus published-row verification | R6 under R4-M28; callers still use the published row. No new deferral and no R4 deletion credit. Optional promotion costs about Binding 200 + Mir 1,100 central, outside this proposal's requested scope. |
| Generic library operations outside the published table | Preserve X2 and classify by its SPEC rule. Do not invent new published rows to hide differences; any rejected library/milestone still triggers O3. |
| Abstract composite comparison and tuple relational floating part | Fund complete R4 producer/consumer corrections. A remaining general-rule Limit requires O4, not automatic deferral. |
| Selected Callable signature | C8 funds the selected identity, substitution and complete consumer contract once. |
| Rejected nodes, invalid default jumps and Opaque-dependent completion | E4/E5 publish independently bound parts and causes; no blanket Opaque-body suppression or new Unsupported for an already rejected operation. Required M39 infrastructure comes forward. |
| Public diagnostic switch and construction-record merge; TransferRequired relocation | R6 under R4-M3a/M4/M17. R4's internal reporter keeps shadow acceptance/publication; M39 publishes its local typed outcome without moving all ownership checks. |
| Origin-only duplicate generic entries; definition capability check; full backend/native parity | R5 owns these existing deferrals. No frozen old-Emission feature work or advance savings. |
| FixedArrayFill Aggregate failure and remaining ledger causes | Reproduce and repair the owning cause, or obtain the required explicit boundary disposition; a failing MIR Session cannot close U12. E9 funds unnamed non-A5 causes. |
| Whole-drop-receiver wording; debug-only scratch/intermittent checks | Retain the pre-existing diagnostic review/R6 item and unverified investigation status. A reproduced required-check failure is resolved before closure; no cause is assumed from a hypothesis. |

## 3. Budget model

The completed estimate table and options follow the independent common, producer and exit scope audits. Estimates include
rules, producer/consumer interfaces, ordering, reset/storage/limits, validator, inspection and driver changes. Test and
benchmark effort is scheduled even where it adds no compiler tokens. Engineering low/high ranges are not proofs or measured
confidence intervals. A missing algorithm is a release gate, even if its implementation work has an allowance.


Reserve is applied to **positive net** design rows: x1.5 for established owners, x2 for new or unresolved shared components.
Measured WIP is not multiplied. Central net includes identified deletions once; those credits are not subtracted again.
The factors apply to Binding and Core as well as Mir. Their historical purpose is to cover underestimated interfaces and
neighbor work; they do not prove an upper bound. Do not multiply the high scenario by the factors and call it this same budget.

| Scope (counted once) | Mir central | Binding central | Core central | Factor | Reserved total delta |
| --- | ---: | ---: | ---: | --- | ---: |
| Frozen U8 (measured) | 5,320 | 0 | 0 | Actual | 5,320 |
| C1 Requirements, positions and genuine premises | 550 | 1,950 | 0 | 1.5 | 3,750 |
| C2 U7b capture/result/receiver/erasure contract | 650 | 3,450 | 0 | 2 | 8,200 |
| C3 Retained transfers and complete U8b | 6,000 | 0 | 0 | 2 | 12,000 |
| C4 U9 source-chain eligibility and judgment | 3,500 | 0 | 0 | 1.5 | 5,250 |
| C5 Boundary results and rooted recursive effects | 4,600 | 850 | 0 | 2 | 10,900 |
| C6 U9 witnesses and U8c caller/hidden-region checks | 2,300 | 0 | 0 | 1.5 | 3,450 |
| C7 A5 lifecycle, driver and inspection | 800 | 350 | 0 | 1.5 | 1,725 |
| C8 Selected Callable signature | 750 | 350 | 0 | 1.5 | 1,650 |
| U8a Secured results and cleanup order/subjects | 900 | 0 | 0 | 1.5 | 1,350 |
| ReferenceRead shared Copy boundary | 350 | 0 | 0 | 1.5 | 525 |
| U8s Safe range acquisition/construction | 900 | 180 | 0 | 2 | 2,160 |
| U8p Place/backing dependency, existing-owner route | 0 | 650 | 0 | 1.5 | 975 |
| U8i/M39 Complete outcomes including correlated cases | 1,900 | 4,100 | 0 | 2 | 12,000 |
| U10 Test migration | 0 | 0 | 0 | No compiler change | 0 |
| U11 Facts, derived/default policy, rejected parts, adapter (E2-E8) | 5,250 | 1,150 | 2,380 | Per concern | 16,970 |
| Non-A5 ledger repair allowance (E9) | 3,000 | 650 | 100 | 2 | 7,500 |
| Abstract/tuple comparison limits | 1,320 | 450 | 0 | 2 / 1.5 | 3,355 |
| U12 Driver structure and measurement entry (E10) | 300 | 0 | 100 | 1.5 | 600 |

C1-C8 replace the old separate U7b/U8b/U8c/U9 placeholders. C3 includes the complete through-reference replacement; C5 owns
shared result/effect summaries; C6 owns the remaining caller checks. Selected Callable publication is C8 only (x1.5).
C7 stores A5 semantic witnesses; U11 integrates reports and migrates A1-A4 facts. E4/E5 owns common rejected/undecided causes
and independently bound children; M39 owns its distinct capability failure fact. U8a owns cleanup CFG/source causes.
U8s owns its construction-to-transfer adapter; C3 owns the general transfer engine. No overlap discount is assumed just
because files are shared.

The full scope includes correlated per-case M39 selection. The smaller uniform-only route would save 3,000 central / 6,000
reserved Mir+Binding tokens but leave a valid general family Unsupported; it is **not** the recommended full-scope option
without a separate O4 decision. This is a costed workload, not a third M39 representation design.

The full engineering scenarios, preserving frozen readers, are:

| Area | Low | Central | High | Central with factors |
| --- | ---: | ---: | ---: | ---: |
| Mir | 97,456 | 109,296 | 129,526 | 137,131 |
| Binding | 383,620 | 389,190 | 398,460 | 401,495 |
| Core | 9,516 | 10,746 | 12,446 | 13,186 |
| Other areas | 416,914 | 416,914 | 416,914 | 416,914 |
| **Total** | **907,506** | **926,146** | **957,346** | **968,726** |

Even the low engineering scenario exceeds the current total ceiling by 13,144; that is not a mathematical lower bound.
Central growth from dev is 55,100, including measured U8. Remaining unmeasured central work is 49,780; its factor-adjusted
allowance is 92,360. The 90,000 Mir gate cannot be satisfied by area rebalance under these estimates.

### Known contingencies and restructuring peaks

The following priced alternatives form an envelope; they do not select a representation or authorize a frozen-path edit.

| Additional exposure over the full reference route | Reserved Mir | Reserved Binding | Other reserved areas | Total |
| --- | ---: | ---: | ---: | ---: |
| Deeper retained-transfer replacement R instead of preserving M24 machinery P | 1,200 | 0 | 0 | 1,200 |
| U8p shared selected-dependency publication if existing facts prove insufficient | 600 | 1,800 | 0 | 2,400 |
| Frozen-reader migration exposure across common contracts, M39 and U8p, including correlated cases | 0 | 0 | Analysis 1,020; Emission 780 | 1,800 |
| **Known-risk net envelope** | **138,931** | **403,295** | **Core 13,186; other areas 418,714** | **974,126** |

The positive Analysis/Emission exposure is funded risk, **not a selected route or D5 waiver**. The implementation design
must preserve existing identities/readers, show an already-permitted migration/deletion, or obtain a separate owner decision.
If the full scope cannot be represented under those conditions, cap approval alone cannot release it. U8p's extra publication
is likewise conditional, and no unrelated deletion is credited.

P's common Mir central estimate is 19,150; deeper replacement R is 19,750. P adds 23,450 and deletes 4,300; R adds 25,750
and deletes 6,000. Neither has closed the semantic gates. The earlier 9,140 sketch omitted full U8b/witness maintenance,
remaining caller checks and complete publication; it is replaced, not added. There is no demonstrated cheaper complete design.

For a conservative permitted schedule, remove common-transfer/producer superseded paths before late U11/U12 work, including
deletions belonging to the E4/E5 prerequisite. Then reserve late exit coexistence before its own deletions: Binding 895,
Mir 3,165 and Core 475, total **4,535** above the envelope. This intentionally still counts E4/E5's deletion allowance in that
late bound. Projected peak: **Mir 142,096; Binding 404,190; Core 13,661; total 978,661**. Rounded area caps plus the funded
other-area exposure total 980,714; round the stage proposal to **981,000**.

This peak is an additional source-budget check, not another exit-growth estimate. Earlier common R and producer peaks fit
this packet order; retaining those old paths through late exit would invalidate it. Final designs must verify actual
per-commit peaks, including any new bridge, before implementation and again before landing. Arbitrary coexistence is not funded.

The non-A5 repair allowance is 3,750 central / 7,500 reserved. It covers unpriced remaining ledger causes and newly visible
full-record differences, not named U8/U9 work a second time. P9 signatures are not counts of defects. A new cause outside this
inventory, exhaustion of its allowance or failure of a semantic gate triggers re-estimation; no finite budget guarantees closure.
Test/harness work (roughly 6,000-14,000 additional test tokens for exit work) and Benchmark work are scheduled, not compiler
size savings. No elapsed-time completion date is claimed before the algorithms are settled.

## 4. Delivery order and verification

| Wave | Work and landing constraint | Safe parallel work |
| --- | --- | --- |
| 0 | Decide the budget direction and M39 direction; finish independent shared-contract alternatives, compare them and review the selected design before code. | Read-only designs, reproducer analysis and test inventory for disjoint concerns. |
| 1 | Complete U8's producer prerequisites and the common retained-instance/contract foundation, including U8b transfer/authority wherever U8 relies on it. Bring U11's shared rejected/undecided-operation cause publication (E4/E5) forward for M39; its required producer/consumer migrations land together. An indispensable U7b producer precedes its U8 consumer. | Independent producer implementations in dedicated worktrees after their designs and Binding exceptions pass. |
| 2 | Finish and land U8, with self-review and two independent final reviewers. Finish the remainder of U7b against the same contract. | U10 migration preparation; U8a cleanup design where it does not assume unfinished transfers. |
| 3 | Complete U8a and the remaining U8c/U9 consumers and U8b acceptance work on the common producer. No U8b transfer needed by U8 is deferred here. Source-chain eligibility precedes dependent retention; effect witnesses precede caller exclusions. | Disjoint consumer files only after their shared inputs are stable; serialize MirBorrows/MirEffects/driver writers. |
| 4 | Complete U10 and the remaining U11 report/fidelity work, then repair classified (b) causes through their owning units. U11's shared publication design/review and prerequisite implementation occurred in wave 1. | Independent reviews and disjoint test/diagnostic consumers; no parallel writers on shared report or builder files. |
| 5 | U12 exit census, ledger closure, allocation/scaling and timing, documentation/ratchets, final Sessions and push. | Read-only evidence review; final landing/verification order remains serialized. |

This advances contract design and the necessary U11 cause publication before the historical U8-then-U7b order because U8
cannot reconstruct facts belonging to those producers or U9. It does not permit a partially sound U8 landing. A selected
design must identify coherent producer/consumer units, deletion
credits and exact dependencies before releasing implementers. Do not split one cause into 200-token Binding fragments.

Every unit follows design → reviewed decision → implementation in its own worktree → self-review → independent review
(two reviewers for a new analysis or roughly more than 3,000 tokens) → disposition of findings → landing signal → final rebase
→ `land-unit.sh --dry-run` and formal per-commit checks → successful `verify-commit` → fast-forward and push. Behavior-preserving
restructuring and behavior changes remain separate commits. Each intermediate commit must meet the effective caps; a later
deletion is not permission to exceed them. Resolve a temporary representation's removal before landing it.

Feedback stays incremental: related rule tests, valid/invalid siblings, fail-before defect evidence, relevant allocation/scaling,
and one MIR Functional ledger run when analysis results change. Formal selection includes Verify's size/architecture checks,
the affected Unit rows, V3 for shared Binding/old-input changes, V12 only for affected MIR dumps, and V15 classification.
V13 accompanies harness changes and the session-end comparison. V16 includes repeated-site, weak/escape, sparse and recursive
shapes; analysis work beyond input plus output is a defect. Preserve warm zero-allocation checks and native O2/allocation
guarantees. Select relevant native fixtures explicitly; NativeAOT is not requested.

Each completed work session has successful Session evidence and reports P9 and open (b). Reuse unchanged-input evidence
under the established manifest rules rather than repeat heavy checks for a documentation-only hash. A failing required check
prevents completion and push. Report P9 stagnation under O5; a planning checkpoint does not count as a new analysis result.

## 5. Alternatives and owner decisions

| Option | Budget and consequence | Assessment |
| --- | --- | --- |
| A. Keep current limits | Mir 93,651; Binding 377,051; Core 8,187; total 894,362; Mir planning gate 90,000. Continue design research without releasing above-limit code. | No complete compliant design has been demonstrated. One more bounded consolidation study may find one; it cannot be promised from current estimates. |
| B. Fund the net completion envelope | Mir 139,000; Binding 404,000; Core 14,000; total 976,000. Covers the 974,126 known-risk net envelope. | Requires a narrower, proven sequence with no unfunded intermediate coexistence. Current conservative late-exit peak does not fit its Mir/Binding caps. |
| **C. Fund completion and the priced restructuring peak** | **Mir 143,000; Binding 405,000; Core 14,000; total 981,000.** Covers the 978,661 peak and rounded per-area limits. | **Recommended planning budget.** Preserves all costed semantics and the restructuring-before-behavior rule; still requires complete designs and actual measurements. |

Option C raises the current caps by Mir 49,349, Binding 27,949, Core 5,813 and stage total 86,638 (9.69%). All other area caps
and the LLVM-template line cap remain unchanged. Individual area headroom cannot all be spent on top of the stage ceiling.
The cap is a guard, not permission to spend its remainder on unrelated work or to retain duplicated paths.

Reducing scope by dropping through-reference replacement or preserves witnesses is rejected by R4-M25 and is not a cheaper
completion option. Deferring non-gating diagnostic text alone would not supply the missing typed semantic facts or make the
current estimates fit. Moving code out of counted compiler areas, borrowing R6 deletions, selecting a static-Loan shortcut,
or omitting interfaces/inspection/limits is not a valid saving. Keep the smaller common P direction as a comparison candidate;
select P or R only after the independent designs establish the required contracts and performance.

Owner decisions needed to release subsequent work:

1. **Numeric budget/policy:** choose A, B or C. Approval of B/C would supersede R4-M25's 90,000 planning trigger for this priced
   scope with the approved whole-plan area/stage envelope. Reforecast at each selected design and landing; an envelope overrun
   requires consolidation or renewed owner approval. The existing per-unit 20% overrun rule stays. Implement the approved
   cap change as its own reviewed policy unit, with DECISIONS/architecture/test updates and the exact owner approval trailer.
2. **M39 direction after two reviews:** authorize independent redesign of the common required-access plus capability/selection/
   rejection representation, or hold U8. Funding its full-case workload does not authorize that redesign or select a form.
3. **Binding exceptions after coherent unit designs:** retain R4-M30's 200-token rule. The table exposes expected requests:
   C1 1,950; C2 3,450; C5 850; C8 350; U8p 650 (plus 900 only if its alternative is selected); M39 4,100; U11 publication
   1,150; comparison publication 450. C7's 350 is distributed to the units introducing those facts, not a separate allowance.
   U8s central 180 needs an exception if its selected forecast/actual exceeds 200. E9 is not a blanket 650-token exception.
   Submit the selected coherent units, combined growth and exact requested limits together for owner approval before code;
   no fragmentation or prior U7b exception substitutes for this approval.

The requested numeric approval grants no new vocabulary, Code Rules baseline/structure exception, frozen-path feature work,
SPEC restriction or O4 deferral. Any such need is evidenced and decided separately. This proposal is complete as a costed
planning decision; its semantic alternatives are deliberately not represented as implementation-ready designs.

## 6. Exit and later stages

R4 exits only when A1-A6 satisfy their contracts, the ledger has P9 = 0 and open (b) = 0, all general Unsupported boundaries have
their required owner disposition, selected verification passes, and the measured compiler fits the approved area and stage
limits. O3 acceptance changes, O4 general-rule limits and O1 vocabulary additions retain their existing owner gates.
Raising a cap cannot reclassify a defect, make an Unsupported family complete, relax a test or select a representation.

U12 records area sizes, hotspots and duplication candidates; resolves the recorded MirAnalysis restructuring deferral; observes
MirDump's next-change restructuring requirement; and proposes the next stage's caps from measured exit sizes under the existing
policy. COMPILER_ARCHITECTURE's measured-plus-2% rule applies at reduction-stage exits; this proposal does not silently extend
it to the R4 growth/shadow stage. Unused R4 allowances do not automatically pass to R5. The stage total remains a separate limit.

R5's current ceiling is 942,976; R6's is 654,531; the final target is 520,679. This proposal does not raise them or credit removal
of Analysis/Emission before R6. The reference central R4 exit leaves only **16,830** for R5; the reserved R4 exit alone exceeds
R5's ceiling by **25,750**, before any R5 work. As sensitivity only, preserving the old planned R5 increment of 48,614 would
put the central path at **974,760**, 31,784 above that ceiling. This is not a new R5 implementation estimate. Approving C thus
accepts a material risk of a later budget revision; it does not establish feasibility of the unchanged later-stage targets.

Reforecast downstream size at common-design closure and U12. Before R5 implementation, estimate its complete generation scope
from actual R4 size and identified deletions. If it does not fit, return a separate budget/deletion proposal before starting.
The R0 timing baselines carried to before R5 remain required. Extra R4 room is not an automatic permanent increase to later-stage budgets.
