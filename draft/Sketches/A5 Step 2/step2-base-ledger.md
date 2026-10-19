# Ledger re-baseline at the step-2 base (unit 2x-T2)

Date: 2026-10-19. Base: `origin/dev` `6e969f94` (1c-9 merged at `0e96cf19`, G148 at `1db0da9f`). Unit row: plan §4.1 2x-T2
([A5 step 2 plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md)), §5.1 (owners of open rows); input
census: the 2026-10-18 ledger audit (`artifacts/handoff/2026-10-18-pause/r4-scratch/step2-audit/LEDGER/audit.md`, base
`806110fc`). Evidence only: no SPEC, compiler, test, library or `classification.tsv` change. "(H)" marks an inference not
checked by running code.

## 1. Runs and evidence

| Run | Command (worktree `bin/worktrees/2x-t2`, HEAD `6e969f94`, clean) | Result |
| --- | --- | --- |
| Ledger | `KIMI_MIR_DIFF=<fresh dir>`; `verify.ps1 -Class '*' -Pipeline mir -TestPurpose Functional -Name 2x-t2-ledger` | 20,699 tests, 1 failure |
| Control | the same without `KIMI_MIR_DIFF` (`-Name 2x-t2-mir`) | 20,699 tests, 1 failure |

Both failures are `FixedArrayFillTest.FillWorksInGenericBodiesAndCandidateLocalArguments` (G103-E9a, unit 2x-E1), so the
failure sets are equal. Evidence (Git-ignored) is `artifacts/verify/mir-diff/step2-base/`: the raw rows, 1,962 sources,
`ledger.tsv`, `summary.md`; `runs/` (both runs' summary, log and result XML); `census/` (`blocking.tsv`, `owners.tsv`,
`signatures.tsv`, `differences.tsv`, `restated.tsv`, `bysig.md`, `delta.txt`); `tools/` (the scripts below and `manual.tsv`).

