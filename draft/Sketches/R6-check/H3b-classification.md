# R6-check H3b: the flip rehearsal's classification

Working record of the planning session (R5) under H0b §5 (`draft/Sketches/R6-check/H0b-flip.md`); DECISIONS R5-M182. Not authority: the
classes, owners and the stop rule are H0b §5.3-§5.5's. One section per round, newest last.

## Round 1 (2026-11-03)

### Inputs

- Head `4aced0623`: `claude/r6-flip` at `1826074d5` (E, O, F with round 0's `fb5989df8`, G, H6) plus S `93762d21e` (`held/r5-h5-s`),
  merged locally, not pushed. Base: dev `5998a0ec0` (`artifacts/verify/shared-base/5998a0ec0/`).
- Runs (raw lists in `artifacts/verify/r6-h3b/round1/`):
  - the default Session with the snapshot step and every fixture: 27,228 tests, **716 failed**, so its snapshot, natives and
    compare-fixtures did not run;
  - a separate snapshot Unit run: 31 differences;
  - a sha256 comparison of the two `fixtures-Release` trees (`fixtures-diff.tsv`): 0 changed, 0 head-only, 200 base-only files
    (40 programs);
  - the 40 milestones: PASS. The five CLI harnesses: PASS.
- Two analysts classified the inputs within the hour, one per input kind. Their tables, which this section summarizes, are on dev:
  - `H3b-round1/round1-tests.md`, with the per-test list `round1-tests.tsv`: 716 tests in 35 groups, T01-T63;
  - `H3b-round1/round1-snapshot-fixtures.md`: 21 rows, S1-S3, F1-F15 and M1.
- Not observed this round: natives on programs both sides accept, and the branch's own ledger. Round 2 runs both.

### Counts

| Class | Tests (groups) | Snapshot / fixture rows (programs) |
| --- | --- | --- |
| 1r | 21 (4: T01-T04) | 7 (27: F1-F6) |
| 1a | 77 (6: T10-T15) | 0 |
| 1x | 17 (1: T20) | 3 (21 records: S2a-c, Milestone 42) |
| 2 | 58 (6: T30-T32, T34-T36) | 8 (12: F7-F14) |
| 3 | 362 (12: T16, T40-T49, T4A) | 0 |
| 4 | 161 (2: T50, T51) | 2 (10 records: S1, S3) |
| 5 | 20 (4: T60-T63) | 1 (1: F15) |

The fixture rows F4-F15 are the same programs as some tests in T30-T31 and T34, seen from the fixture side. Class 3 includes:

- 76 tests where Binding's effect records move to A4 (H0b §5.4);
- 20 verdict reads re-pointed under R5-M185;
- the rest, which are location, code and evidence moves (counted by code in the tables).

### R5's rulings on round 1

1. **H5 stops** (§5.5). The class 2 rows block until they are fixed on dev or accepted as one owner list. The class 5 rows block until
   their owner's fix lands or the owner decides. Then the branch merges dev and round 2 runs.
2. **T4A, S3 and T63's require row are one blocking family, R6-1, not class 3 or 4.** At the head, A3's `RequireFallthrough_Kd`
   publishes direct where the old control-flow check derived it from a recovery's syntax Error. Under DIAGNOSTICS §4.3 ("Later
   phases"), a completion check rests on the bodies the parser supplied. This affects:
   - 6 `RequireRecoveryDiagnosticTest` rows;
   - 7 `syntax.json` cases plus one mutation;
   - one lost independent record beside `TypeMismatch_Kd`.
   Re-pointing the tests would weaken a DIAGNOSTICS contract and its pins, which AGENTS forbids. The cause is A3's: it does not see
   Binding's guess rule (`Binding.GuessedCompletion`). The fix is a design across three owners:
   - R4: A3 decides the dependence;
   - BU: the guess rule;
   - R5: the publisher's Binding-cause arm (R5-M184), which today reads only `Binding.FailureCauses`.
   R5 proposes the design with R4.
3. **ContentUpdate (5 programs) is in dispute.** The fixture analyst reads 1r (F4a and F4b: SPEC 15.6's holder bullet, "`replace`,
   `exchange` and `swap` completely replace no holder"). The test analyst reads class 2 (inside T30). R4's per-row verdict decides; until
   then the rows block, as class 2.
4. **T61 is R5's own row** (F's Core path): a warm `Analyze()` allocates 576 B on every program, against six `Purpose=Allocation` tests
   pinned at 0 B. Its fix is a commit on the branch (`claude/r5-h5-t61`), started in round 1's hour.
5. **F15 (`Utf8TryWriteAcquire`)** stays class 5 until R4 reads it. It is not in the base ledger, because it is an Allocation-purpose
   test. The best reading is class 2 (b).
6. **Class 3 and 4 rows become T-units** after the blockers clear, one family per unit, each with a review: R4 reviews the A-cause
   families and BU the Binding-cause family T40. Their counts by code go into H5's commit message (Q13). R5's own re-points are T16 and
   T43.
7. **The 1r list** (F1-F6, T01-T04) is reported to the owner for information (O-1). The 1a list (T10-T15) is the owner's.

### Blocking rows by owner

| Owner | Rows | Kind |
| --- | --- | --- |
| R4 (A5) | T30 (21 tests: loan conflicts on valid programs, ContentUpdate disputed); F8 IntegerRange; F9 FixedOriginRetentionStore ×2; T36 (a callable conversion's well-formedness); T62 (5 tests: invalid programs accepted with their only record lost, unsound; the three index rows may be (c)); T63 (8 tests: lost independent records; "owner: ?" for the prelude static, DictionaryStoredReference and InstanceCollector rows); F15 | fix on dev or confirm |
| R4 (A1, A4, MIR limits) | T31 (20 tests: located `Unsupported_Kd`, `StaticMovePathRequired_Kd`, `OwnershipDepthLimit_Kd` on valid programs; F10 G222); T32 (9 tests: A4 `IncompatibleContractImplementation_Kd` on valid Iterator wrappers and generic default replicas); T34's 13.7.1/0; T60 (a new `InternalInvariant_Kd`) | fix on dev |
| R4 with BU and R5 | R6-1 (ruling 2) | design, then fix |
| R5 | T61 (ruling 4) | fix on the branch |
| the owner | the (e) acceptance list: T31's two R4-M147 Gen rows, F11-F14 (6 programs: RawOrigin ×4, NestedReference, Utf8TryWrite FirstNever), T34's 7.7.2/0 (the collector's static record, P42); the (c) readings: F7 (G147 (8ii), GenericCase A4-box-ok and A4-field-ok), T35 with T14 (code after a Never call: 4 Language errors and 10 vanished limits), T62's index rows; the 1a list T10-T15 (77 tests) | decide |

No Binding-cause row blocks. BU's rows are T40's re-points, which BU reviews as a T-unit, and the consent C-8 (D1, D2).

### R4's verdicts on round 1 (2026-11-03)

R4 confirmed the MIR-cause rows. Its tables are `r4-notes/post-exit/h3b-r4-verdicts.md`, `h3b-A.md` and `h3b-B.md` in R4's scratchpad,
and their verdicts are copied here. The changes to the round's table:

- **No unsound acceptance.** T62 ×5 is 1a, not 5: SPEC 4.7.4, 15.6.2, 4.9.6, 15.6.3, 15.6.6 and 16.3.3, with X-1's (a).
- **ContentUpdate ×5 and CompilerFunctionReference are 1r**: SPEC 15.6 holders bullets 1-2, 15.6.3, 15.6.4 and R4-M95 D6. The
  dispute (ruling 3) is closed.
- **T30's self-Loan W family is (c), G148 (2) "GXS".** The MIR rejects these programs by the SPEC's letter, but they are intended valid:
  F9 ×2, InputDependentValueCall, F15, MemberThroughLayers and F8 (`count<S>` over an abstract Iterable). F15's "?" is this row. It goes
  to the owner as Q-A1.
- **F7 with PairCaseFamilies is (c), G147 (8).** R4 reads them as valid: an A5 over-approximation. Owner Q-A2.
- **T35 is class 3 and T14 is 1a.** SPEC 14.10.3 decides both with its own examples, so no (c) is needed.
- **Moved to other owners:**
  - T34's 13.7.1/0 is Binding's record. S dropped variant 0's Old side, so the row is class 3, a data fix by R5.
  - T60 is Binding's (BU-26, the input-side mirror of U-O3's r1loc), so it goes to BU with U-O3.
  - F14 is Binding's (`Binding.OriginRows`: a Never writer's formatting row), so it goes to BU as a new PLAN row, (e) until then.
  - T63's prelude static row is 1x, and its InstanceCollector rows are 1x ×2 and 1a ×1. All are R5's re-points; the InstanceCollector
    rows need BU's consent (BU-60).
  - T63's NeverContinuation row is 1a (R5-M159's twin rule).
