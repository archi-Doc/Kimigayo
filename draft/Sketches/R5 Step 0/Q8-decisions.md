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
