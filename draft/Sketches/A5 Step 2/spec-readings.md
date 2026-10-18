# A5 Step 2, unit 2x-S1: SPEC readings

Date: 2026-10-19. Base: `dev` at `48a42168`. Unit 2x-S1 of the
[A5 Step 2 Implementation Plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) (PD23, §4.1). It changes no SPEC, IMPL, compiler, library or test file, and not the tracked prototype.

Evidence, named by case id (g1, a2, …) in both files:

- `spec-readings-cases.cs`: prototype cases on a scratch copy of `A5 Step 0/A5RulesPrototype.cs`, run under the design (design A
  with E8's ending, round 13). Each case has the MIR-fact encodings ("forms") the reading compares; §7 has the run.
- `spec-readings-probes.txt`: 24 sources with the old path's records (OLD) and shadow A5's problems (MIR) at the base.

SPEC lines are at the base: S4, S8, S15 = `docs/spec/` chapters 4, 8, 15.

## Outcomes

| Item | SPEC verdict | Old / MIR today | Outcome | Owner |
| --- | --- | --- | --- | --- |
| G147 (8ii): A4-box-ok, A4-field-ok | error in the `uniq` case at `v@follow = x` | accept / error | A5 right; the 4 rows are (d), clause `15.6 15.6.4`; pair-cases rows get the SPEC verdict and an `Old` entry | 2c-7 (else X-1) |
| finding: Φ_E per holder (g5) | valid | accept / error | an unlisted A5 rejection: OQ8 item S1-a (§6) | owner |
| af43dcd8ffce | valid | error / accept | (a), clause `4.9.6 15.6.3 15.6.4`; the old test row re-spelled at R6-check | X-1; MIR test 2d-6 |
| 47f0722c876b | error at the second `f(source)`, `CallEffectConflict_Kd` | `Unsupported_Kd` / accept (unsound) | family test: `CallableEffectBoundTest` row 2 on MIR | 2f-4 (2f-3 first) |
| 59e2289ce4f4 | error at `Helpers.again(source)` | error / accept (unsound) | family test (h1, h3); the row moves from F5 to F6 | 2f-3 |
| PD16's stored Φ_c | a value the call stores into another input is never excused; transfers of the result are | p3: accept / accept (unsound) | 2x-P6 cases p1–p5; 2f-3 reports the Language code, not `Unsupported_Kd` | 2x-P6, 2f-D, 2f-3 |

## 1. G147 (8ii)

**SPEC.** S15:454: a write through a reference adds the written value's Loans to every holder it may reach; a reference also
reaches storage the body does not own "when it comes from a parameter or capture, …, or from a call result that its contract ties
to no argument, or derives from such a reference", and a value stored there may be used after every point. S15:594: "Retention is
decided per argument, never per Origin"; a result that may retain an Origin of a borrowed argument carries its Loans "and those of
such a read too". S15:453 (P1): a value read from a part carries every Loan the holder carries.

**Reading.** `Cell<s/T>.init(link: slot@uniq)` and `Box<uniq/(s/T)>.init(item: slot@uniq)` have one row, `0 Value result` (H, by
`RetentionTest`'s `init` row; not printed): by contract the result may hold what a read through the argument yields, here
`slot`'s value, a parameter reference. Whether `cell.link` derives from it is decided per argument too, so
`cell.link@follow = child@move` may store the Reborrow `child` of `v` into storage the body does not own, and `v@follow = x`
conflicts (S15:455). Reading "derives" as "is a Copy or Reborrow in this
body" is unsound: g3 has the same caller facts, but its callee `Wrap.make(link: uniq/(uniq/E during b) during a) -> Cell<E>
during a` returns `link@follow@follow@uniq`, which is `*other`, so the write stores `child` into the caller's storage. No
Type-blind rule tells g1 from g3, and S15:594 is Type-blind.

**Evidence.** Prototype g1–g4: the implemented facts (A3: `ret(i, Value)` with content) reject g1–g3 and accept the direct twin g4
(`var link = slot@uniq; link@follow = child@move`); the narrow form accepts g3, unsound. Probes: shadow A5 rejects g1 and g2 (one
view, `0x2`; the `uniq` view by PLAN G147) and g3 at `v@follow = x` and accepts g4; the old path accepts all four, g3 included.

**Outcome.** A5 is right. The (8ii) rows of a5-writes (2 programs × Source and Run) are (d) by S1015's P4 (retention per argument,
integrated at `4372b058`), clause `15.6 15.6.4`, as that row's C3 rows are; the old path's acceptance of g3 shows its rule
unsound on these facts, so (a) would close them too. `tests/diagnostics/pair-cases.json` A4-box-ok and A4-field-ok:
`Expected.uniq` = `ComparisonLoanConflict_Kd` at `v@follow = x`, Loan `let child = v`; `Old` = Rule `P4` with the current
verdicts and Run (R4-M60). Owner: 2c-7, which closes a5-writes with (8i)'s rows; else X-1. A Type guard that recovers
these forms would be a SPEC precision refinement, unscheduled like G127; not proposed (lib 0 / ms 0).

**Finding (g5, g6).** A3 carries Φ_E per holder (§5: `refs(S)` holds E when Φ_E ∈ S), while S15:454 decides storage the body does
not own per reference. With no call: `var t = (local@uniq, w2@move); t.0@follow = child@move; _ = local@follow; v@follow = 9`,
`w2` a parameter reference. `t.0` is `local@uniq` and derives from no parameter, so SPEC accepts (g5); A3 and shadow A5 store
`child` into E and reject at `v@follow = 9`. Its twin through `t.1` (g6) is an error in every form. Derivation per Move Path (form
"per part") is exact on g1–g6. An unlisted rejection: OQ8 item S1-a (§6).

## 2. af43dcd8ffce

**SPEC.** LIBRARY: `insertOrReplace(self: uniq/Self, key: K, value: V) -> Option<V>`. S4:552: added and replacing inputs'
dependencies join the collection's set; removed values carry the stored Types' full dependencies; no operation subtracts. S15:570:
a value descends from the receiver only when no other input's Type names its Origins; `value: V` does. S15:594: the result still
retains the receiver's content and `value` (rows H, from LIBRARY's text); `Option<V>` names no Origin of the receiver Borrow, so
it keeps no receiver Loan. S15:454 and S15:505 (distinct roots): a write through `item` reaches the holders its Loans name and
hits no Loan carried elsewhere.

**Reading.** Valid. `item@follow = 42` writes the old value's referent and conflicts with nothing; `entries` keeps its
dependencies, so writing their Places while `entries` lives is an error (a2). The old test's premise (no descent, so a conflict)
does not follow: without descent the result carries the same Loans by retention, and carrying a Loan never makes a write through
the carrier a conflict.

**Evidence.** Prototype a1–a3 = SPEC. Probes: shadow A5 accepts a1 (the ledger source) and a3 and rejects a2 at `x = 5`; the old
path errs at `item` in all three (and at `x = 5` in a2).

**Outcome.** Classification (a), clause `4.9.6 15.6.3 15.6.4`, at X-1 (the plan's residual Comparison/IdentifierName row).
`DependentDictionaryLiteralTest.ARemovedReferenceDescendsOnlyFromTheSoleInputNamingItsOrigins` row 3 (`:35`) asserts the old
verdict and is re-spelled at R6-check, as G147 (7)'s row is. 2d-6's MIR coverage: a1 and a3 accepted, a2 rejected at `x = 5`.

## 3. 47f0722c876b

**SPEC.** S8:843: "`preserves results` excludes conflict with Loans of earlier results of the same callable value"; S8:774:
replacing `v` is outside the guarantee; S8:856: a call through `F` uses rules 1 to 3 (S8:787–791), rule 1 "an abstract input may
reach every Loan its Origins may denote"; S8:793: a possible conflict is an error at the later call.

**Reading.** `f = two@move` replaces the callable value, so the second `f(source)` has no exclusion; through `source: uniq/S` it
may reach every Loan S's Origins denote, which `first: S.Item` may keep. An error, `CallEffectConflict_Kd`, as for `var g =
two@move; g(source)` (c3), whose old code it is. The same `f` twice (c1, 936f8571825e) is valid. S2 gives the same verdicts (its
§10: "the first row becomes valid and the second a `CallEffectConflict_Kd`").

**Evidence.** Prototype c1–c3: with rule 1's reach as a phantom on the carrier S and A3's `disturbed` (a write of the receiver) the
verdicts are SPEC's; A3 as written (Φ_c only on a descending result) accepts c2 and c3, unsound. Probes: shadow A5 accepts c1–c3;
the old path reports `Unsupported_Kd` at c1 and c2 (R1g) and `CallEffectConflict_Kd` at c3.

**Outcome.** Family test: `CallableEffectBoundTest.ReplacingTheCallableCannotReuseTheEarlierValuesGuarantee` row 2 (`:401`)
expects `CallEffectConflict_Kd` at the second `f(source)` on MIR; the default pipeline keeps `Unsupported_Kd` until R6-check. Rule
1's reach for a non-descending abstract result is 2f-D's choice (carrier reach or Place phantom). 2f-3 may report c2 as a located
`Unsupported_Kd` if its "only rule 3 could excuse" test does not see the replacement; 2f-4's invalidation gives the Language code.
The ledger row then differs by code only.

## 4. 59e2289ce4f4

**SPEC.** S15:450: the Loans a body receives through its parameters' Origins "are checked where the body is called, against what
the call may reach (§8.11.4)"; S8:789: through an abstract input a call may reach every Loan its Origins may denote; S8:787:
bounds come from the premises, "never bounds of implementations"; S8:677: a non-virtual function declares no bound; S8:774: other
requirements are outside the guarantee.

**Reading.** `Helpers.again(source)` passes `source: uniq/S`, abstract at the caller, so the call may reach every Loan S's
Origins denote, which `first` keeps by descent (S15:570, P12). Nothing excludes them: `again` is not `take`, and its body is not
its contract. An error at `Helpers.again(source)`, as the old path and `EffectBoundCallerTest.cs:20` say. The F6 audit's
alternative (only the argument's Borrows) is unsound: a helper calling `value.reset()` (h2) has the same caller facts and would
launder the error that the direct `source.reset()` (h4) reports. A helper under `T is StableSource` (h3) gets the same verdict.

**Evidence.** Prototype h1, h4, h5: without desc(c) (MIR today) h1 and h4 are accepted, unsound; with A3's desc(c) both are
rejected (at the argument's reservation and the call; the callee is not `take`, so nothing excuses) and takeTwo (h5) is valid.
Probes: the old path rejects h1–h4 with `CallEffectConflict_Kd` and accepts h5; shadow A5 accepts all five.

**Outcome.** Family test in 2f-3's `MirBorrowsTest.Preserves`: h1 and h3 errors, `CallEffectConflict_Kd` at the call (2f-D's A6
row). The row leaves 5e1a449cc7bf's F5-filed signature at 2f-6's split, as the F6 audit proposed.

## 5. PD16's stored Φ_c

**SPEC.** S8:774: a later call "conflicts with no Loan kept by a result of an earlier call …, including Loans kept after transfer
or Reborrow of that result"; S15:452 separates a call's results from "the writable storage its public contract lets it write";
S15:455: checks are decided by the values that carry Loans, "never a Loan's identity"; S8:791: rule 3 excludes "the Loans kept by
earlier results".

**Reading.**
1. A value the earlier call itself writes into another input (`takeInto(self, spare: uniq/Option<Self.Item>)`, row
   `0 Content store 1`) is not a result. A later call that may reach its Loans conflicts (p3; p3b, without a bound). Excusing it by
   the result's identity would let an implementation re-lend the stored item, since conformance compares only with results.
2. A value the caller derives from the result keeps the exclusion, moved into a holder (p1) or stored by another call
   (`Stash.put(held@uniq, first@move)`, p2): "after transfer".
3. A `uniq/Self` receiver also has the row `0 Content store 0` (H, by `RetentionTest.cs:126`'s `item` row). What the call stores
   into its own receiver is the receiver's state, which the later call borrows exclusively. A phantom stored there would reject
   takeTwo (S8:687), 22.2.3's nextPair and takeLate (`EffectBoundCallerTest.cs:41`).

**Evidence.** Prototype p1–p5 in four forms: only "own id" (the call's store rows carry a distinct phantom Φ_c^s, never excused,
and the receiver's own row carries none) matches SPEC on all six. "none" (A3 round 9) accepts p3 and p3b, unsound; "result id"
accepts p3, unsound, and rejects p4; "own id + self" rejects p1, p2, p4 and p5. Probes: both paths accept all six, so p3 and p3b
are unsound acceptances on both; no test has the form, so no ledger row.

**Outcome.** PD16's interim ends: a conflict only a call's stored Φ_c^s causes is a Language error (`CallEffectConflict_Kd`) in
2f-3, and 2f-4 never excuses it. 2x-P6 takes p1–p5 as derived cases (its stored-Φ_c item); 2f-D fixes the phantom's identity and
the receiver-row exclusion; 2f-3's stored-Φ_c test is p3 with p3b.

## 6. Owner question

**OQ8 S1-a, storage the body does not own per holder.** A5, since 1b, carries Φ_E per holder (A3 §5); S15:454 decides it per
reference. A5 rejects g5, which SPEC accepts. Count: lib 0 / ms 0 (the 1b-5 census, PLAN), no row in the a5-1b5-b1 ledger.

- (a) Approve it as a loss. S15:454's sentence ends "…, or derives from such a reference: a Copy or Reborrow of one, a value a
  call may retain from one, through a read of a borrowed argument included (§15.6.4), or a value read from a holder that holds
  one". The call clause states what §1 reads; the holder clause is the loss and takes P1's one carrying unit. One SPEC session
  before X-1 (A5 reports the code in shadow since 1b), then a MIR test (g5 an error; g4 and g6 controls).
- (b) A representation: Φ_E per Move Path (the "per part" form), a second carrying unit beside P1's holders in `MirLoanSets` and
  A5's stores.
- (c) Hold: g5's form stays an unlisted rejection at R4's exit.

Recommendation: (a). No SPEC example, library or milestone program has the form.

## 7. Prototype run and findings for other units

| Group (forms) | Case: SPEC → each form |
| --- | --- |
| G147 (8ii) (A3, narrow, per part) | g1, g2, g3: error → error, valid **UNSOUND**, error · g4: valid → valid ×3 · g5: valid → error (over-strict) ×2, valid · g6: error → error ×3 |
| af43dcd8ffce (design) | a1: valid → valid · a2: error at `x = 5` → same · a3: valid → valid |
| 47f0722c876b (A3, carrier) | c1: valid → valid ×2 · c2, c3: error → valid **UNSOUND**, error at the second call |
| 59e2289ce4f4 (today, desc) | h1, h4: error → valid **UNSOUND**, error · h5: valid → valid ×2 |
| stored Φ_c (none, result id, own id + self, own id) | p1, p2: valid → valid, valid, error, valid · p3: error → **UNSOUND** ×2, error ×2 · p3b: error → **UNSOUND**, error ×3 · p4: valid → valid, error ×2, valid · p5: valid → valid, valid, error, valid |

The forms chosen by the readings (A3 for G147 (8ii) except g5, the design, carrier, desc, own id) have no unsound acceptance;
g5 is the reported rejection. Run: the header of `spec-readings-cases.cs` (`dotnet run proto.cs -- -s2x1`, .NET SDK
11.0.100-rc.2, about 30 s). The hook adds a flag only, so every earlier case runs unchanged.

Findings for other units (nothing changed):

- 2f-6's completion condition names 936f8571825e "rejected on MIR"; S8:843 and S2 make it valid (c1), so after 2f-4 its old
  `Unsupported_Kd` row is the old path's limit (R1g), to be classified rather than paired.
- 2f-3's "desc(c) … through W into the storage its store rows write" would store Φ_c through the receiver's own row (§5, 3) and
  reject takeTwo and takeLate; 2f-D excludes that row.
- Old-path unsound acceptances, frozen until R6-check: g3, g6, p3, p3b.

## 8. Not verified

- The `0 Content store 0` row of an abstract `uniq/Self` requirement (take, takeInto) is inferred from RetentionTest's `item`
  row, not printed (H).
- The prototype's excuses and disturbances are hand-given, as in rounds 7–10; 2x-P6 derives them. The carrier phantom is one
  candidate encoding of rule 1's reach (2f-D).
- g3's alias of `*v` in caller storage is read, not run; the probe shows Binding complete.
- S1-a's lib/ms count rests on the 1b-5 census; 2x-T1 re-measures it.
- The codes and locations A5 will report for c2, h1 and p3 are 2f-D's.