- **Class 2 (b) rows R4 fixes on dev**, each ≤ 1 h, with the full ledger before review, landing in R5's windows:

  | Unit | Rows | Fix |
  | --- | --- | --- |
  | U-B1 | T31b, F10 (G222) | A1 `MirMoveData.Resolve` (running) |
  | U-A2 | T36 | the Fixed meet's entailment, `MirOrigins.Chains` |
  | U-A3 | T63 DictionaryStoredReference ×2 | A5's activation seed set |
  | U-B2 | T32b | A4's Requirement-call branch on a concrete Self |
  | U-A1 | T30 ClosureReceiverClassification ×4 | a receiver store's W through a shared capture |
  | U-B4 | T32a IteratorOriginEffects ×6 (G220) | design first, after G264 U1; a new MIR form is the owner's O1 |

  These go after H5 under the owner's (e) unless they land first:
  - U-B3: T31a, F11 and F12 (PQ-10, over an hour);
  - U-B5: T31d (A5's depth bound 32 → 64);
  - F13: G216 (b), the front's U-O3;
  - F14: Binding's row above.
- **T31c** (truncate/removeAll Gen) is (e) already decided (R4-M147 D″). The test is re-pointed citing it.
- **R4's lane order:**
  1. U-B1 with U-A2.
  2. U-A3 with U-B2.
  3. R6-1's builder and A3.
  4. U-A1.
  5. U-B4.
  6. The GXS unit, after Q-A1.

### The owner's list (board O-1)

| # | Question | Rows | Recommendation |
| --- | --- | --- | --- |
| 1 | Q-A1, G148 (2): adopt GXS with its extension? This is a SPEC 15.6.4 revision through the full process. Under it, a store past layer 1 leaves out the direct Loans that name its target. The extension covers exclusive-element collections and the rule that an abstract part of a holder never refers into the holder's own Loan from a shared borrow. The alternative is to keep SPEC and list the rows as (e). | F9 ×2, InputDependentValueCall, F15, MemberThroughLayers, F8 | adopt (R4); until its unit lands, the rows are on H5's (e) list |
| 2 | Q-A2, G147 (8): are F7 (A4-box-ok, A4-field-ok) and PairCaseFamilies valid? | 3 programs | valid (R4): an A5 over-approximation, fixed later, (e) for H5 |
| 3 | Do H5's long fixes wait, or go on one (e) list with STATUS limits and PLAN rows? | PQ-10 (T31a ×7, F11, F12), the depth bound (T31d ×2), G216 (b) (F13), F14, G220 (T32a ×6), SPEC 7.7.2/0 (the collector's static record, P42), and the rows of questions 1-2 until their units land | one list (R5): H5 waits only for the ≤ 1 h fixes (U-B1, U-A2, U-A3, U-B2, U-A1, R6-1, T61) |
| 4 | The 1a list: accept the programs that the old analysis's limits or false errors rejected | T10-T15 (77 tests), T62 ×5, T14, NeverContinuation, InstanceCollector's frame row | accept |
| — | For information: the 1r list (unsound acceptances the MIR closes) | F1-F6, T01-T04, ContentUpdate ×5, CompilerFunctionReference | — |

