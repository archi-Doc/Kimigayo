# Q8: planning-session decisions on the package's open points

**Approved by the owner on 2026-10-18 (「Q8を承認」), with the proposals Q8-6 and Q8-7 (DECISIONS R5-M23).**

The planning session's decisions on `Q8-package.md` §open points (2026-10-18). With them the package goes to its design review
and then once to the owner (OQ10, DECISIONS R5-M1). Where the package and this file disagree, this file governs.

| # | Open point | Decision |
| --- | --- | --- |
| 1 | Q17's form | Each MIR recording rule names its own requirement (`MirProblem.Requirement`), restoring PLAN10 §7; this needs a rewritten ask A-5 to R4 (unit 1c) and supersedes R4-M3a's derivation clause in the commit that implements it. The plan's code-to-requirement table stays only as the fallback if R4 declines |
| 2 | Order of Q7 and R4's unit 1c | Q7 (Diagnostics-owned `OwnershipRequirement` kinds) first, so 1c names its requirements with them; if 1c must land before Q8's approval, A-5 splits into A-5a (Loan facts on `MirProblem`, G125) and A-5b (requirements, after Q7) |
| 3 | H1's subject source | A scan of the published Adjust column after an eager Final publication (new behavior-preserving unit H1a), not a judgment at the publisher's visit |
| 4 | Subject source S2 (unconsumed Places) | Defined on the published columns, not by a Discard Context predicate |
| 5 | Lint rows 9–11 (jump and label checks) | Recorded in the lint walk by the pure rule |
| 6 | Q16's mutability view | Computed, not stored |
| 7 | CFA before H10 | CFA does not read Binding's target rows; CFA's remaining `RequireFallthrough` calls Binding's guess query |
| 8 | When H1a lands | Any time after G2 |

Information for R4 (ask A-13, new; no work requested now): the Q8 probe (`artifacts/verify/r5-evidence/q8-probe/` in the main
checkout) shows MIR accepting, with no MIR problem, three programs the old analysis rejects: a bare Move of a non-Copy local, a
non-Copy Place used as a statement (copied, then dropped twice), and inferred captures of non-Copy values. H1 closes them together
with the acceptance flip (PD29), so no dev state lets MIR decide them; the difference ledger cannot show them because TransferRequired
passes through uncompared (R4-M4).

## Design review round 1 (lens q8: BLOCK) — decisions

The reviewer found no fourth coverage gap (S1–S3 emulated over 75 programs flag every old TransferRequired subject). The package is
redone against these decisions; the same reviewer checks round 2.

