# X-5: sampled final review of R4's MIR analyses (independent reviewer, 2026-11-03; read-only)

Base: `origin/dev` 3283d9aaa (read from a `git archive` copy; Mir/Analysis differs from the census base 5b1777f34 only by 7ec8030fd).
Ledger: `artifacts/verify/shared-base/1eb0cbaa9/mir-diff` (raw-44232.tsv, sources/). No build, no run, no worktree.

## Verdict: ACCEPT for the exit

No blocking finding in the sample: the 10 triage verdicts hold, every sampled rule has MIR-layer error and valid cases, the sweep
found no unsound acceptance and no hidden failure. Conditions C1-C5 below are facts X-6's records must state as they are; a record
claiming otherwise would be untrue (that, not the code, would block).

## 1. Rules (App. A; 9 sampled, Origin stratum 7 and 13, effect rule 12)

| # | Rule (SPEC) | MIR-layer tests | Err | Valid | Note |
| --- | --- | --- | --- | --- | --- |
| 4 | Replacing a holder ends its Loans (15.6, 15.6.3) | `MirBorrowsTest.DestroyingAMovePathEndsTheLoansItPrefixes` (k29a, k34a / k23a, k34c); `Prototype.TheBaseCasesKeepTheirVerdicts`, `TheOuterReplacementCasesKeepTheirVerdicts` | yes | yes | |
| 7 | Origin relations by variance; declared, substituted; well-formedness per case (15.6.1) | `MirOriginsTest.Proofs.OriginProofFamiliesHaveTheirOutcomeOnTheMir` (families with and without `UnprovenOriginRelation_Kd`); `MirOriginsTest.Unknown.APairLayersWellFormednessIsUnknownOnlyInItsCase`, `ARelationBindingJudgesDirectlyIsNotTheStratums` | yes | yes | Variance itself is Binding's verdict until R7 (D1); no MIR test names it (census 15.3.5, 15.3.6). N3 |
| 12 | Effect summaries against live Loans; mutable-static anchors (15.6.4) | `MirBorrowsTest.Rooted.ACallsEffectsMeetTheLiveStaticLoansRooted` (e1, e3, e4, e4c, e6, e11, adapted, forWriter, e15 / e2, e7, forReader, e11b, e14), `ADestructionMeetsItsOwnStaticAndTheLiveStaticLoans`, `AWidenedEffectMeetsTheLiveStaticLoans` | yes | yes | On hand-built MIR only: the source twin `ACallsEffectsMeetTheLiveStaticLoans` asserts Empty, since Binding rejects every static Borrow until P42 (PQ-1, BU-25), a recorded limit |
| 13 | Judging relations; chains; Refuted and Unknown (15.6.5) | `MirOriginsTest.AFiniteChainIntoAFixedOriginIsRefutedAtItsLastEdge` / `AValidChainHasNoOriginProblem`; `Unknown.AChainBetweenFixedOriginsThePremisesDoNotDeriveIsUnknown` / `ARelationThePremisesDeriveHasNoUnknown` | yes | yes | |
| 14 | Destruction observation (15.6.6) | `MirBorrowsTest.AnObservingDestructionIsAnExclusiveDeepUse` (o1566, o24a, o29b / o24b, o27), `AResultIsDestroyedBeforeTheTemporaryItBorrows` | yes | yes | |
| 15 | Reservations: two-phase, activation, abandonment (15.6.7) | `MirBorrowsTest.AReservationIsSharedUntilItsActivation` (r10b, r10d, r28a, r28d, r31b, sorted / r10a, r10c, r28b, r28c), `AReservationMeetsEachLoanOnce`; `Continuations` r2 / r3 (abandoned) | yes | yes | |
| 22 | Unreachable code: assumed state; abandoned comparisons end their Loans (14.10.3) | `MirBorrowsTest.LoansFlowOverJoinsLoopsAndCheckOnlyCode` (afterReturn), `Continuations.ALoanStaysLiveIntoANeverFormsContinuation` (errors with reachable twins; valid ad4m, r3, d1v, d1x, s4-s6) | yes | yes | The abandoned comparison has no Loan-stratum test (2x-T7 asked for one): only the build test `MirBuildTest.Calls.TheNeverValueValidatesWhereOnlyTheChecksContinue` and emission tests (`ElementBorrowEmissionTest` "Comparison"). N2; F10 is not Landed (C1) |
| 24 | `@raw` checks like an immediately ending `@ref`; fresh anchor (5.2.2, 5.4) | `MirBorrowsTest.Anchors.PlacesDerivedFromTheAnchorAndSafeBorrowsStillConflict` / `ARawPlaceBorrowsLoanMeetsNoAccessOfThePointersHolder` | yes | yes | Anchor clause only. The first clause (`x@raw` under a live `x@uniq` conflicts; `x@raw` leaves no Loan) has no MIR pair; the code classes Raw as SharedBorrow (`MirBorrows.Accesses.cs:97-106`). N4 |
| 27 | Comparison inspection Loans (13.4) | `MirBorrowsTest.ADagComparisonsInspectionLoanRejectsAMoveOfItsOperand` (bad / good) | yes | yes | A DAG Tuple of strings only; a plain `s == take(s@move)` has default-pipeline twins (`ComparisonOperatorTest`, `ElementParameterMoveEmissionTest`) but no MIR row. N5 |

