# X-1 triage, half 2 (R4 triage agent, 2026-11-03; read-only)

Ledger: `artifacts/verify/shared-base/1eb0cbaa9/mir-diff` (`raw-44232.tsv`). A raw id is `<comparison>/L<line of raw-44232.tsv>`.
I picked the rows by re-running `MirDifferenceLedger.Differences`' rule (old, complete, unpaired by body and code, no unpaired MIR
row left in the body after all pairing). That gives 53 rows, matching the ledger's blocking counts for the 8 half-2 signatures
(12 + 11 + 8 + 8 + 8 + 4 + 1 + 1). Per-row verdicts: `x1-half2.tsv`.

## Counts

| verdict | rows |
| --- | ---: |
| R (restated) | 33 |
| a | 5 |
| d | 15 |
| X | 0 |
| ? | 0 |

| signature (complete missing old A5) | R | a | d |
| --- | ---: | ---: | ---: |
| CallActivationConflict_Kd InvocationKoto (12) | 11 | 1 | 0 |
| ComparisonLoanConflict_Kd InvocationKoto (11) | 7 | 0 | 4 |
| ComparisonLoanConflict_Kd BinaryKoto (8) | 0 | 3 | 5 |
| ComparisonLoanConflict_Kd ConversionKoto (8) | 2 | 0 | 6 |
| ComparisonLoanConflict_Kd FieldKoto (8) | 8 | 0 | 0 |
| CallReservationConflict_Kd NumberLiteralKoto (4) | 4 | 0 | 0 |
| CallReservationConflict_Kd IdentifierNameKoto (1) | 0 | 1 | 0 |
| PlacementActivationConflict_Kd ArrayLiteralKoto (1) | 1 | 0 | 0 |

## Groups

- **R, A1 restates (19 rows).** The statement uses a moved or uninitialized Place. The MIR reports A1 `MovedPlace_Kd` or
  `UninitializedPlace_Kd` there (new or both), and the old Loan record restates that failure. All 8 FieldKoto rows, all 4
  NumberLiteralKoto rows and 7 InvocationKoto rows fall here.
- **R, conflict reported once (14 rows).** BU-27's `change(entries[1])` rows (261ee73d1c89, 4983f9dc73d9 ×2, b1e9c0969b9a): the
  MIR's CallActivationConflict sits at the inner `entries[1]` with this row's exact span. Mechanical pairing matched the outer old
  record with it, which is why the inner record shows as missing. The outer activation checks the same Loans through that result,
  so neither is a missed independent record. The other rows are an overlap of two exclusive arguments reported once at the call
  (f7322971d2c5, b50448e160ff, a5ee4dd6613a, 58afdaf31027, 366bdfe2c59d), the old activation record beside the MIR's read
  conflict (dd0047bc8cc5, a4ed26eb204e), the old record at a read or write through an index result beside the MIR's
  receiver record (f172686ede45, c266b63b0631), and the literal's placement versus the operand `b` (e7ff4e6ae8ba).
- **d, 15.6.3 as revised (R4-M52) (14 rows).** These are the 15.6.3 examples' Valid lines (2041079ddfb5, 4ef2f1940730,
  ebd2922c028d), their two error variants (596cdd9bc717, 65f659ad8ee1: each variant's real error pairs by code), ContentUpdateTest's
  `replace` ending child's Loan (55f3c3c9a921, 943a89665f91, a4e693a304e9), and the four Lending-cursor copies (abe08c1e556a,
  3f7c9e07878a, 9014157d9ad6, f78d225dd9d9). In those copies the first `values = Values.init()` pairs, and the second one conflicts
  with nothing, since the first `=` ended the cursor's Loan.
- **d, 15.6.4 as revised (R4-M154) (1 row).** e953d3c484f2 (`viaSlot`).
- **a (5 rows):**
  - 2ce7c2996be7 (ContentUpdateTest.OverwritingACallUpdatedSlotKeepsReturnedChildrenExclusive): line 8
    `replace(slot@uniq, with: old)` is valid, since change's result keeps only what a read through `slot@uniq` carries, not the
    Loan on slot (15.6.4; 15.6.3 takeFirst). The test's error at line 9 pairs.
  - 9f2c5beebe79 (TypeDagScalingTest.ARingThroughAHandleRetainsTheLoan): t's destruction observes no Origin (15.6.6, 16.3.3).
  - 029886d83bfe: AbstractCallReachTest's OldPathGaps "again UniqItems", G217 p4.
  - 1720228ea1c3: an Origin clause keeps no Loan live (15.6).
  - a1aae04c6469: 4.7.4, 15.6.2.

## X rows

None. The candidate checked hardest was BU-27, which the classification note records as "(b), open, owner A5": a second
CallActivationConflict at an inner indexUniq receiver missing on the MIR. On this ledger the MIR reports at that inner receiver,
so the old record left over is the outer activation, which checks the same Loan pair.

## Remaining uncertainty and follow-ups

- No probe was run; the verdicts come from the sources, the raw rows and SPEC.
- Two (a) rows hold old-rule pins that assert the old conflict, which the MIR does not give. 2ce7c2996be7's test asserts only
  "some Loan conflict", which the MIR's line-9 record meets. TypeDagScalingTest.ARingThroughAHandleRetainsTheLoan(true) asserts
  ComparisonLoanConflict_Kd on the combined problems, so it needs an OldRule marker or a change of expectation when the old path goes.
- The e8 error variants' (d) depends on which of the two old records the code pairing picks. Either way, the variant's real error
  is reported by the MIR.
