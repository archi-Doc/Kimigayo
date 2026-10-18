# A5 Step 2, unit 2x-S1: SPEC readings

Date: 2026-10-19, revised after the independent review. Base: `dev` at `48a42168`; PLAN G148 is cited at `d55bce08`. Unit 2x-S1 of
the [A5 Step 2 Implementation Plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) (PD23, §4.1). It changes
no SPEC, IMPL, compiler, library or test file, and not the tracked prototype.

Evidence by case id: `spec-readings-cases.cs` (prototype cases in the MIR-fact "forms" a reading compares, §7) and
`spec-readings-probes.txt` (OLD and shadow-A5 records of 28 sources; r1–r4 from the review). S4, S8, S15: `docs/spec/` ch. 4, 8, 15.

## Outcomes

| Item | SPEC verdict | Old / MIR today | Outcome | Owner |
| --- | --- | --- | --- | --- |
| G147 (8ii): A4-box-ok, A4-field-ok | error in the `uniq` case at `v@follow = x` | accept / error | A5 right; 4 rows (d), clause `15.6 15.6.4`; pair-cases rows get `Old` | 2c-7 (else X-1) |
| S1-a: Φ_E per holder (g5, r3) | valid by S15:454's letter | accept / error | A5 follows the approved P1; OQ8 question (§2) | owner |
| af43dcd8ffce | valid | error / accept | (a), clause `4.9.6 15.6.3 15.6.4`; the old test row re-spelled at R6-check | X-1; MIR test 2d-6 |
| 47f0722c876b | error at the second `f(source)`, `CallEffectConflict_Kd` | `Unsupported_Kd` / accept (unsound) | family test: `CallableEffectBoundTest` row 2 on MIR | 2f-4 (2f-3 first) |
| 59e2289ce4f4 | error at `Helpers.again(source)` | error / accept (unsound) | family test (h1, h3); the row moves from F5 to F6 | 2f-3 |
| PD16's stored Φ_c | a value the call stores into another input is never excused; transfers of the result are | p3: accept / accept (unsound) | 2x-P6 cases p1–p5; 2f-3 reports the Language code | 2x-P6, 2f-D, 2f-3 |

## 1. G147 (8ii)

