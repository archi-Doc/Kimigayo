# R6-check H0a: unit a, the publication of MIR problems (H3)

Status: H0's design part a (plan §4.10 rows H0 and H3; PD29, PD30), design only, for H0's design review and the owner's one look under
OQ11 (R5-M1). Working record, not authority: what it decides becomes authority only as the DECISIONS entries H3 writes (§10).

Working time (DECISIONS Q12): 17:40–18:02 on 2026-11-02, about 22 minutes, one session, within the one-hour limit.

Base: dev `1ec539c79` (code, DECISIONS, PLAN). "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. "mir" = `KIMI_PIPELINE=mir`. Facts are read from the base; no build,
test or probe was run. (H) marks an unchecked inference. Standing request: the owner's 「その役割分担でOKです。H0の設計から始めて下さい。
終わったら、R4の完了を待ってください。」 (2026-11-02): R4 owns H3's publication in `MirReport`, R5 the Core wiring, BU the Binding facts.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| H0a-1 | One **publication table** decides what each pipeline publishes, by classes the published columns identify (requirement partition, `Body`, code). Default: unchanged until H5 (H3 is behavior-preserving there). mir from H3: every MIR problem except A4's, plus the old analysis's effect issues only; no other old Ownership issue and no old `RequireFallthrough_Kd`. From H5: every MIR problem on every pipeline | 2 |
| H0a-2 | Acceptance under mir is computed from the same rows the table publishes (`Analysis` and `Problems`), so a rejection without a record cannot arise; `Binding.LintErrors` stays a term | 2, 4.3 |
| H0a-3 | **H3-1 (bp)** folds the construction records and the collector records into `MirReport` as `MirProblem(code, ControlFlow, subject, NoBody, all views)`; `MirReport` resets with the version in `MirProgram.Clear`, not in `MirAnalysis.Run`; the two `reported` sets and both `Records` lists go. Validator findings stay `MirProgram.Findings` (the driver reads them) and publish through a table row | 3.1 |
| H0a-4 | The merge across bodies by (code, subject, Reason) is a **record view owned by `MirReport`** (`MirReport.Records`), so R4-M132's derived merge has one copy; Core only maps records to the diagnostics adapter | 3.2 |
| H0a-5 | A record's requirement is its problems' own (R4-M67, R5-M23), never derived from the code; a cross-body disagreement is the `InternalInvariant_Kd` that `Add` already gives. Partition = the requirement's (DIAGNOSTICS rule 1). Binding.* → Ownership.* renames stay at H5 (R5-M23) | 3.3 |
| H0a-6 | Key: a record with facts at its subject node, condition = its Reason's ordinal among the records of its (code, subject); a location-only code by its location (`ReportLocation`, R4-M71) | 3.4 |
| H0a-7 | One **fact table**, one row per `MirFactKind` (R4-M132's R5 condition); views give the case evidence and Note through Q8-6's one renderer; no other Note text | 4.1 |
| H0a-8 | A record with `Cause` facts is `PrerequisiteUnavailable_Kd` (R4-M132 OQ-2 (a)) with `DerivedFrom` = its causes' record keys, or `Binding.FailureCauses(site)` for a Binding cause (Value −1, 2g-1); the owner's finalization suppresses it (DIAGNOSTICS §4.3) | 4.2 |
| H0a-9 | PLAN10 P10's fallback gets **no new producer**: every MIR incompleteness already has its record; DIAGNOSTICS rule 8's contract check stays the net | 4.3 |
| H0a-10 | Effects between H3 and H5 (PD29): A4's problems (Ownership requirement, `NoBody`, not `InternalInvariant_Kd`) are held; the old analysis's effect tail is the one producer; its early-stop gap is a mir-only PLAN open issue ending at H5 | 5.1 |
| H0a-11 | G128 (PD30): R4-M153 gave A5's `CallRetention.Limit` `OwnershipDepthLimit_Kd`, so PD30's premise (one code, `Unsupported_Kd`) no longer holds. Recommended (A): no rule; where both meet one call, the collector's `StorageNesting` record and A5's depth record both stay, counted, until R7 removes the bound (Q13) | 5.2 |
| H0a-12 | Independence (SPEC 23.1.2, Q13): §6's table; the only dependents are records with `Cause` facts | 6 |
| H0a-13 | Owners: R4 the Mir files and `MirReport.Records`, R5 the Backend and Core files and the table, BU Q8-6's renderer and `FailureCauses`; review: H3-1 R, H3-2 D+R under H0 with one review (Q13) | 7 |

## 1. State on dev (facts)

| Item | State | Evidence |
| --- | --- | --- |
| What check publishes (R5-M156, OQ5's records) | Every pipeline: construction records, validator findings (as `InternalInvariant_Kd`) and every collector record, location-only, requirement `ControlFlow`, ControlFlow partition; the analyses' problems unpublished (R4-M1). The old analysis and old CFA decide and publish | `C/Core/Compilation.Pipeline.cs:82-94` (`ReportAnalysis`), `:127-150` (`MirRecords`) |
| `KIMI_PIPELINE=mir` | Changes no published record; selects the ledger's observation, the identity mark and the manifest entry until H12 | `C/Core/Compilation.Switch.cs` contract; VERIFICATION `:94` |
| Acceptance | `Accepts` = Binding and startup complete, `Analysis.IsVerified`, and no Error through the Ownership partition; `Analysis` = old result and no MIR record | `C/Core/Compilation.Generation.cs:46-47`; `Compilation.Pipeline.cs:35-43` |
| `MirReport` (A6) | Keys (code, subject, body, Reason); `Add` drops an Inlined problem, merges views, checks the requirement (disagreement: one `InternalInvariant_Kd`), merges derivedness (direct wins, causes unioned), returns the index. Contract: "Must not publish (R4-M11's merge across bodies … is publication's)" | `C/Mir/Analysis/MirReport.cs:3-16`, `:48-84` |
| `MirProblem` | `(Code, Requirement, Subject, Body, Views, Reason)`; `NoBody` = A4's obligation rows and the C1-F4 view's broken contracts | `C/Mir/Analysis/MirProblem.cs:8-12`, `:31-42` |
| `MirFact` | 14 kinds, each with a producer: Anchor, Use, Store, Reservation, Inlined, EffectRead/Write/Unknown, Evaluated, Relation, Required, Limit, Table, Cause; Cause only from rule W (`MirBorrows.Causes.cs:296`), none with Value −1 | `C/Mir/Analysis/MirFact.cs:78-94` |
| Ask A-5 (revised by R5-M23) | (1) Loan facts: landed (Anchor/Use/Store/Reservation producers); (2) G125's key: landed as `Body` (R4-M71); (3) `MirProblem.Requirement` per recording rule: landed (R4-M67). No code → requirement table exists or is wanted | R4-M67, R4-M71, R4-M132 |
| Q7, Q17 | Landed: `OwnershipRequirement` in Diagnostics (`src/Kimi/Diagnostics/DiagnosticRequirement.cs:15`), 0 `OwnershipFailure` references; the ledger's MIR requirement column and `requirement` difference kind (`tests/xUnitTest/MirDifferenceLedger.Aggregation.cs:22, :52`, `.ComparedSet.cs:26-66`) | R5-M24, R5-M56 |
| A6 (2g-D, 2g-1–2g-7) | Not on dev: no Binding cause (Cause −1) producer, G105 open (PLAN `:116`), primary locations (2g-4a/b), the cross-strata merge (2g-5), the A6 contract test (2g-7). R4's exit includes them (R4-M35) | DECISIONS cites 2g-D only as pending; PLAN G105 |
| Construction records | `MirProgram.Report` dedupes per (code, site) in its own `reported` set; 6 builder sites; `MirDump.cs:42` prints them | `C/Mir/MirProgram.cs:30, :67-84` |
| Collector records | `CheckedSet.Report`, its own `reported` set; 24 sites; G128's storage verdict `Limit` → `StorageNesting` → `Unsupported_Kd` at the requesting site | `C/Backend/Instances/CheckedSet.cs:30, :100-107`; `InstanceCollector.cs:182-197`; `Capability.cs:69` |
| Driver | `Run` clears `Problems` and `Drops` at a new version (`MirAnalysis.cs:69-70`), A4 adds its problems in step 3 only, all with `NoBody` (`MirEffects.Solve.cs:102-119`); C1-F4 findings are `InternalInvariant_Kd`, `Ownership.Internal`, `NoBody` (`MirOrigins.cs:135-141`) | `C/Mir/Analysis/MirAnalysis.cs:6-21, :61-97` |
| Old CFA | Reports only `RequireFallthrough_Kd` (1 site), A3's twin | `C/Analysis/ControlFlow*.cs` |
| Old effect issues | `ValidateEffectBounds` appends them last (`OwnershipAnalysis.cs:121`; `.EffectBounds.cs:44-65`: `EffectBound`, `CallableEffectBound`, `VirtualEffectBound`, or `Unsupported` where `IsEffectLimit`); it is not reached after an incomplete Binding, a stop or an unproven Origin obligation (`OwnershipAnalysis.cs:89-104`) | — |
| Diagnostics adapter | `Koto.Report` (`Koto.cs:570`), `Koto.ReportDerived` (`:605-619`), `DiagnosticCollection.ReportLocation` (`:269-274`); the owner merges a repeated (key, code) and faults on different facts (`DiagnosticOwner.cs:316-345`); order by location, subject, code, then check (`:655-679`) | — |
| Catalog facts of MIR codes | Args only on `OwnershipStorageLimit_Kd` (requiredBytes, limitBytes; evidence `table`); `case:Text` on MovedPlace and the Loan and default codes; two Origin-relation alternatives (relation, longer, shorter, source[, destination]); `PrerequisiteUnavailable_Kd` none | `src/Kimi/Diagnostics/DiagnosticCode.tinyhand:613-798, :855, :971-981` |

The plan's rows H0/H3 still say "the published requirement derived from code and facts with … Q17's table": R5-M23 superseded that (each
recording rule names it; "a code → requirement table (kept only as the fallback if R4 declines A-5)"), and R4 accepted (R4-M67). Q8 §5
left H0 two items: the partition of MIR problems and the condition when two codes share one requirement at one subject (§3.3, §3.4).

## 2. The publication table (H0a-1, H0a-2)

Rows are identified by published columns only; the table lives in Core's publisher (§7) and is the one place deciding publication.

| Class | Identified by | Default until H5 | mir, H3–H5 | From H5 |
| --- | --- | --- | --- | --- |
| Construction and collector records | requirement `ControlFlow`, `NoBody` | publish (today's) | publish | publish |
| Validator findings | `MirProgram.Findings` | publish (today's) | publish | publish |
| A3 (`RequireFallthrough_Kd`) | `ControlFlow`, a body | hold (old CFA's twin publishes) | publish; old CFA's not | publish |
| A1, A2, A5 problems, `MirReport`'s disagreement record | Ownership partition, a body | hold | publish | publish |
| A4's problems | Ownership partition, `NoBody`, not `InternalInvariant_Kd` | hold | **hold** (PD29) | publish (H5, with H4's records) |
| C1-F4 findings | `InternalInvariant_Kd`, `NoBody` | hold | publish | publish |
| Old analysis issues | `Ownership.Issues` | publish | effect tail only (§5.1) | deleted (H5, H10) |

- Default stays exactly as today from H3-1 to H5, so H3's default Session and diagnostic snapshot must be equal (bp).
- Under mir, `Analysis.IsVerified` is "no Error among the published rows, no old effect issue, `Binding.LintErrors == 0`" (lint records fail no
  node, PD31, so dropping that term would accept a lint error). `Problems` lists the same rows. `ReportAnalysis` under mir skips the old
  CFA (its one code is A3's twin) and publishes the Ownership partition once (§5.1).
- At H5 every row reads "publish" and the table collapses to "every record" (its removal condition), and `KIMI_PIPELINE` again changes no
  record (until H12). H3 supersedes R5-M156's "`KIMI_PIPELINE=mir` now changes no published record" under mir only.
- VERIFICATION `:94` changes at H3: from H3 a `-Pipeline mir` run is evidence, not a gate (its old-record expectations fail until H5
  moves them); the mir ledger run still reads its raw rows (the ledger observes `MirReport` directly, R4-M17, unchanged).

Rejected: publishing MIR problems beside the old analysis's under mir (every failure twice; equal keys with different facts fault in
`DiagnosticOwner.Record`); a second switch for H3 (two selections for one rehearsal); doing nothing under mir until H5 (H3b's rehearsal and
the snapshot census under mir need H3's publication, plan rows H3 and H3b).

## 3. `MirReport` as the one record source

### 3.1 H3-1: the fold (H0a-3; R4-M3a's stage ledger item; behavior-preserving)

- `MirProgram.Report(site, code)` adds `MirProblem(code, ControlFlow, site, NoBody, ulong.MaxValue)` to `Problems`; `CheckedSet.Report`
  does the same through the program it collects. `MirReport`'s key (code, subject, NoBody, default Reason) gives today's "once per code
  and subject"; the `reported` sets and the `Records` lists go (`MirProgram.cs:30, :67-84`, `CheckedSet.cs:30, :80, :100-107`).
- `MirReport` belongs to the version: `MirProgram.Clear` resets it with the bodies, and `MirAnalysis.Run` no longer does (`:69`), since
  construction records precede the analyses. `Drops` keeps its reset in `Run`. The collector runs after the analyses and a warm re-run adds
  equal keys, which merge.
- `Compilation.MirRecords`, `MirDump.cs:42` and the 49 test reads of the two lists (about 28 files, grep at the base) read the class rows
  through one helper. MIR goldens and the semantic snapshot stay equal.
- Gain: one record representation and merge; a construction record can be a `Cause` by index (2g-1's cause sites include "a MIR
  construction record's subject", plan A5 step 2 row 2g-1). Rejected: folding at H5 (the H5 commit, already ≈ ±300/−2,700, grows, and
  2g-1's causes would need a second key path); folding validator findings (the driver reads `Findings` to skip analysis; one row suffices).

### 3.2 The merge across bodies (H0a-4; R4-M71, R4-M72's AR-4 hand-over)

`MirReport.Records` (R4, new partial `MirReport.Records.cs`) gives one record per (code, subject, Reason) in first-problem order: its
requirement, its first problem's facts, derivedness and causes merged by `Add`'s own rule (direct wins, causes unioned, R4-M132), and
each `Cause` index remapped to its record's index. Pooled; a warm version allocates nothing. Case views stay per body: a record's case
evidence is its first problem's when every grouped problem limits to the same case text, else none (best effort, Q13).

Reason: the derived merge already lives in `MirReport.Merge`; a Core copy would be a second copy of the rule (AGENTS). This departs from
R4-M71's "at publication" placement and `MirReport`'s "Must not publish (… is publication's)" wording; `MirReport` still reports nothing.
Rejected: grouping in Core (second copy); keying `MirReport` without `Body` (R4-M71's r05 reason holds).

### 3.3 Requirement and partition (H0a-5)

A record names its problems' requirement; problems of one (code, subject, Reason) in two bodies naming different requirements give
`InternalInvariant_Kd` (`Ownership.Internal`) at the subject, as `Add` does within a body. The partition is the requirement's: Ownership for
A1, A2, A4, A5 and the A6 mappings; ControlFlow for A3 and the construction and collector records (unchanged keys and partition on
default). The plan's "requirement derived from code and facts" is void (R5-M23); no table is added.

### 3.4 Keys and conditions (H0a-6)

- A code with facts (catalog arguments or evidence: `OwnershipStorageLimit_Kd`, the case and Origin-relation codes, `DefaultArgument*`)
  reports at its subject node: `Koto.Report(requirement, code, …, condition)`, where condition = the ordinal of its Reason among the
  records of its (code, subject) in record order (0 for the default Reason), so two relations at one subject are two problems with a
  defined order (`DiagnosticOwner.cs:655-679`), and one relation never faults on different facts.
- A location-only code (`Unsupported_Kd`, `OwnershipDepthLimit_Kd`, `DefaultExpansionLimit_Kd`, `InternalInvariant_Kd`, the collector's
  codes) reports by `ReportLocation` (R4-M71, DIAGNOSTICS rule 3), as today.
- Two codes sharing one requirement at one subject (Q8 §3.6's open item): two problems, ordered by code; no rule.

## 4. Facts, derived records and the fallback

### 4.1 The fact table (H0a-7; R4-M132's R5 conditions)

| Kind | Published as |
| --- | --- |
| Anchor, Use, Store, Reservation, Evaluated | related locations (the old analysis's roles: loan anchor, witness use, store, reservation, default operation) |
| EffectRead, EffectWrite, EffectUnknown | related location: the static Field's declaration (A5's err(effect) rows; A4's are held until H5) |
| Relation (+ the problem's `MirReason`) | the Origin-relation evidence alternative; the C1-F4 row (Value) gives `source` and, for a fit, `destination` |
| Required, Limit; Table | `OwnershipStorageLimit_Kd`'s arguments; its `table` evidence |
| Cause | `DerivedFrom` (§4.2) |
| Inlined | never reaches publication (`Add` drops it) |
| a kind without a row | `InternalInvariant_Kd` at the subject (R5's condition) |

Views below every view of the body give the `case:Text` evidence, the case Note and the related binder declarations through Q8-6's one
renderer (R5-M23; `MirCases` numbers views as the old case bits, `C/Mir/MirCases.cs` contract). No other Note: the old analysis's
hand-built notes (`OwnershipAnalysis.cs:300-400`) are not reproduced; their loss is a classified snapshot difference, not a regression
(Q13). Notes and relation text come from the diagnostics adapter only (AGENTS Diagnostics).

### 4.2 Derived records (H0a-8; R4-M132 OQ-2 (a), PD17)

A record whose merged facts end in `Cause` facts is published as `Koto.ReportDerived(requirement, causes, condition)`: its own
requirement and condition, `DerivedFrom` = for Value ≥ 0 the key of the record its cause index names (§3.2), for Value −1 (2g-1's Binding
cause at the site) `Binding.FailureCauses(site)` (`Binding.Diagnostics.Prerequisites.cs:53`). One resolver in the publisher; an index
naming a removed problem is `InternalInvariant_Kd` (R5's condition: a broken contract, never a silent drop). The owner's finalization
suppresses it exactly when every cause reaches a published direct Error (DIAGNOSTICS §4.3), so acceptance never rests on it.

### 4.3 PLAN10 P10's fallback (H0a-9)

P10/DIAGNOSTICS rule 8: a phase ending incomplete with no Error reports one `PrerequisiteUnavailable_Kd` fallback. The MIR phase never
ends incomplete without a record: an arena skip records `OwnershipStorageLimit_Kd` (R4-M15a, R4-M132 U4); validator findings stop the
analyses and publish as `InternalInvariant_Kd`; an incomplete Binding has Binding's Errors; an analysis exception is `CheckFaulted_Kd`.
Under mir, H0a-2 computes acceptance from the published rows, so a rejection without a record cannot arise; the owner's unexplained-
rejection check stays the net. Rejected: a MIR fallback producer (no reachable case; a symptom catch-all).

R5-M96's interim (an arena-skipped body refused at generation as a located `Unsupported_Kd` until a record carries the facts): under mir
the body is rejected at check with its arguments, so the interim is unreachable there; on default it stays until H5 removes the refusal
path (plan H5). R4-M132 U4's `Incomplete` index is then unused by H3.

## 5. One producer per failure

### 5.1 Effects between H3 and H5 (H0a-10; PD29)

- Under mir, A4's problems are held by the table row (Ownership partition, `NoBody`, not `InternalInvariant_Kd`: exactly A4's step-3
  records today, `MirEffects.Solve.cs:102-119`; a contract test asserts that every held problem names one of A4's five requirements:
  `EffectBound`, `CallableEffectBound`, `VirtualEffectBound`, `Unsupported`, `DepthLimit`).
- The old analysis publishes only its effect tail: `OwnershipAnalysis` records the issue count before `ValidateEffectBounds` and reports
  the issues from there through its existing loop (`EffectBound` through `Binding.ReportEffectViolation`, the others in the Ownership
  partition), invalidating the partition first; Binding's own effect records publish as always. Temporary, `[Transitional("R6-check")]`,
  removal H5 (which deletes `ValidateEffectBounds`). About +10 lines in the frozen old analysis: no accepted form changes (D5).
- Gap: where the old analysis returns before `ValidateEffectBounds` (a stop or an unproven Origin obligation, `OwnershipAnalysis.cs:94-104`),
  mir has no destruction-effect producer, so mir may accept such a program with an effect violation A4 holds. mir-only, ends at H5, and H3b
  rehearses with H5 applied: a PLAN open issue, no machinery.
- Rejected: publishing A4 under mir from H3 (Binding's bind-time effect checks are A4's twins and cannot be switched by pipeline, PD29);
  an explicit A4 mark on `MirProblem` (a column for what `Body` and the requirement already encode; asked of R4 in §9 instead).

### 5.2 G128 `Apply` null (H0a-11; PD30)

Facts: the collector reports a storage verdict `Limit` as `StorageNesting` (`Unsupported_Kd`) at the requesting site
(`InstanceCollector.cs:182-197`; also `.Witnesses.cs:231, :279` for comparison items); A5 reports a call's `CallRetention.Limit`, which
includes Binding's storage height, as `OwnershipDepthLimit_Kd` at the call (R4-M153, superseding R4-M51 there); the builder's two G128
limits are construction `Unsupported_Kd` (R5-M152 §1). PD30's premise ("R4-M51's code, `Unsupported_Kd`, as A4 and A5 report the same
cause") no longer holds, so H3's fold cannot give one record by key merge. Two owner choices now meet at this cause: `Unsupported_Kd` as
G128's interim code (BU-12, PLAN G128) and `OwnershipDepthLimit_Kd` for a retention limit (R4-M153, 「G272: (1)」). PLAN G128's row
still says A5 reports a located `Unsupported_Kd` (R4-M51); `MirBorrows.Reports.cs:95-109` and DIAGNOSTICS' A6 table say
`OwnershipDepthLimit_Kd`: H3-2 corrects the row (Auto, PLAN).

- **(A), recommended:** no rule. Where one call meets both (a concrete call past the bound in a root body, H), the collector's
  `Unsupported_Kd` and A5's `OwnershipDepthLimit_Kd` both publish: two detections of one implementation bound on a program the
  bound already rejects. Not a Q13 regression (no acceptance move, no Language error, nothing lost); SPEC 23.1.2 leaves grouping
  implementation-defined. Counted by code in H3b's census; PLAN G128's row names it; it goes with the bound at R7 (PLAN G128's removal
  condition). Cost 0 lines. Supersedes PD30's "H0 decides the single owner" (a planning-session decision; record selection is the
  planning session's under Q13).
- (B): one code for the cause: the collector's G128 records take A5's code and requirement, merged across bodies by (code, subject,
  Reason). It changes the collector's published code and partition on default (snapshot, STATUS, R5-M156's `ComponentSummaryTest` G128
  rows), overrides BU-12's owner choice for those sites, needs the A4 hold to tell a collector `NoBody` depth record from A4's, and
  touches R5-M156's OI-2 confirmation: more cost than the duplicate.
- Rejected: the collector reading A5's records (a consumer deciding by another's record); A5 recording nothing at a storage-bound
  retention (an undecided retention accepted where no collector request reaches the call: unsound).

## 6. Independent and dependent MIR records (H0a-12; SPEC 23.1.2, Q13, rule W)

| Record | Class | Published as |
| --- | --- | --- |
| A direct A1, A2, A3, A5 problem (and A4's from H5), Language, Proof or Warning | independent | its code |
| `OwnershipStorageLimit_Kd`, `OwnershipDepthLimit_Kd`, `DefaultExpansionLimit_Kd`, `GenerationResourceLimit_Kd`, `OversizedLayout_Kd`, every located `Unsupported_Kd` (construction, collector, A5's undecided-store and closure-hold rows) | independent | its code |
| `InternalInvariant_Kd` (findings, collector with a complete Binding, `Add`'s disagreement, C1-F4, a broken fact contract) | independent, never suppressed | its code |
| A problem with MIR `Cause` facts (rule W, R4-M132 U3) | dependent | `PrerequisiteUnavailable_Kd`, `DerivedFrom` its causes |
| A problem with a Binding cause (2g-1–2g-3: A1, A3, A4 at Opaque sites; §9 R4-5 asks the same for A5 at Binding's twin rows) | dependent | `PrerequisiteUnavailable_Kd`, `DerivedFrom` `FailureCauses` |
| A problem with an Inlined fact; the builder's `derived`/`Rejected` forms; a collector `InternalInvariant_Kd` with an incomplete Binding (R5-M151) | no record (template's check, or the Binding error's dependent) | — |

Q13's regressions apply to H3 as: on default, none may appear (bp); under mir, differences are the flip's and are classified at H3b and
judged at H5. A dependent record removed by rule W in an R4 unit is counted by code there, not in H3.

## 7. Owners, units, files and size (H0a-13)

| Unit | Owner and files | Size (H) |
| --- | --- | --- |
| H3-1 fold (bp) | R4: `C/Mir/MirProgram.cs`, `C/Mir/Analysis/MirAnalysis.cs` (`:69`), `MirReport.cs`/`MirProblem.cs` contracts, `C/Mir/MirDump.cs`; R5: `C/Backend/Instances/CheckedSet.cs`, `InstanceCollector.cs`, `C/Core/Compilation.Pipeline.cs` (`MirRecords` → the class rows) | +30/−45, t±60 |
| H3-2 publication under mir | R4: `C/Mir/Analysis/MirReport.Records.cs` (new, §3.2). R5: `C/Core/Compilation.Publication.cs` (new partial with its entry contract: the table §2, keys §3.4, fact table §4.1, cause resolver §4.2), `Compilation.Pipeline.cs` (`Analysis`, `Problems`, `ReportAnalysis` through the table), `Compilation.Switch.cs` contract; old analysis effect tail (`C/Analysis/OwnershipAnalysis.cs`, `.EffectBounds.cs`, Transitional H5). Docs: DECISIONS (§10), ARCH "Analysis records" row (`:99`) and R6 row, DIAGNOSTICS `:293`, `:299` ("unpublished until H3"), CODEMAP, VERIFICATION `:94`, PLAN (§5.1's gap, §5.2's count) | MirReport +70; Core +170/−15; old +10; t+300 |

Total about +280/−60 source, above the plan's ±200 (the fact table and keys were not in the row). Prerequisites: R4's exit with A6
(2g-D–2g-7; R4-M35), G11 (done, R5-M167), Q17 (landed), Q8-6's renderer (H1's first commit, bp, BU), OQ11's approval of H0. Review: H3-1
one R review (bp); H3-2 D+R under H0 (plan row H3), one review (Q13); R4 reviews the Core hunk's use of `MirReport.Records`, R5 the
`MirReport.Records` contract. Holds: none after R4's exit (plan §3.5); `Compilation.Pipeline.cs` is R5's from H3 to H5.

## 8. Tests (lowest layer that observes each rule)

- `T/MirAnalysisTest.cs` (`MirReport`'s rules; beside `ARepeatedProblemMergesItsViews` `:117` and `AMergeIsDerivedOnlyWhenEveryReportIs`
  `:184`): records merge across bodies by (code, subject, Reason) (R4-M71's r05: two constructors, one record; two Reasons, two records);
  a record is derived only when every grouped problem is; a `Cause` index names its record after the merge; a cross-body requirement
  disagreement gives one `InternalInvariant_Kd`; construction and collector records are `NoBody` `ControlFlow` problems (H3-1); warm
  `Records` allocates nothing (`Purpose=Allocation`).
- `T/MirPublicationTest.cs` (new: the publisher is a new component): one row per fact kind (each kind's published form; a kind without
  a row → `InternalInvariant_Kd`); `OwnershipStorageLimit_Kd`'s arguments and `table`; an Origin relation's evidence; case evidence
  from views; a derived record → `PrerequisiteUnavailable_Kd` naming its MIR cause, and naming `FailureCauses` for a Binding cause
  (from 2g-1), suppressed when its cause publishes; two relations at one subject → conditions 0 and 1; a `for` expansion's siblings stay
  one location-only problem (`PipelineSwitchTest.MirRecordsAtOneLocationAreOneProblem` `:317` stays as the default's row).
- `T/PipelineSwitchTest.cs`: under mir a `MovedPlace_Kd` program publishes MIR's record and the old one not; an old-only rejection
  (a ledger (b)/(d) row, H) is accepted under mir; an effect violation publishes once under mir (the old tail), A4's held; a lint error
  still rejects under mir; default publishes the same rows as before (`EveryPipelinePublishesTheMirRecords` `:269` re-pointed).
- Snapshot: default Session with `-DiagnosticSnapshot` equal (bp, both commits); one mir run with the snapshot as a recorded census
  (not a gate, §2), which H3b classifies. Ledger unchanged (no ledger edit).

## 9. Open questions (one line each)

| # | To | Question |
| --- | --- | --- |
| R4-1 | R4 | Do you accept `MirReport.Records` owning the cross-body merge (§3.2), superseding R4-M71's "at publication" placement? |
| R4-2 | R4 | May H3-1 move `MirReport`'s reset from `MirAnalysis.Run` (`:69`) to `MirProgram.Clear` so construction records survive the analyses? |
| R4-3 | R4 | Is "Ownership requirement, `NoBody`, not `InternalInvariant_Kd`" A4's set by contract, to be stated in `MirProblem`'s invariants? |
| R4-4 | R4 | Will 2g-1's Binding cause (Cause, Value −1, site = the Binding-failed node) be the only Binding-cause form H3 resolves? |
| R4-5 | R4 | Will A5's problems at Binding's twin rows (R4-M111) and at G353's refused stored Type carry a Binding cause, or stay two records to H11? |
| R4-6 | R4 | Does one concrete call past G128's bound give both the collector's `StorageNesting` and A5's `OwnershipDepthLimit_Kd` (a reproducer for H3)? |
| R4-7 | R4 | Does the C1-F4 row a `Relation` fact names give `source` and `destination` for the evidence without reading syntax? |
| BU-1 | BU | Will Q8-6's one Semantics-case renderer (H1's first commit, bp) land before H3, taking a body's binders and view bits? |
| BU-2 | BU | Are `Binding.FailureCauses(node)` keys stable at `ReportAnalysis` time for an Opaque site and a twin node? |
| O-1 | owner | None required (record selection is the planning session's, Q13); §10 lists the departures for the owner's one look under OQ11. |

## 10. Departures and DECISIONS text (H3 writes them)

- R5-M156: "`KIMI_PIPELINE=mir` now changes no published record" → under mir from H3, the MIR's publication and acceptance (§2); restored
  at H5. Reason: H3b's rehearsal needs a selectable MIR acceptance.
- R4-M71/R4-M72 (AR-4): the merge across bodies is `MirReport.Records`' (§3.2); `MirReport`'s contract loses "the merge … is
  publication's". Reason: one copy of R4-M132's merge.
- R4-M3a's stage item done at H3-1 (construction records folded), extended to the collector's records (plan row H3).
- `MirProgram.Problems`' "only `MirAnalysis` resets them" → `MirProgram.Clear` does.
- PD30's single-owner clause → (A) of §5.2 (two records at a G128 call until R7), if adopted.
- Plan rows H0/H3 "derived requirement … Q17's table": void since R5-M23 (recorded, no change).

Draft entry (H3-2): "R5-M… MIR problems publish under mir (R6-check H3). `MirReport.Records` groups the problems by (code, subject,
Reason) with `Add`'s derived merge; Core's publication table decides per pipeline and class (§2) until H5 collapses it; one fact table
(one row per `MirFactKind`), conditions by Reason ordinal, derived records as `PrerequisiteUnavailable_Kd` with `DerivedFrom`; A4 held
and the old effect tail published under mir until H5 (PD29). Supersedes R5-M156 under mir, R4-M71's placement, PD30's single owner.
Rejected: §2, §3, §5's rejected options."

## 11. Not done

- No reproducer was run (R4-6's co-occurrence, the mir census, the 49 test reads re-measured) — design only, per the brief.
- The H5 flip, H4, the snapshot classification method, the deletion order, H9 and the SPEC/IMPL pass are other H0 parts.
- The exact role names for related locations (§4.1) follow the old analysis's roles and DIAGNOSTICS' A6 table; H3-2 fixes them.
