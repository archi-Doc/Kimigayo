# Q8: planning-session decisions on the package's open points

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
