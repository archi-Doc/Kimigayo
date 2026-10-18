# A5-F4 boundary census (unit 2a0-0)

Date: 2026-10-19. Base: `origin/dev` `48a42168`. Unit row: plan §4.2 2a0-0
([A5 step 2 plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md)); decisions PD8, PD9, PD10; inputs: the
A5-F4 audit (`artifacts/handoff/2026-10-18-pause/r4-scratch/step2-audit/F4/audit.md`), A3 §7, SPEC 15.3.5–15.3.6, 15.5, 15.6.3,
15.6.5, 5.2.2, 8.11.3. Evidence only (DEC Q6 form 3): it changes no SPEC, compiler, library or test file. "(H)" marks an
inference not checked by running code.

## 1. Method

**Inputs** (default pipeline: `Bind`, the startup check, `Analyze`):

| Set | Inputs | Source |
| --- | ---: | --- |
| library | 1 | `func main() => ()`; only library nodes counted |
| milestone | 44 | `tests/milestones/Milestone*.kimi` |
| golden | 31 | `tests/mir/*.kimi` |
| spec | 63 | every `tests/diagnostics/spec-cases.json` variant (28 accepting), `DiagnosticCorpus.Source` |
| fixture | 5,498 | the sources of all 5,949 native fixtures of the base suite (5,498 distinct), captured at `WriteFixture` |
| ledger | 1,901 | supplementary: `bin/worktrees/a5-1b5/artifacts/verify/mir-diff/a5-1b5-b1/sources/` (806110fc); 1,900 reproduce their hash |

Beyond these sets, the whole xUnit suite (21,085 tests) ran under each variant below; it reports assertion changes, not records.

**Instrumentation** of a scratch copy of the base (`git archive 48a42168`; no tracked file changed):
[`f4-census-probe/instrumentation.patch`](f4-census-probe/instrumentation.patch) adds the static `F4Probe` and these events,
each guarded by `F4Probe.Enabled`, which only the probe test sets:

| Event | Hook (base file:line) | Counts |
| --- | --- | --- |
| `independent`, `skip` | `PublishesIndependentResults`, `PathReborrowOrigin` (`B/Binding.ArgumentOperations.cs:563-668`) | bodies the skip covers; each skip with the Origin with and without it (`gains` when they differ) |
| `mm-chain`, `gate985`, `mm-unbound`, `mm-dependent` | `RecordMismatch`, `FailingOperand`, `AddFailingChains` (`B/Binding.Diagnostics.cs:920-1190`), `JudgeLocalRegion` (`B/Binding.LocalRegions.cs:434`) | every chain Binding records at a fit, its end classes, and whether its judgment walked `LocalRegionSources` |
| `t1`, `t1-row` | `CheckTypeOriginRelations`, `AddTypeClauseObligations` (`B/Binding.OriginProof.cs:273-332`) | Type clauses judged as Constraints; Type-clause rows |
| `anchor-proof`, `anchor-bound` | `ProvesOriginOutlives` outside candidate trials (`B/Binding.OriginProof.cs:204`), `RecordRegionBound` (`B/Binding.LocalRegions.cs:372`) | relations and region bounds with an `Anchor` end |
| `gate79` | `ReportJudgedOriginObligation` (`A/OwnershipAnalysis.Borrows.cs:74-79`) | the Unrepresentable gate's records |

The probe test [`F4CensusProbe.cs`](f4-census-probe/F4CensusProbe.cs) also writes every old-path record (`LegacyRecords.Of`),
every extra Binding Origin record (`callRelations`) and every `Binding.Obligations` row with its stored-end classes and
`JudgeOriginObligation` verdict. Variants: `F4_NOSKIP=1` returns the walked Origin (no skip); `F4_CALLBORROWS=1` lets the old
reporter judge `CallBorrows` rows (`A/OwnershipAnalysis.Borrows.cs:41`).

**Behavior preservation.** The instrumented default run's records and rows equal those of a build with only the probe test, the
inert `F4Probe` class and the `callRelations` accessor, on library, milestones, goldens and spec; the instrumented copy passes
the whole suite (21,085 tests, 0 failed), allocation tests included.