**Census method.** `tools/census.py` re-implements `MirDifferenceLedger.Differences` (with R5-Q17's side) and `Aggregate`'s
blocking and open predicates over the raw rows and `classification.tsv`; it reproduces `summary.md` exactly. `tools/owners.py`
assigns each open blocking row one owner unit: a row whose comparison and body hold a MIR record goes to 2x-T3 (the body
part of PD18's predicate); otherwise its cause comes from the 2026-10-18 census by source hash (`loan-classes.tsv`,
`u-classes.txt`, `origin-rows.txt`) and the code, and the 10 sources whose rows that census did not classify were read and
recorded in `tools/manual.tsv`. `tools/delta.py` compares the blocking rows with the old census's.

**Same ledger as G148's.** Signature by signature and blocking row by blocking row (locations compared by file name), this
ledger equals G148's records run at `b57ae7dc` (`artifacts/verify/mir-diff/g148-r3/ledger-head4`, `5a0192fe`): only PLAN
commits followed it on dev.

## 2. Totals

| | 806110fc (a5-1b5-b1) | 6e969f94 (step-2 base) |
| --- | ---: | ---: |
| Comparisons; rows (both, old, new) | 2,010; 2,961 (875, 1,282, 804) | 2,072; 3,044 (851, 1,419, 774) |
| Signatures; unclassified (rows) | 263; 15 (237) | 266; 21 (295) |
| Open (b) signatures (rows) | 186 (1,899) | 179 (1,915) |
| Blocking rows; open (unsound, library, milestone) | 522; 440 (435, 0, 5) | 580; 466 (460, 0, 6) |
| Open blocking A4 / A5 / U | 40 / 218 / 182 | 40 / 209 / 217 |
| SPEC examples (blocks, variants, checked on the MIR) | 43, 63, 31 | 44, 64, 32 |
| Table rows nothing matched | none | none |

## 3. Owners of the 466 open blocking rows

| Owner unit (§5.1) | Rows | Old base (§5.1) | Content |
| --- | ---: | ---: | --- |
| 2x-T3 restated kind | 79 | ≤ 74 | §6 |
| 2x-T4 classification of the MIR-right gates | 211 | 180 | the 180 rows of §5.1, plus the 31 new rows of §5 |
| 2a (A5-F4) | 85 | 85 | Origin codes 84 (Unproven Field 37, TypeSemantics 17, Invocation 8, Conversion 3, IdentifierName 2; Unsatisfied Conversion 9, Field 2, IdentifierName 2, Invocation 2, NumberLiteral 1; UnprovenConstraint 1) and the Loan row of `origin r outlives x.s` (1720228ea1c3) |
| 2c (A5-F7) | 5 | 5 | Closure stores into captures or state 4 (a46e125d3817, 358748d9dfcc, 4037bfa952e2, 40f3cbe34df1); G147 (3) 1 (24028725b625) |
| 2d (A5-F8) | 10 | 19 | G147 (1) Dictionary literal 10 (Comparison/Binary 5, CallActivation/Invocation 2, Placement/DictionaryLiteral 2, Comparison/IdentifierName 1); G147 (2)'s 9 no longer block (§5) |
| 2e (A5-F9) | 16 | 15 | DefaultArgumentBorrow 11 (new: 881db7701b46, `b: s/T = a`), DefaultArgumentAccess 5 |
| 2f (A5-F6) | 52 | 52 | A4 IncompatibleContractImplementation/Is 35, UnsatisfiedEffectBound/MemberAccess 3, /IdentifierName 2; CallEffectConflict/Invocation 9 (PD7); U Invocation 3 (a repeated `preserves results` Callable: 47f0722c876b, 936f8571825e, a105a408a756) |
| X-1 | 8 | 7 + 1 + 2 | E8 (d) R4-M52 in the mixed Comparison/Invocation signature 4 (943a89665f91, 55f3c3c9a921, a4e693a304e9, ebd2922c028d); (a) 4.7.4 in the mixed Comparison/Binary signature 1 (a1aae04c6469); (a) G147 (7) erased callable 1 (a5815e667296); `insertOrReplace` result 1 (af43dcd8ffce, 2x-S1); U Never `.Err(1)` 1 (76521fc87e18); G147 (4)(5)'s 2 no longer block (§5) |
| 2b, 2g, 2x-E1/E2 | 0 | 0 | |
| **Total** | **466** | 440 | every row has exactly one owner unit (`census/owners.tsv`) |

By signature (section complete for every row; the table row and category are the current first match):

| # | Signature (kind, side, analysis, code, requirement, form) | Rows / open blocking | Table row | Owner units (rows) |
| ---: | --- | ---: | --- | --- |
| 1 | missing old U Unsupported Unsupported IdentifierName | 180 / 169 | - (?) | 2x-T3 6, 2x-T4 163 |
| 2 | missing old A5 UnprovenOriginRelation UnprovenOrigin Field | 38 / 38 | a5-old-origins (b) | 2x-T3 1, 2a 37 |
| 3 | missing old A4 IncompatibleContractImplementation EffectBound Is | 35 / 35 | a4-preserves (b) | 2f 35 |
| 4 | missing old U Unsupported Unsupported MemberAccess | 37 / 33 | - (?) | 2x-T3 1, 2x-T4 32 |
| 5 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict IdentifierName | 21 / 17 | a5-old-loans (b) | 2x-T3 10, 2c 4, 2d 1, X-1 2 |
| 6 | missing old A5 UnprovenOriginRelation UnprovenOrigin TypeSemantics | 17 / 17 | a5-old-origins (b) | 2a 17 |
| 7 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict MemberAccess | 20 / 15 | a5-old-loans (b) | 2x-T3 12, 2x-T4 3 |
| 8 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict Binary | 21 / 11 | a5-old-loans (b) | 2x-T3 4, 2a 1, 2d 5, X-1 1 |
| 9 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict Invocation | 13 / 11 | a5-old-loans (b) | 2x-T3 7, X-1 4 |
| 10 | missing old A5 DefaultArgumentBorrow DefaultArgumentBorrow IdentifierName | 11 / 11 | a5-old-default-borrow (b) | 2e 11 |
| 11 | missing old A5 CallActivationConflict ComparisonLoanConflict Invocation | 64 / 9 | a5-old-loans (b) | 2x-T3 7, 2d 2 |
| 12 | missing old A5 CallEffectConflict CallEffectConflict Invocation | 9 / 9 | a5-old-effects (b) | 2f 9 |
| 13 | missing old A5 UnprovenOriginRelation UnprovenOrigin Invocation | 9 / 9 | a5-old-origins (b) | 2x-T3 1, 2a 8 |
| 14 | missing old A5 UnsatisfiedOriginRelation UnprovenOrigin Conversion | 13 / 9 | a5-old-origins (b) | 2a 9 |
| 15 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict Field | 8 / 8 | a5-old-loans (b) | 2x-T3 8 |
| 16 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict Index | 17 / 8 | a5-old-loans (b) | 2x-T3 8 |
| 17 | acceptance old U Unsupported Unsupported IdentifierName | 30 / 6 | - (?) | 2x-T4 6 (the milestone rows) |
| 18 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict Conversion | 7 / 6 | a5-old-loans (b) | 2x-T3 2, 2x-T4 4 |
| 19 | missing old A5 DefaultArgumentAccess DefaultArgumentAccess IdentifierName | 5 / 5 | a5-old-default-access (b) | 2e 5 |
| 20 | missing old A5 CallReservationConflict ComparisonLoanConflict NumberLiteral | 4 / 4 | a5-old-loans (b) | 2x-T3 4 |
| 21 | missing old U Unsupported Unsupported Invocation | 4 / 4 | - (?) | 2f 3, X-1 1 |
| 22 | missing old A4 UnsatisfiedEffectBound CallableEffectBound MemberAccess | 3 / 3 | a4-callable-preserves (b) | 2f 3 |
| 23 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict If | 3 / 3 | a5-old-loans (b) | 2x-T3 3 |
| 24 | missing old A5 UnprovenOriginRelation UnprovenOrigin Conversion | 3 / 3 | a5-old-origins (b) | 2a 3 |
| 25 | missing old A4 UnsatisfiedEffectBound CallableEffectBound IdentifierName | 2 / 2 | a4-callable-preserves (b) | 2f 2 |
| 26 | missing old A5 PlacementActivationConflict ComparisonLoanConflict DictionaryLiteral | 2 / 2 | a5-old-loans (b) | 2d 2 |
| 27 | missing old A5 UnprovenOriginRelation UnprovenOrigin IdentifierName | 2 / 2 | a5-old-origins (b) | 2a 2 |
| 28 | missing old A5 UnsatisfiedOriginRelation UnprovenOrigin Field | 2 / 2 | a5-old-origins (b) | 2a 2 |
| 29 | missing old A5 UnsatisfiedOriginRelation UnprovenOrigin IdentifierName | 10 / 2 | a5-old-origins (b) | 2a 2 |
| 30 | missing old A5 UnsatisfiedOriginRelation UnprovenOrigin Invocation | 3 / 2 | a5-old-origins (b) | 2a 2 |
| 31 | missing old U Unsupported Unsupported Conversion | 4 / 2 | - (?) | 2x-T3 2 |
| 32 | missing old U Unsupported Unsupported SyntaxForm | 2 / 2 | - (?) | 2x-T4 2 |
| 33 | missing old A5 CallReservationConflict ComparisonLoanConflict IdentifierName | 1 / 1 | a5-old-loans (b) | 2x-T3 1 |
| 34 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict CodeBlock | 1 / 1 | a5-old-loans (b) | 2c 1 |
| 35 | missing old A5 ComparisonLoanConflict ComparisonLoanConflict NumberLiteral | 1 / 1 | a5-old-loans (b) | 2x-T3 1 |
| 36 | missing old A5 PlacementActivationConflict ComparisonLoanConflict ArrayLiteral | 2 / 1 | a5-old-loans (b) | 2x-T3 1 |
| 37 | missing old A5 UnprovenConstraint UnprovenOrigin TypeSemantics | 1 / 1 | a5-old-origins (b) | 2a 1 |
| 38 | missing old A5 UnsatisfiedOriginRelation UnprovenOrigin NumberLiteral | 1 / 1 | a5-old-origins (b) | 2a 1 |
| 39 | missing old U Unsupported Unsupported TupleType | 1 / 1 | - (?) | 2x-T4 1 |

2x-T4's 211 rows by cause: mutable statics 167 (`u-static`: IdentifierName 144, of which the 6 milestone acceptance rows,
and MemberAccess 23), check-only code 10 (`u-check-only`), Never Places 12 (`u-never`: IdentifierName 9, SyntaxForm 2,
TupleType 1), temporaries' parts 7, accessor `storage` 2,
disjoint write during a reservation 1, the destruction receiver's helper borrow 3, a Field path through an rc or obj layer 2,
E8 (d) R4-M52 Conversion 4 (signature 18 after 2x-T3), (a) 4.7.4/15.6.2 MemberAccess 3 (signature 7 after 2x-T3).

