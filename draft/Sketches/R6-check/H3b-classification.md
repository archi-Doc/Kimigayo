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