**Commands** (scratch copy `C`; `S` the probe folder of this unit):

```
git archive 48a42168 | tar -x -C C && cd C && git apply S/instrumentation.patch && cp S/F4CensusProbe.cs tests/xUnitTest/Tests/
dotnet restore tests/xUnitTest/xUnitTest.csproj && dotnet build tests/xUnitTest/xUnitTest.csproj -c Release --no-restore
cd tests/xUnitTest/bin/Release/net10.0
F4_FIXSRC=<fix> KIMI_FIXTURE_DIRECTORY=<ll> ./xUnitTest.exe          # fixture sources; then deduplicate by content
F4_SETS=library,milestone,golden,spec,fixture,ledger F4_FIXDIR=<fix> F4_LEDGER=<ledger sources> F4_OUT=default.tsv \
  ./xUnitTest.exe -method XunitTest.F4CensusProbe.Census             # again with F4_NOSKIP=1 and F4_CALLBORROWS=1
./xUnitTest.exe                                                       # whole suite, per variant
python -I S/census.py <out> <a5-1b5-b1 raw-13972.tsv>
```

Outputs are kept in this worktree's `artifacts/verify/2a0-0-census/` (git-ignored): the three TSVs, `census.txt`, the suite
failure lists, and the uninstrumented `base.tsv`.

## 2. Results

Counts are by set as library / milestone / golden / spec / fixture / ledger; "0" means zero in every set.

**(a) The containment skip** (PD9; SPEC 15.6.3, 8.11.3, 15.5).

- Bodies the skip covers: 12 / 3 / 0 / 0 / 66 / 82. The 12 library bodies are exactly the 12 Iterator `next` bodies:
  `RangeIterator`, `ClosedRangeIterator`, `OwningIterator`, `BorrowingIterator`, `SliceIterator`, `ArrayIterator`,
  `ArrayUniqIterator`, `ArrayOwningIterator`, `FixedArrayOwningIterator`, `DictionaryIterator`, `DictionaryUniqIterator`,
  `DictionaryOwningIterator`. Milestones: Milestone13 `Cursor.next`, Milestone29 `Drain.next` and `Cursor.next`.
- Skips that fire: 8 sites in 8 library bodies, 4 in Milestone29 `Drain.next`, 32 in 29 fixture bodies, 55 in 37 ledger bodies.
- Gaining an edge without the skip: of the 12 library bodies only `BorrowingIterator.next` (`Kimi/Iteration.kimi:59`
  `self.iterator`: `source` becomes `source and step`); the other seven firing bodies Reborrow `self.state` and already carry
  `self`'s Origin. Milestones 0; fixture 1; ledger 30 sites.