## 2. Triage sample (5 R, 5 a): all 10 verdicts confirmed

| Raw id (hash) | Verdict | Check |
| --- | --- | --- |
| 44232.119 (7ad4db660251) | R | `a[0] = "bad"` inside `a[...] = "new"`'s index: old twice (Place, statement), MIR once at the statement (L159 both); one Loan |
| 44232.470 (a132a2bc7bee) | R | Milestone 15 without the else: MIR A1 `UninitializedPlace_Kd` at `current@ref` (933+11), inside the old IfKoto span |
| 44232.826 (61756325b081) | R | n ends at two exits while o lives: old at each exit, MIR once at `let n = 5` (L1231); one Loan. N6 |
| 44232.1804 (f7322971d2c5) | R | `use(values[0]@uniq, values[1]@uniq)`: old 3 records, MIR one `CallReservationConflict_Kd` at the call (L2745); one Loan pair |
| 44232.9/L10 (4d4f9d5a7c7d) | R | `pair.left@ref` after `pair.left@move`: MIR A1 `MovedPlace_Kd` at 208+13 |
| 44232.109 (34484a6588e8) | a | `a.1[(... a.0@move ...)]`: only a.1 is held (4.7.4); Tuple elements are disjoint (15.6.2) |
| 44232.589 (c7ce9b1b89fa) | a | fill's `h.p = Raw.allocate(1)` through its uniq parameter; no Loan is formed in fill |
| 44232.782 (af43dcd8ffce) | a | `insertOrReplace(...) -> Option<V>` (Dictionary.kimi:47, no `during self`): `item@follow = 42` meets no Loan on entries |
| 44232.368/L511 (2ce7c2996be7) | a | `replace(slot@uniq, with: old)` after `old = change(slot@uniq, ...)`: change's `-> T` keeps no Loan on slot (15.6.4); the real conflict at `old@follow = 4` pairs |
| 44232.726/L1086 (1720228ea1c3) | a | `origin r outlives x.s` keeps no Loan of w live (App. A rule 3) |

Paired failure removed (X-5 row): not run. For .119 and .1804 the removal removes the old record too; .470 and .9 become the valid
milestone or program; for .826 one exit removed leaves the other, and the MIR's record sits at n's storage, so it should remain
(unverified, U1).

## 3. Symptom-fix sweep of `Mir/Analysis/**`

None blocking. No try/catch, `Supports*` gate, shape allowlist, Koto or `KotoKind` read, or try-then-fallback chain.

- S1 A mode bool, `checks` (solve vs checks replay), threaded through seven members to gate the limit/deep facts:
  `MirBorrows.Solve.cs:85` (Carry), `MirBorrows.Flow.cs:22` (Plan), `MirBorrows.Calls.cs:68, 126, 160, 175, 193` (Call, Keep,
  Received, RecordValue, Solved; gates at :138-149 and :226).
- S2 A bool turning a query into a mutation: `MirBorrows.Causes.cs:124` `Covers(found, adding)`.
- S3 Silent default: `MirBorrows.Values.cs:44` `default: return (0, 0);`. Correct today (UnaryOp, Discriminant, Len, scalar Casts and
  BinaryOps), but a new MirRvalueKind or MirCastKind would carry no Loan silently; a whitelist with `InternalInvariant_Kd` closes it.
- S4 Catch-alls to Limit (a located `Unsupported_Kd`, the allowed form, but unpinned, census E1/E6): `MirEffects.cs:124`,
  `MirEffects.Solve.cs:241`.
- S5 Duplication candidate: StaticEnsure decided by callee id in two analyses, `MirEffects.cs:190` and `MirBorrows.Effects.cs:186`
  (both read `MirProgram.StaticInit`).
- `MirBorrows.Calls.cs:270` (`?? written.All`) is R4-M154's documented upper bound, not a hiding fallback: no action.

## 4. Contracts and structure limits

- All 15 entry files begin with a contract (Owns, Input, Output, Must not). Invariants are stated as rules, unlabeled, in MirAnalysis,
  MirDataflow, MirDrops, MirEffects, MirInit, MirLints, MirMoveData (N7).