**Reading.** S15:454: a reference reaches storage the body does not own "when it comes from a parameter …, or from a call result
that its contract ties to no argument, or derives from such a reference"; a value stored there may be used after every point.
S15:594: retention is "per argument, never per Origin", and a result retaining an Origin of a borrowed argument carries "those [Loans]
of such a read too". `Cell<s/T>.init(link: slot@uniq)` and `Box<uniq/(s/T)>.init(item: slot@uniq)` have the row `0 Value result`
(H, by `RetentionTest`'s `init` row): by contract the result may hold what a read through the argument yields, `slot`'s value, a
parameter reference. So `cell.link` may derive from it, `cell.link@follow = child@move` may store the Reborrow `child` of `v` into
storage the body does not own, and `v@follow = x` conflicts (S15:455). The narrow reading ("a Copy or Reborrow in this body") is
unsound: g3 has the same caller facts, but `Wrap.make(link: uniq/(uniq/E during b) during a) -> Cell<E> during a` returns
`link@follow@follow@uniq`, which is `*other`, the caller's storage. No Type-blind rule tells g1 from g3.

**Evidence.** Prototype g1–g4: A3's facts reject g1–g3 and accept the direct twin g4 (`var link = slot@uniq; link@follow =
child@move`); the narrow form accepts g3, unsound. Probes: shadow A5 rejects g1, g2 (the `uniq` view, PLAN G147) and g3 at
`v@follow = x` and accepts g4; the old path accepts all four, g3 included.

**Outcome.** A5 is right. a5-writes's (8ii) rows (2 programs × Source and Run) are (d) by P4 (A3 §2, approved in R4-M35, integrated
at `4372b058`), clause `15.6 15.6.4`, as that row's C3 rows are; since the old path also accepts g3, (a) would close them too.
`pair-cases.json` A4-box-ok and A4-field-ok: `Expected.uniq` = `ComparisonLoanConflict_Kd` at `v@follow = x`, Loan
`let child = v`; `Old` = Rule `P4` with the current verdicts and Run (R4-M60). Owner 2c-7, whose verification then adds
`GenericOwnershipCaseTest`; else X-1. A Type guard recovering these forms would relax S15:594 (a refinement like G127); not proposed.

## 2. S1-a: storage the body does not own, per holder (owner question)

**Facts.** A3's P1, approved with A3's losses (R4-M35) and integrated as S15:453, reads a part as the whole holder
(design-A3.md:42) and carries Φ_E in σ like a Loan (:126, :243, :248); INTEGRATED:519 decided "storage the body does not own" "as
A3's Φ_E is". S15:454's wording, "derives from such a reference", leaves derivation undefined and reads narrower: S15:452 separates
"references derived from" a borrow from "aggregates … that hold one". By that letter SPEC accepts g5 and r3 (`var t = (local@uniq,
w2@move); t.0@follow = child@move; _ = local@follow; v@follow = 9`, `w2` a parameter reference), since `t.0` is `local@uniq`. A5
follows P1 and rejects them. Count: lib 0 / ms 0 (the 1b-5 census, PLAN); no test row by reading (the A5 acceptance signatures'
notes, classification.tsv:38–43, name other causes; the other signatures' sources were not read: H).

**Evidence.** Four forms on nine cases (§7). A3 = SPEC except g5 and r3 (over-strict). "split" (the first draft's "per part": Loans
and Φ_E per part) accepts r1, which S15:453–454 (P1, P10) reject: unsound, so it backs no option. "Φ_E per part" (Loans per holder;
a part read carries the holder's Loans and its own part's Φ_E) = SPEC on all nine; exact beyond them is (H). Probes: shadow A5
rejects g5, r1, r2 and r3 and accepts r4 (both parts body-owned); the old path accepts g5, r1, r3 and r4.

**Options.**
- (a) Complete P1's integration. S15:454's sentence ends "…, or derives from such a reference: a Copy or Reborrow of one, a value a
  call may retain from one, a read of a borrowed argument included (§15.6.4), or a value read from a part of a holder that holds
  one, directly or through a reference to that holder". It narrows the current letter to the intake's recorded decision; A5 is
  unchanged. Then a MIR test: g5, r3, r1, g6 errors; g4, r4 valid.
- (b) SPEC states only the call clause (no verdict change; G147 (8ii)'s (d) rests on it). A5 carries Φ_E per Move Path beside P1's
  Loan holders: an A3 erratum superseding R4-M35's P1 for Φ_E (a DECISIONS entry), modelled before its unit with r1 an error.
- (c) Hold. A5 keeps a rejection no integrated SPEC clause states, which fails §5.4 item 5 (prototype parity), so (c) needs OQ9's
  list; G147 (8ii)'s (d) then rests on reading "derives" only.

**Interim (PD30, R4-M74 OQ8; R4-M35).** A5 has reported g5's Language code in shadow since 1b; a located `Unsupported_Kd` would
need the per-part fact (b) adds (argued). Question: does the shadow code stand, until the SPEC session or under (b) until its unit,
as an exception to both PD30 and R4-M35 ("Until the SPEC revisions land, … a violation of an approved loss is a located
`Unsupported_Kd`")? Nothing is published before R6-check and no corpus program has the form (tests: H). The session comes before
g5's MIR test, X-1 and R6-check; the sentence can join OQ12's SPEC session (S3's §15.3.6 clarification, before 2a0-3b).

**Against G148's P13** (owner today: a representation, OQ8 (b); PLAN G148). Alike: a per-holder simplification against a per-part
letter, lib 0 / ms 0, recoverable by a per-part representation. Unlike:
1. P13 was a new loss; A3 as approved already carries Φ_E per holder (P1, with the intake's decision), so (a) records a decision
   made.
2. P13 had 3 tests and a library-shaped form (a generic `for` over a local `Array<T>`); S1-a has no test (by reading, H) and no
   named library form, and re-spells as separate locals (H).
3. P13's interim is "a located `Unsupported_Kd` where a conflict depends on such a step" (PLAN G148); S1-a's would need the
   per-part fact itself (argued).
4. (b) recovers in-body aggregates only. A struct built by `init` is a call result, one value (S15:594), so r2 stays an error while
   its tuple twin r3 becomes valid: two verdicts for one data shape (Principle 1). Under (a) both are errors.
5. (b) gives storage the body does not own a second carrying unit while Loans keep the holder (INTEGRATED:523–524).

If G148's per-part unit tracks where in a holder each Loan sits (H; its design is open), (b) costs less, but 1, 4 and 5 stand.
**Recommendation: (a)**, in OQ12's SPEC session, with the PD30 exception confirmed until it lands.

## 3. af43dcd8ffce

**Reading.** Valid. `insertOrReplace(self: uniq/Self, key: K, value: V) -> Option<V>` (LIBRARY): `value: V` names V's Origins, so the
result does not descend from the receiver (S15:570), but it still retains the receiver's content and `value` (S15:594; rows H) and
names no Origin of the receiver Borrow, so it keeps no receiver Loan. `item@follow = 42` writes the old value's referent: a write
through `item` hits no Loan carried by another root (S15:505). `entries` keeps its dependencies (S4:552: no operation subtracts),
so writing their Places while it lives is an error (a2). Without descent the result carries the same Loans by retention, and
carrying a Loan never makes a write through its carrier a conflict, so the old test's premise does not follow.

**Evidence.** Prototype a1–a3 = SPEC. Probes: shadow A5 accepts a1 (the ledger source) and a3, rejects a2 at `x = 5`; the old path
errs at `item` in all three.

**Outcome.** (a), clause `4.9.6 15.6.3 15.6.4`, at X-1 (the plan's residual Comparison/IdentifierName row).
`DependentDictionaryLiteralTest.cs:35` (row 3) asserts the old verdict and is re-spelled at R6-check, as G147 (7)'s row is. 2d-6:
a1 and a3 accepted, a2 rejected at `x = 5`, on MIR.

## 4. 47f0722c876b

**Reading.** An error. S8:843: `preserves results` "excludes conflict with Loans of earlier results of the same callable value";
S8:774: replacing `v` is outside the guarantee. After `f = two@move` the second `f(source)` has no exclusion; under rule 1 (S8:856,
S8:789) its input `source: uniq/S` may reach every Loan S's Origins denote, which `first: S.Item` may keep, and S8:793 makes that an
error at the later call: `CallEffectConflict_Kd`, as for `var g = two@move; g(source)` (c3). The same `f` twice (c1,
936f8571825e) is valid. S2 §10 gives both verdicts.

**Evidence.** Prototype c1–c3: rule 1's reach as a phantom on the carrier S, with A3's `disturbed`, gives SPEC's verdicts; A3 as
written (Φ_c only on a descending result) accepts c2 and c3. Probes: shadow A5 accepts c1–c3; the old path reports
`Unsupported_Kd` at c1 and c2 (R1g) and `CallEffectConflict_Kd` at c3.

**Outcome.** `CallableEffectBoundTest.cs:401` (row 2) expects `CallEffectConflict_Kd` at the second call on MIR; the default
pipeline keeps `Unsupported_Kd` until R6-check. Rule 1's reach for a non-descending result is 2f-D's choice. 2f-3 may report c2 as a
located `Unsupported_Kd` if it does not see the replacement; 2f-4's invalidation gives the Language code. The row then differs by
code only.

## 5. 59e2289ce4f4

**Reading.** An error at `Helpers.again(source)`. S15:450: Loans a body receives through its parameters' Origins are checked "where
the body is called, against what the call may reach (§8.11.4)"; through the abstract input `source: uniq/S` the call may reach every
Loan S's Origins denote (S8:789), which `first` keeps by descent (S15:570, P12). Nothing excludes them: bounds come from premises,
"never bounds of implementations" (S8:787), a non-virtual function declares none (S8:677), and `again` is not `take` (S8:774).
Checking only the argument's Borrows (the F6 audit's alternative) is unsound: a helper calling `value.reset()` (h2) has the same
caller facts as the direct `source.reset()` (h4), which errs. A helper under `T is StableSource` (h3) errs too.

**Evidence.** Prototype h1, h4, h5: without desc(c) (MIR today) h1 and h4 are accepted; with A3's desc(c) they are rejected (no
excuse: the callee is not `take`) and takeTwo (h5) is valid. Probes: the old path rejects h1–h4 with `CallEffectConflict_Kd` and
accepts h5; shadow A5 accepts all five.

**Outcome.** 2f-3's `MirBorrowsTest.Preserves`: h1 and h3 errors, `CallEffectConflict_Kd` at the call (2f-D's A6 row). The row
leaves 5e1a449cc7bf's F5-filed signature at 2f-6's split.

## 6. PD16's stored Φ_c

**Reading.** S8:774 excludes "Loans kept by a result of an earlier call …, including Loans kept after transfer or Reborrow of that
result"; S15:452 separates results from "the writable storage its public contract lets it write"; S15:455: values decide, "never a
Loan's identity".
1. A value the earlier call itself writes into another input (`takeInto(self, spare: uniq/Option<Self.Item>)`, `0 Content store 1`)
   is not a result: a later call that may reach its Loans conflicts (p3; p3b, no bound). Conformance compares only with results, so
   an implementation may re-lend the stored item.
2. A value the caller derives from the result keeps the exclusion, moved into a holder (p1) or stored by another call
   (`Stash.put(held@uniq, first@move)`, p2).
3. What a call stores into its own receiver (`0 Content store 0`, H by `RetentionTest.cs:126`) is inside its own argument, and
   S15:455 checks an activation "against the values live into its call other than its own argument"; a phantom there would reject
   takeTwo (S8:687), 22.2.3's nextPair and takeLate (`EffectBoundCallerTest.cs:41`).

**Evidence.** Prototype p1–p5 in four forms: only "own id" (the call's store rows carry a distinct phantom Φ_c^s, never excused,
none through the receiver's own row) = SPEC on all six; "none" (A3 round 9) accepts p3 and p3b; "result id" accepts p3 and rejects
p4; "own id + self" rejects p1, p2, p4 and p5. Probes: both paths accept all six (no test has the form, so no ledger row), and the
old path rejects p5b (takeTwo with `S is Source`), which shadow A5 accepts: 2f-3's Take-only row.

**Outcome.** PD16's interim ends: a conflict only a call's stored Φ_c^s causes is a Language error (`CallEffectConflict_Kd`) in
2f-3, never excused by 2f-4. 2x-P6 derives p1–p5; 2f-D fixes the phantom's identity and the receiver-row exclusion; 2f-3's
stored-Φ_c test is p3 with p3b.

## 7. Prototype run and findings

| Group (forms) | Case: SPEC → each form |
| --- | --- |
| G147 (8ii), S1-a (A3, narrow, split, Φ_E per part) | g1–g3: error → error, **UNSOUND**, error, error · r2, g6: error → error ×4 · g4: valid ×4 · g5, r3: valid → error ×2 (over-strict), valid, valid · r1: error → error, error, **UNSOUND**, error |
| af43dcd8ffce (design) | a1, a3: valid → valid · a2: error at `x = 5` → same |
| 47f0722c876b (A3, carrier) | c1: valid ×2 · c2, c3: error → **UNSOUND**, error |
| 59e2289ce4f4 (today, desc) | h1, h4: error → **UNSOUND**, error · h5: valid ×2 |
| stored Φ_c (none, result id, own id + self, own id) | p1, p2: valid → valid, valid, error, valid · p3: error → **UNSOUND** ×2, error ×2 · p3b: error → **UNSOUND**, error ×3 · p4: valid → valid, error, error, valid · p5: valid → valid, valid, error, valid |

The readings' forms (A3, the design, carrier, desc, own id) have no unsound acceptance; their only over-strict verdicts are
S1-a's (g5, r3).
Run: the header of `spec-readings-cases.cs` (`dotnet run proto.cs -- -s2x1`, .NET SDK 11.0.100-rc.2); the hook adds a flag only.

MIR's unsound acceptances today, all A5-F6 (2f-3; c2's code at 2f-4): c2, c3, h1–h4, p3, p3b, p5b. The old path's, frozen until
R6-check: g3, g6, p3, p3b. Findings for other units (nothing changed): 2f-6's completion condition names 936f8571825e "rejected on
MIR", but S8:843 and S2 make it valid (c1), so after 2f-4 its old row is R1g's limit, to classify; 2f-3's "desc(c) … through W into
the storage its store rows write" must skip the receiver's own row (§6, 3).

## 8. Not verified

- Retention rows (`0 Value result` of Cell.init and Box.init, `0 Content store 0` of an abstract `uniq/Self` requirement,
  insertOrReplace's) are inferred from RetentionTest and LIBRARY, not printed (H). g3's alias is read, not run.
- Excuses and disturbances are hand-given, as in rounds 7–10 (2x-P6 derives them); the carrier phantom and "Φ_E per part" (a side
  holder per part) are case encodings, not rules. S1-a's lib/ms count rests on the 1b-5 census (2x-T1 re-measures it).
- The codes and locations A5 will report for c2, h1 and p3 are 2f-D's.