- Verdict changes without the skip: library, milestones, goldens and spec 0; fixture 1, `AssociatedIteratorImplicitShared`
  (accepted; then `UnprovenOriginRelation_Kd` at `let alias: ref/i32 during a = self.value`); ledger 16 programs, each already
  rejected by `IncompatibleContractImplementation_Kd` and gaining one `UnprovenOriginRelation_Kd` at its Reborrow. Whole suite:
  20 failures in 4 classes, 18 asserting acceptance (`IteratorIndependenceTest` 10, `IteratorOriginEffectsTest` 5 with the
  fixture's `SharedImplicitReborrowsAllowRetainedItems`, `IteratorIndependenceEditingTest` 3) and 2 asserting an exact issue
  set (`VirtualEffectSelectionTest.ReceiverPositionDoesNotHideEarlierArgumentEffects`).
- The form is a shared Reborrow through an exclusive field reached through the exclusive receiver, returned at the field's
  Origin: `let value: uniq/i32 during source` … `func next(self: uniq/Self) -> Option<ref/i32 during source> => .Some(self.value)`
  (`IteratorIndependenceTest.cs:75`). SPEC 15.6.3 contains it in `source` and `self` (both layers exclusive), so `during source`
  needs `self outlives source`: Unknown, the shared twin of SPEC 15.5's `detach`. SPEC 8.11.3 exempts no body from this; its
  eligibility judges the signature and its guarantee binds callers. These are acceptances SPEC rejects as Unknown, not Refuted
  (§3).

**(b) Inner-chain records** (PD8, OQ11; SPEC 15.6.5). `RecordMismatch` recorded 181 chains at 170 nodes in 161 programs, all in
spec and ledger (0 in library, milestones, goldens, fixtures):

| Chain | Chains / nodes | Owner by A3 §7 |
| --- | ---: | --- |
| fixed–fixed, no local region | 130 / 124 | Binding until R7 |
| finite (`Projection`) to fixed, no inner node | 41 / 41 (spec 3: 15.6.5 `bad`, 15.8.2 ×2) | no row exists (§4 finding 1) |
| judged through `LocalRegionSources` (`JudgeLocalRegion`) | 10 / 7 (7 Refuted, 3 more Unproven) | A5 (PD8) |

The 7 nodes are ledger programs where a call's inferred Origin carries recorded bounds, for example `03a1c944ff3c`
(`return f(m@ref, q)`, `m@ref` reaching `t`), `7250ed1e9d8f` (`return t.0` after `let t = f(local@ref, x, b)`), `ebb0a96421b0`.
`FailingOperand`'s and `AddFailingChains`' local-region branches never produced a record: a local region's relations are rows,
which the old reporter judges (probe `regions.kimi`: `var r = x; r = local@ref; return r` gives row `R → I` Refuted and an
`Ownership.UnprovenOrigin` record). Binding's gate `B/Binding.Diagnostics.cs:985` and the `Unbound` branch: 0 records.

**(c) Several Origin records of one code at one node** (PD10; DIAGNOSTICS:159). 12 nodes, all ledger: 6 old-reporter nodes,
4 where one row's meet splits (`bc801917a239`: row `(a and b) → x` at `ref/(ref/i32 during (a and b)) during x` gives two
`UnprovenOriginRelation_Kd`; `a202be2cf0ac`, `ad1845eb8d07`, `ef6c09663424`) and 2 calls with several failing rows
(`33c4547d470b` 3, `e3312717bde0` 2), and 6 Binding nodes with a primary and one more fixed–fixed record (`89af9652d6a5`:
`return longer@move` reports `longer → b` and `a == b`). The 6 row nodes are A5's, so PD10's condition holds and 2a-3 runs; the
6 Binding nodes stay Binding's (fixed–fixed).

**(d) `CallBorrows` rows** (A3 B8; SPEC 15.6.4 step 3). 34 / 7 / 2 / 3 / 95 / 601, all 742 Verified by
`IsVerifiedOriginObligation`; ends fixed–fixed (709) or a stored `Unbound` end that resolves at use (33). Example:
`Kimi/FixedBuffer.kimi:2` `ref/Self`, `source → self`. Judging them (`F4_CALLBORROWS=1`) changes no record in any set and no test.

**(e) T1 occurrences** (A3 §7, audit-origins §7.1). Type clauses judged as Constraints: 27 events (fixture 14, ledger 13), all
Proven (15 at a selected call, 12 in candidate trials); 0 in library, milestones, goldens, spec. Type-clause rows: 11 (fixture 3,
ledger 8). Rows whose clause and substituted ends equal a T1 judgment in the same body: 3, fixture `LocalOriginClauseTypePremise`
(`let s2: S = s@move`, `let s3: S{w}`: clause `a outlives b` as `p → q`) and ledger `a64a4485e6f7` (`U{x}`). No T1 judgment
failed, so no record is doubled at the base.

**(f) `Anchor` ends** (PD9; SPEC 15.6.5, 5.2.2). 13 relations, all library and all `anchor → fixed`, Proven: `UniqSlice.index`
and `indexUniq` (`return element`), `Dictionary.tryGet`, `Storage.splitFirst` (6), `Storage.tryGetPairUniq` (4). Rows with an
Anchor end: 0; region bounds into an Anchor: 0; relations with the Anchor as the shorter end: 0. A chain passes an inner node
only with an edge into it, so no chain passes a raw Borrow's Origin; under SPEC 15.6.5 an inferred region at a chain's end is
Proven, so all 13 keep their verdict. Default-pipeline acceptances SPEC 15.6.5 refutes: none found.