## 4. a5-old-loans at this base

98 open blocking rows of 224 (197 missing) in 26 signatures (108 at 806110fc): restated candidates 68 (DUP-A1 26; DUP-A5 38,
with the 3 `BodyOf` rows of a229d8f4dca0 and e7ff4e6ae8ba, which is e78fcec4f721 with `var items`; E8 2; the two compound
assignments 20047a4c587f `a[0] += 1` and 5a0d0e2e3337 `pair.left.value += 1`, the note's a1-cascade analog, from new
sources), G147 (1) 10, F7 4, E8 (d) 8 more (Conversion 4 to 2x-T4, Invocation 4 to X-1), (a) 4.7.4 4 (MemberAccess 3, Binary
1), G147 (3) 1, G147 (7) 1, F4 1, `insertOrReplace` 1. Gone since 806110fc: G147 (2) 9, G147 (4) 1, G147 (5) 1, and aa63161bf69c's DUP-A5 exit
row, whose body now holds a5-p13's MIR `Unsupported_Kd` (R4-M73's body hold), so the row is a code choice.

## 5. New and stale rows for 2x-T4

**Gone from the blocking set** (48 old rows): A1 33 (already classified at 806110fc); G147 (2) 9: BU-8/BU-13 landed, so 5
are location rows (a5-location) and 4 are an old Comparison at the index beside the MIR's CallActivation at it (a5-old-loans
missing plus a5-new), none blocking; G147 (4) 9efe9d756008 and G147 (5) 1968ab320746: location rows after R5-F14/F15 (§5.1's
2x-T2 row: they paired, so X-1 no longer waits for them); aa63161bf69c (§4); e78fcec4f721 (its source changed to
e7ff4e6ae8ba); 06ee21107fb1's 2 G100 rows: Binding now reports `ExclusivePathTake_Kd` for the Field Moves out of drop's
`self` (BU-15's 2x-E4a; `GenericDestructorEmissionTest`), so the source has no compared row.

**New blocking rows** (106 not among the old census's blocking rows): 65 are already closed by (a) or (d) table rows (`u-index` 24,
`u-binding-sets` 19, `u-bu14-missing` 16, `u-static-keys` 4, `u-never-loop` 2); 9 are restated candidates (§6); 1 is 2e's
(881db7701b46); 31 are 2x-T4's:

| Source | Rows | Cause | Proposed classification |
| --- | ---: | --- | --- |
| 251764fb3a2c | 18 | Milestone 42's whole text compiled as `Hello.kimi` by a corpus test (which one is not traced): IdentifierName 16 and MemberAccess 1 missing, 1 milestone acceptance | u-static (a), as 73204fa40e52 |
| 1aa9cbeeb3b3, 5d361fc2aeba, 8a09c6e9e58c | 3 | The destruction receiver's helper borrow (`Support.see(self)`, `let whole = self@ref`; 8a09c6e9e58c is spec-case 16.3.1-destruction-receiver's base variant): Binding accepts since BU-15 (2x-E6), the old Analysis keeps its gate | a new (a) row, SPEC 16.3.1 |
| 4a50b3fc5e21, d292b02c70e2 | 2 | A Field through an rc or obj layer (`p: ref/(rc/Point)` reading `p.n`; `p: uniq/(obj/Point)` writing `p.n = 5`; `PathCapabilityDiagnosticTest`, BU-13's `6b27745c`) | a new (a) row; clause to name (15.1.5, H) |
| 52c4defc2618 | 2 | A Place of `(Never, i64)` (TupleTypeKoto, IdentifierNameKoto; `UnboundCallSlotTest`) | u-never (a), forms extended |
| 92df8d7329c3, c4a64eba0fca, c4d034d23aec | 6 | `x: Never` read by Copy twice, or after `x@copy`, `x@Never` (`NeverContinuationTest`, which states the old gate stays until R6-check) | u-never (a), IdentifierNameKoto |

Signatures 1, 4 and 17 mix these causes, as at 806110fc (G106): a classification row matches by form, so 2x-T4 classifies
each signature as a whole. They also hold non-blocking rows 2x-T4 must read first: signature 1 has 11 and signature 4 has 4
whose body keeps an unpaired MIR A1 or A5 record (old `Unsupported_Kd` beside a MIR error: code choices, H), and signature 31
(U Conversion) has 2 such rows; signatures 7 and 18 hold 5 and 1 non-blocking a5-old-loans rows.

**Unclassified signatures** (21; 15 at 806110fc). New: R5-F13's 8 jump signatures (incomplete, acceptance and missing, old U
at YieldKoto 2+2, ReturnKoto 5+5, ExitKoto 2+2, ContinueKoto 1+1 rows), present and non-blocking; U missing and acceptance
TupleTypeKoto (52c4defc2618); U missing ConversionKoto (4 rows, Never borrows); A1 new UninitializedPlace ConversionKoto (2
rows: 61f8772d6d89, 373d23b7f58b, `var y = x` with `x: Never` then `y@uniq`; check-only code, a1-c3's (d) 14.10.3 with its forms
extended, H). Kept: U missing IdentifierName, MemberAccess, Invocation, SyntaxForm; U acceptance IdentifierName,
MemberAccess, Invocation; incomplete mir U acceptance and new InvocationKoto (21bbb68ddf53, X-1's ownerless row). Gone: the
six other old ones (Q17's mir-side rows, a5-defaults and a5-p13 absorb 4efae67ec7a7's MIR records; f0c29ce98038 is no longer
compared; 92df8d7329c3's MIR MovedPlace is gone).

**Stale table text.** `a1-result` was already deleted by R5-Q17 (`ebbacda3`; DECISIONS R5 Q17), so 2x-T4's deletion item is
void. a5-old-loans' note: "(6.4.5; 1)" and "(16.2.2; 1)" are 0 (location rows now), "(4.7.1; 9)" is 0 blocking (above), "22 are
new-path errors" is 11 (G147 (1) 10, G147 (3) 1), "147 missing rows in 134 sources at f4268bca" is 197 missing rows of 224 in
26 signatures, 98 blocking; R4-M73's 4 body-hold rows are counted. The 4efae67ec7a7 MIR `Unsupported_Kd` rows now match
a5-defaults and a5-p13; whether a Field default (`next: ... = .None`) or the polymorphic recursion causes them is unverified.

## 6. 2x-T3's 79 restated candidates

Each row's comparison and body hold a MIR record and none unpaired (`census/restated.tsv` lists the records). PD18 narrows the
kind to a paired record naming the same Loan anchor or Move Path or the same A1 subject Place, which the raw rows cannot show;
so 79 is an upper bound and §5.1's X-1 line takes any row 2x-T3 leaves `missing`.

| Group | Rows | MIR record in the body | Expected under PD18 (H) | If left `missing` |
| --- | ---: | --- | --- | --- |
| DUP-A1 | 26 | A1 MovedPlace or UninitializedPlace at the borrowed Place | restated (same A1 subject Place) | X-1 |
| DUP-A5 | 38 | A5 conflict at the statement or reservation (3 by `BodyOf` in a229d8f4dca0, the old node outside its tree) | restated (same Loan) | X-1 |
| Compound assignment | 2 | A5 conflict at the statement (`a[0] += 1`, `pair.left.value += 1`) | restated (a5-old-loans' note) | X-1 |
| E8 (d) | 2 | A5 conflict on the next line (596cdd9bc717, 65f659ad8ee1) | open | 2x-T4 (signature 18's (d)) |
| Origin | 2 | A5 conflict at `n = 2` (0c0d270cb525, b1d01281f352) | not restated: an Origin record names no Loan | 2a |
| U Never Places | 8 | A1 MovedPlace or an A5 conflict at `x` (f98b676aa12e, f527fad97620, 4a217b56fdd3, d20cf42a2b4e) | open: an old gate record has no related site | 2x-T4 (u-never) |
| U temporary part | 1 | A5 conflict at `makeResource()` (22e393042ead) | not restated | 2x-T4 (u-temporary-part) |

## 7. §5.1 at this base

466 → 2x-T3 ≤ 79 → ≥ 387 → 2x-T4 211 → ≥ 176 → 2b 0 → 2e 16 → ≥ 160 → 2a 85 → ≥ 75 → 2c 5 → ≥ 70 → 2d 10 → ≥ 60 →
2f 52 → ≥ 8 → 2g 0 → X-1 8 and any row 2x-T3 leaves → 0. Every open blocking row's owner is an R4 unit: G147 (2)(4)(5)
landed, so none waits on an R5 or BU unit (G147 (3) is 2c-1's, PD6), though 2d-3 still takes its W1 slot (AR-1).

## 8. Unverified

- Causes of rows the 2026-10-18 census classified are inherited by source hash, not re-read; the 10 new sources were read.
- PD18's anchor part (§6) and the proposed clauses for the new (a) classes (§5) are readings, not runs.
- The two runs ran beside other units' builds; neither timed out.