| # | Finding | Decision |
| --- | --- | --- |
| Q8-1a | H1's record would turn a default's bare read of a preceding parameter (SPEC 7.2.3 `bad(x: string, y: string = x)`) from `DefaultArgumentMove_Kd` without repair into TransferRequired with a verified `x@move` repair (itself an error, SPEC 23.1.3) | S1 and S2 exclude `HirRes.PreparedParam` subjects; `DefaultArgumentMove_Kd` stays with its producer (the old path until H5; from H5 A1 reports it for PreparedInput roots, R4's unit 1c "DefaultArgument* for PreparedInput roots") |
| Q8-1b | H1's record drops the Semantics case fact of a pair-layer Place whose owner case lacks a plan (DIAGNOSTICS Semantics-cases row: TransferRequired `target:Text,case:Text`) | H1 takes the failing cases from `BareAcquisition`'s mask and records them, as the explicit-entry form does |
| Q8-1c | S3's no-capture-list record has its own Note and no repair (DIAGNOSTICS §4.3 Defaults, :152) | The package names S3's reporter and states its record: the Note, no repair, and the default Note where §4.3 requires it |
| Q8-1d | Q14 compares only (code, subject) | Q14 compares every old record at each subject — code, Note, case fact, repair — normalizing parentheses (old at `s`, H1 at `(s)`) |
| Q8-2 | A failed capture fails the closure node, which replaces its body, so independent errors inside it are lost (SPEC 23.1.2; probes p7, t03); likewise MIR problems inside receiver/index operands of compound S1/S2 Places | A failed capture fails the closure's creation, but the closure body stays published and built, and a failed compound Place keeps its operands published — G121's proposal (PLAN) applied before H1: a node failure does not hide independently resolved children. This changes R4-M61's "a node published Error is replaced whole" for these cases (a departure for the owner's summary); its unit precedes H1 and touches the HIR publisher and the MIR builder (W1 or an R4 unit — agreed with R4) |
| Q8-3 | Should `MirValidator` reject a Copy operand of a non-Copy Type (R4's question) | Yes, as a producer invariant inside G:R6-check-flip (in H1 or the unit right after it, before the group merges): it reads Binding's published Copy proof under the body definition's premises (no new proof), exempts `PairAcquire` and per-view `Adapt` operands, and needs a hold window on `MirValidator.cs` shared with G131's rule (F11). Q14 runs it in shadow first. Binding stays the judge of TransferRequired |

Adopted non-blocking findings: the departures list adds plan row Q11 (records move from the jump binding to the lint walk) and, at
H5, A4/A5 `Binding.*` requirements becoming `Ownership.*` in the public-names row; V1 is a Binding static helper in a new partial,
not a `BindingModel.cs` member (R5-M7); DIAGNOSTICS updates are listed for Q10–Q12 and H1 (§4.3 sentences, :107, :152, :166);
Q10's fallback for an uncovered `UnsafeFunctionValue` is a lint row, not a node failure (PD29/PD31); the order producers → publish →
H1 → counting → lint walk is stated (rows 1–5 and 8 derive from `FailureCauses`, which already includes H1's failures);
republication creates no new subject (asserted); every null jump-target row has a row 9 or 10 record (tested); `MirReport.Add`
checks "merged problems agree on the requirement" as an invariant.

Owner summary additions: Q8-1a's code stays `DefaultArgumentMove_Kd` (no default code change); Q8-1b/1c keep the documented
records; Q8-2's change of R4-M61's node-failure contract (G121 applied); Q8-3's validator rule.

## After the round-1 redo — decisions on the synthesizer's notes

| # | Note | Decision |
| --- | --- | --- |
| Q8-4 | Q8-1a's exclusion is right for S1 (the builder moves the prepared input, and A1 reports `DefaultArgumentMove_Kd` from H5), but for S2 it leaves no producer: a default whose block discards a non-Copy preceding parameter is copied through the prepared reference and the copy dropped, with no MIR problem (probe u2), and Q8-3's validator rule would then fault on a user error | S2 keeps `HirRes.PreparedParam`-rooted subjects and records them with the old path's code `DefaultArgumentMove_Kd`, its Note and no repair, provided Binding can record that code without a new `BindingFailure` member (PD21; the round-2 review checks this in the code). Only if it cannot, S2 records `TransferRequired_Kd` with the default Note and no repair — the record Binding already gives a bare capture entry of a preceding parameter in a default — listed as a user-visible code change for the owner |
| Q8-5 | The validator rule's hold window shared with F11 | Needed only while R4 runs in parallel: G:R6-check-flip lands after R4's exit, when no hold applies; the rule keeps its shadow run in Q14 |

## Design review round 2 (lens q8: BLOCK) — decisions

Resolved in round 2: B1(a) by Q8-4 (the review confirmed PD21 feasibility: `BindingFailure.TransferRequired` maps to
`DefaultArgumentMove_Kd` for a PreparedParam-rooted Place through the existing per-node code switch, with no repair; only the
requirement name changes, `Ownership.DefaultArgumentMove` → `Binding.TransferRequired`), B1(c), Q14, Q21 for compound Places, the
validator placement and every non-blocking item. Two findings are a second round on their families (B1(b), B2); the planning session
proposes the fixes below, and the owner approves them with the package or chooses otherwise (AGENTS).

| # | Finding | Decision |
| --- | --- | --- |
| Q8-6 | B1(b), second round: the only renderer of DIAGNOSTICS :166's Semantics-case record (case fact, "Found under … (SPEC 8.15)", related binder declarations) lives in the old Analysis (`OwnershipAnalysis.cs:360`, `.Cases.cs:321`), which Binding may not read and H11 deletes; Binding's `FailingCaseNote` renders a different text without the fact | Proposed: one renderer — the Semantics-case formatting moves into Binding beside `FailingCaseNote` and serves both forms; the old Analysis calls it until H11. :166's record text stays |
| Q8-7 | B2, second round: Q21's in-place writes (Adjust None) for a failed closure lose an expression body's tail, which only the closure's own `Consume` consumes (`Binding.Hir.Adjust.cs:29`; Error nodes skip `Consume`, `Binding.Hir.cs:118`), so probe p7's record is lost and the builder makes a by-case Copy of a non-Copy capture that the validator rule would fault | Proposed: a closure whose creation failed keeps its own `Consume` as a Closure (its defaults and its expression body's result); only its creation is Opaque. In-place writes apply to the operands of failed compound Places only |
| Q8-8 | S1's PreparedParam roots (non-blocking) | S1 gives them the same record as S2 (Q8-4: `DefaultArgumentMove_Kd`, no repair), so Binding is the one judge of bare reads of prepared inputs and A1's mapping (R4's 1c) serves only an explicit `a@move`; the R4 planning session is told (A-5 scope) |

Package text to correct (stale against Q8-4): D8, the §3.7 exclusion row, §5, §11.1 and Q14's "or a PreparedParam root"; §7's
public-names row adds `Ownership.DefaultArgumentMove` → `Binding.TransferRequired`.

Owner summary additions (round 2): Q8-6 and Q8-7 reached two rounds and are presented as the planning session's proposals; Q8-8
narrows R4's 1c PreparedInput mapping to explicit `a@move`.

Placement (2026-10-18, agreed with the R4 planning session, ask A-14): Q21 lands in R5 window W1; its commit supersedes R4-M61 for these cases and closes PLAN G121, and it runs MirInitTest, MirEffectsTest, MirBorrowsTest and the difference ledger, sending R4 any changed A-row.