**(g) The Unrepresentable gate** (`A/OwnershipAnalysis.Borrows.cs:74-79`). 0 records in every set. Unrepresentable needs an
`Unbound` end (`B/Binding.OriginProof.cs:137`); the one such row (ledger `565362ba10e0`, `Slice<View>{source}`) is in a
Binding-rejected program with no ownership record. All 239 `Ownership.Unsupported` old rows of a5-1b5-b1 (IdentifierNameKoto
149, MemberAccessKoto 37, GenericsKoto 20, FieldKoto 8, TypeSemanticsKoto 7, UnsafeBlockKoto 5, InvocationKoto 4, 9 others)
reproduce at the base at their location from other causes; none is the gate. The audit's "unknown U share" of A5-F4 is 0.

## 3. Verdict changes and refuted acceptances

- **Library and milestones (OQ7): none.** Without the skip, with `CallBorrows` rows judged, and with raw Borrows as inferred
  regions, no library or milestone record changes; no O3 consultation is due before 2a-1a.
- **Tests and fixtures:** without the skip 18 tested acceptances (one a native fixture) become Unknown and 2 rejection
  tests gain a record (§2 (a)). With PD9 these are A5-F4's expected MIR verdicts for 2a-4 (target sides or OldRule markers).
- **Refuted acceptances (PD9): none.** The 18 skip acceptances are Unknown under SPEC 15.6.3, not Refuted; no miscompile is shown
  (the conformance check, `IncompatibleContractImplementation_Kd`, already rejects the 16 ledger variants). Proposed PLAN row:
  "Binding's `preserves results` containment skip (`B/Binding.ArgumentOperations.cs:563-668`) accepts 18 tested forms SPEC 15.6.3 rejects as
  Unknown; no library or milestone program needs it. Remove it so both pipelines follow SPEC, with the 18 tests changed under
  15.6.3, by R6-check; or a SPEC proposal (owner)."

## 4. Findings for 2a0-D and the planning session

1. **Rows for finite ends.** 2a0-3a's predicate "a local-region or inferred end" leaves out the 41 finite-to-fixed records
   (§2 (b)), among them spec 15.6.5 `bad`, whose Unsatisfied problem 2a-2 expects from MIR; A3 §7 publishes no row for them and
   MirOrigins takes edges only from rows and containment. Recommendation: `JudgeCallPosition`'s predicate,
   `!FixedOrigin(longer) || !FixedOrigin(shorter)` (`B/Binding.CallRelations.cs:313`).
2. **OQ11's scope is small.** The relations OQ11 moves are 10 records at 7 nodes, all in test programs.
3. **Resolved ends.** 39 rows (33 `CallBorrows`) store an `Unbound` atom that `OriginAtUse` resolves; the view must store
   resolved ends (audit §2).
4. **Raw Derefs (H).** The 13 library anchor relations stay Proven only if E4's containment stops at a raw Deref (SPEC 5.2.2:
   no Loan is recovered); 2x-P4's raw-anchor case should check it.
5. **Owner question (the skip):** (a) SPEC's letter (PD9): A5 rejects the 18 forms, 2a-4 records them and Binding's skip goes;
   (b) a SPEC proposal exempting `preserves results` bodies from receiver containment, which A5 would then implement;
   (c) hold. Recommendation (a): no library or milestone program depends on the skip, and the conformance check already rejects
   the 16 ledger variants.

## 5. Not verified

- A5's own verdicts: no `MirOrigins` exists; (a) and (f) use Binding's walk and judge as a proxy for E4's containment (H).
- Programs outside the six sets were checked only through test assertions.
- T1 matching is by clause, substituted ends and enclosing body, an approximation; (b)'s region flag is per node, so the 3 more
  records at the 7 nodes may be direct chains.
- One ledger source (`6aa30aff7a03`) did not reproduce its hash; its counts are its reconstruction's.