- Stale: `MirBorrows.CallRows.cs:24` "Removal: per-case callee acquisition (PLAN G13)" and STATUS line 51 "(PLAN G13)": no G13 row in
  PLAN or PLAN_HISTORY (DECISIONS 4014, 4294, 5147 cite it) (C4). `MirReport.cs` head "unpublished until R6": R6-gen is done,
  publication is R6-check (N8).
- Limits (approximate script): no file over 5,000 tokens (largest MirOrigins.Chains.cs, about 4,370), no line over 160, no member over
  6 parameters or 1 bool, no control nesting over 4. Statements per member not measured.

## 5. Census sample (5 producers)

| # | Site on 3283d9aaa | Located | Pinned | Owner | Stage |
| --- | --- | --- | --- | --- | --- |
| M1 | `MirBorrows.Reports.cs:55-58` | yes | `Closures.AViewWhoseCaseAdmitsLessThanTheCallAcquiresIsHeld` | R4 | "PLAN G13", dangling (C4) |
| M2 | `MirBorrows.Locations.cs:61-64`, marked at `Addresses.cs:114` | yes | `Raw.ABorrowOfADuringSelfLayerIsUnsupported` | R4 | condition only (Addresses.cs:16-17) |
| M3c | `MirBorrows.Calls.cs:226-230` | yes (the call) | none | R4 | none |
| E5 | `MirEffects.Solve.cs:208-211` | yes | `MirEffectsTest.SeveralCallableSignaturesAreALimit` | R4 | none (2f-2 replaced) |
| O1 | `MirOrigins.Chains.cs:66` to `:571-575` | yes | `MirOriginsTest.AnUnclassedEndIsTheLocatedLimit` | R4/BU | G216 (b) |

The census's sites hold at this base. X-2's done condition (an owner-approved stage per producer) is not met for 25 producers (C3).

## Conditions on X-6's records

- C1 Landed has 10 entries (Binding, A1, A3, A4, F1-F5, F9: `VerifiedSpecExampleTest.cs:21`, summary.md). F6, F7, F8 and F10 are
  not landed, and F7's 3 and F8's 10 spec cases are never checked on the MIR. OQ5 (ii)'s "Landed = 14" must not be claimed.
- C2 The ledger at 1eb0cbaa9 still shows 105 open blocking rows inside 158 open (b) signatures (1,502 rows). X-1's verdicts (R 73,
  a 17, d 15, X 0) are scratch notes: the record names them as the closure evidence, not a ledger count of 0. X-1's uncompared-code
  list (TransferRequired, the lints, `OwnershipCaseLimit`, `IsEffectRecord`'s records, with stand-in tests) is not in the exit notes.
- C3 The 25 no-stage producers (census section 4) are listed as DEC O4 / OQ9 items with the owner's approval, not as staged.
- C4 M1's removal names the missing PLAN G13: give it a row or name the right one.
- C5 G124: the MIR accepts where PLAN G124 says "a located `Unsupported_Kd` on the MIR" until 2b-5: an unsound acceptance on both
  pipelines (reproducer unconfirmed). STATUS's MIR boundary says so.

## Non-blocking (post-exit fix list)

- N1 S1-S3 (mode bools, silent default) and S5's duplication.
- N2 Rule 22: a Loan-stratum error and valid pair for an abandoned comparison (2x-T7), then F10 in Landed with a variant.
- N3 Rule 7: a MIR test of variance-derived relations once R7 moves the verdict from Binding.
- N4 Rule 24: `x@raw` under a live `x@uniq` (error) and a write after `x@raw` (valid) at the MIR layer.
- N5 Rule 27: a plain Non-Copy operand row (`s == take(s@move)`) beside the DAG case.
- N6 44232.826: the record at `let n = 5` merges two exits' StorageDead into one subject; a location choice, but SPEC 23.1.2 may want
  one per exit.
- N7 Label the Invariants in the seven contracts; N8 MirReport's "until R6".
- N9 The 18 unpinned producers (census section 5), S4's two first.
- N10 Two library assertions, `MirAnalysisTest.TheLibraryHasNoAnalysisProblem` and `MirCensusTest.TheLibraryHasNoProblems` (PD22
  wanted one); they differ since R5-M168, so confirm both are intended.
- N11 Half 2's old-rule pin `TypeDagScalingTest.ARingThroughAHandleRetainsTheLoan(true)`.

## Not covered by this sample

A3 E1-E14 and ER-*, prototype parity, V16, PD31 fact rows per code, the DIAGNOSTICS A6 contract, the d rows, and the X-5 row's
Session, mir measurement and milestones (no run). U1 above stays unverified.