### The owner's answer (2026-11-03)

The owner answered all four questions of the list above with 「推奨どおり」 ("as recommended"; board O-1). What each answer means:

1. **Q-A1: GXS is adopted with its extension.** R4 runs it through the full process: the proposal and the SPEC 15.6.4 revision with its
   tests, then the unit after U-A1. Until it lands, F9 ×2, InputDependentValueCall, F15, MemberThroughLayers and F8 are on H5's (e) list.
2. **Q-A2: F7 (A4-box-ok, A4-field-ok) and PairCaseFamilies are valid.** A5 over-approximates there. R4 fixes it later, and the three
   are on H5's (e) list until then.
3. **One (e) list for H5.** H5 waits only for the fixes of an hour or less: R4's U-B1, U-A2, U-A3, U-B2 and U-A1, R6-1, BU's G409
   (T60) and R5's T61. These go on the list instead:
   - PQ-10 (T31a, F11, F12);
   - the depth bound (T31d);
   - G216 (b) (F13);
   - G410 (F14);
   - G220 (T32a);
   - SPEC 7.7.2/0 (P42);
   - the rows of answers 1 and 2.

   H5's Rec unit records the list in STATUS, as limits, and in PLAN rows.
4. **The 1a list is accepted.** T10-T15, T62 ×5, T14, NeverContinuation and InstanceCollector's frame row become T-units. The frame
   row's re-point needs evidence that the program is really accepted (BU-60's condition).

### After the owner's answer: U-A2 moves to the (e) list (2026-11-03)

R4 found that T36 (U-A2) is not a fix of an hour or less (`r4-notes\post-exit\state-h3b-ua2.md`):

- The MIR forms a body row for a nested signature's result relation and judges it under the body root's oracle, which lacks the nested
  inputs' premises.
- Binding judges the same relation correctly where it is written (R4-M162).
- The fix belongs to U-O3's class: such a row forms no body row. That is BU's R7-O3.

Under the owner's third answer (one (e) list; H5 waits only for the fixes of an hour or less), T36 joins F13 under G216 (b) on the
(e) list. R4's H5 blockers are now U-B1, U-A3, U-B2, U-A1 and R6-1's builder and A3 commit, beside BU's G409 (T61 has landed on the
branch).
- **U-A1 also moves to the (e) list** (R4, 2026-11-03, `state-h3b-ua1.md`). T30's ClosureReceiverClassification ×4 rows belong to G148 (2)'s
  self-store family. The Exclusive call `f(y)` stores the receiver's Content into two E nodes, which leaves the shared capture's Loan where
  it meets `g`'s StorageDead. The SPEC texts pull apart: 15.6.4's per-argument letter against 15.8.2's "retains only in its var bindings
  and the referents of its captured exclusive references". GXS as written does not recover these rows, so GXS's proposal (owner-adopted)
  takes them in and reconciles the two clauses. R4's H5 blockers are now U-B1 (Q-8), U-A3, U-B2 and R6-1's builder and A3 commit,
  beside BU's G409.
