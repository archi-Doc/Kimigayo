# Step 0: planning-session choice between designs A and B (provisional, before review)

## Where the two independent designs agree

Both designs, written without seeing each other, chose the same core:

- S1′ base naming: one Loan per borrow site and case.
- Deep borrows and Moves (Rust's rule), so access Places are compared syntactically. This removes the prototype's R3,
  access resolution with authority sets.
  - In A this is P3; in B it is L2.
- Interned, immutable Loan sets: one set id per flow holder per chain head, stored in `MirDataflow` with a join hook.
- Backward holder liveness, with liveness through references closed at check time.
- Loan-free views are skipped.
- Retention keeps every Loan of a retained referent's content (A P4 = B L3).
- Optional: mutable-static borrows never leave the body (A P6 = B L5). It removes A4 per-result anchors and question c2.
- Places reached through references with distinct roots are disjoint. This relaxes the row 8 table and needs owner
  confirmation.
- No new closed-vocabulary MIR member.

## Differences

| Topic | A | B |
| --- | --- | --- |
| Holders | Root locals plus escaped `E` (P1: loses partial replacement; lib 0, ms 0, tests ≤10) | Loan-capable move paths (keeps the precision; needs subtree ranges and the G7 MirMoveData fix) |
| Following references | Layer-blind (P10, needed) | Layer-blind (L4, optional; a fix costs about 40 lines) |
| Origin boundary | Binding keeps fixed–fixed judgments; A5 takes the old Analysis's role (finite or inferred ends, chains). No Binding reporter is deleted | A5 judges every body relation; deletes Binding reporters B1, B2, B4, B6, B9, B14 and B17 |
| `preserves results` and kept results | Phantom pseudo-Loan Φ_c | A "descendant Loan" from inner retention; kept results and S2 are one rule |
| Two-phase | G5/G6 builder contracts (TwoPhaseUniq on every reserved input; assumed path continues the invocation) | TwoPhaseUniq restated as "activates at the first use of its reference" |
| Statics | Optional P5 (keys compared by Field) | `MirStatic` key with Subst, plus `MirCallee` columns |
| Retention source | Binding view `TyCtxt.Retains` | Contract retention rows plus an intrinsic `Retains` column |
| New code | ≈2,270 lines (Mir 1,850, Binding 420) | ≈2,900 lines (A5 1,630, MIR contracts 670, Binding 570) |
| Evaluator bound | Linear on the shapes; one super-linear term (k distinct overlapping nodes at a point), counted in `Work` | Θ(statements + heads × flow holders) set operations; risk K1 (extra passes for an in-loop copy chain) |
| Owner approvals | `MirLocalRole.UpdateGuard`, `RawSlice` arity, `EndBorrow` meaning, `MirCallee.Arg`, DEC entries | TwoPhaseUniq meaning, descriptor rows, intrinsic `Retains`, join and backward pass, flow-closed move paths, `MirCallee` columns, `MirStatic` Subst key |

## Choice: A as the base

Reason: the owner's priority is simple, then fast, then precise.

- A is about 630 lines smaller.
- A needs fewer representation changes.
- A moves no Binding reporter, so double reports cannot arise.
- A's one extra precision loss (P1, per-root holders) costs no library or milestone program.

Candidate grafts from B. The reviewer decides each one; a graft is taken only if it is shorter or removes a risk:

1. B's descendant-Loan rule for kept results and S2, if it is shorter than A's Φ_c.
2. B's TwoPhaseUniq restatement, if it removes builder work that A's G5/G6 needs.
3. B's evaluator bound argument, if it removes A's remaining super-linear term without reference counting.
4. B's explicit overlap rules (Deref step classes borrow/own/raw; interned projection chains) as the text for A's
   syntactic comparison.

Open hypotheses in A, to be confirmed by the prototype in round 3:

- syntactic access with deep borrows is sound without authority sets;
- the check-time closure needs no backward feedback;
- per-root holders (P1) and layer-blind following (P10) change verdicts only where the loss list says.
