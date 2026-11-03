# X-1 triage, Half 1 (2026-11-03)

Ledger: `artifacts/verify/shared-base/1eb0cbaa9/mir-diff` (raw-44232.tsv). Rows: `x1-half1.tsv` (raw id = comparison id plus the
raw-44232.tsv line; a paired record is named the same way). Selection: the ledger's own rule (complete, old, unpaired by code in its
body, no unpaired MIR record left in the body), restricted to Half 1's signatures: 52 rows, matching ledger.tsv's blocking counts
(IdentifierNameKoto 18, MemberAccessKoto 18, IndexKoto 10, IfKoto 3, ExitKoto 1, NumberLiteralKoto 1, A1 StaticMovePathRequired 1).
No probe was run; no worktree was made.

## Counts

| Verdict | Rows |
| --- | ---: |
| R (restated) | 40 |
| a | 12 |
| d | 0 |
| X | 0 |
| ? | 0 |

| Signature (complete missing old) | R | a |
| --- | ---: | ---: |
| A5 ComparisonLoanConflict_Kd IdentifierNameKoto | 11 | 7 |
| A5 ComparisonLoanConflict_Kd MemberAccessKoto | 14 | 4 |
| A5 ComparisonLoanConflict_Kd IndexKoto | 10 | 0 |
| A5 ComparisonLoanConflict_Kd IfKoto | 3 | 0 |
| A5 ComparisonLoanConflict_Kd ExitKoto | 1 | 0 |
| A5 ComparisonLoanConflict_Kd NumberLiteralKoto | 1 | 0 |
| A1 StaticMovePathRequired_Kd MemberAccessKoto | 0 | 1 |

## R groups (40)

- Assignment target Place, the MIR's record at the statement (`both`, BinaryKoto) (24): 44232.119, .121, .738, .1661, .1662, .1663,
  .1665, .1785, .2119, .2122, .2178, .2180, .2181, .2183, .2185, .2186, .2253, .2415, .2417, .2419 and the call forms .1700, .1731
  (MIR CallActivationConflict_Kd at the same `entries[1]`), .2494, .2496 (MIR CallActivationConflict_Kd at the indexUniq/slot call).
- Overlapping exclusive arguments reported once at the reservation or call (7): .1804, .2151, .2153 (MIR CallReservationConflict_Kd,
  15.6.7); .2155, .2158, .2160, .2162 (a229d8f4dca0, MIR CallActivationConflict_Kd at `bump(xs[0].n@uniq)`; PLAN G169).
- A1's moved or uninitialized Place (7): .541, .764, .2145, .802 (moved), .470 (uninitialized), .472, .477 (moved); the three IfKoto
  rows are Milestone 15 variants whose only change is the initialization or move, and the milestone has no Loan conflict there.
- One conflict, one MIR record (2): .1614 (SPEC 15.8.2 example with `z@follow = 43` enabled; MIR at the same statement) and .826
  (local `n`'s storage end, once at `let n = 5` against one old record per `exit`, 15.6.6).

## a rows (12)

- 4.7.4/15.6.2, `a.0` moved or written while `a.1[...]`'s index is evaluated: .109, .2431, .2187 (as a5-old-loans-index).
- 15.8.2 example line `z@follow = 42 // Valid.`: .1377, .1612.
- PLAN G106's OptionStore (.1834, two rows) and G6c (.1835): 15.8.2, 15.6.
- G147 (7), FiniteRegionCompositionTest's erased callable: .876 (15.6, 15.6.4, 15.6.7).
- insertOrReplace's `Option<V>` result keeps no receiver Loan: .782 (15.6.4, 15.6.3).
- New in this triage: .589 (c7ce9b1b89fa), `fill(h: uniq/Hid during a)`'s `h.p = Raw.allocate(1)`: a Field write through an exclusive
  parameter with no Loan formed in the body (15.6.2). The MIR's two records of that compilation are in `hidden()`, another body.
- A1 .598 (227a6c7535ac, `take(make2((return 42)).0@move)`): (a) by R4's reading of 3.4.1, 3.6, 15.1.3, 15.1.5 (PLAN G315,
  a1-temp-part), which G315 still asks to confirm; until then the classification row keeps it (b).

## X rows

None. In every Half 1 row either the MIR reports the same failure in the same body (named in the TSV) or SPEC allows the program at
that point.

## Notes for the planning session

- 44232.1614: the ledger's order-based pairing (first old row of the code in the body) matched the old record at line 8
  (`z@follow = 42 // Valid.`, L2396) with the MIR's line-14 record, so the over-strict line-8 record appears as a non-blocking
  `location` row and the line-14 restatement (L2397) as the blocking one. The verdicts are unaffected; a location-aware pairing would
  show it as one (a) row and one pair.
- 44232.2145 and .802: the old record sits at the index operand (`i`, the literal `1`), where no Loan exists; it only restates A1's
  moved element.
