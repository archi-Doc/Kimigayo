# R6-check H0a: unit a, the publication of MIR problems (H3)

Status: H0's design part a (plan §4.10 rows H0 and H3; PD29, PD30), design only, for H0's design review and the owner's one look under
OQ11 (R5-M1); integrated by [H0-design](H0-design.md) (the planning session's rulings R1–R4 and the design review's findings applied in place, each marked "(ruling Rn)" or "(review Bn/Nn)"); revision 2 folds the peers' answers and R4's review, marked "(answer X)" or "(R4 review Bn/Nn)"; approved as H0 by R5-M182. Working record, not authority: what it decides becomes authority only as the DECISIONS entries H3 writes (§10).

Working time (DECISIONS Q12): 17:40–18:02 on 2026-11-02, about 22 minutes, one session, within the one-hour limit.

Base: dev `1ec539c79` (code, DECISIONS, PLAN). "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. "mir" = `KIMI_PIPELINE=mir`. Facts are read from the base; no build,
test or probe was run. (H) marks an unchecked inference. Standing request: the owner's 「その役割分担でOKです。H0の設計から始めて下さい。
終わったら、R4の完了を待ってください。」 (2026-11-02): R4 owns H3's publication in `MirReport`, R5 the Core wiring, BU the Binding facts.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| H0a-1 | One **publisher** in Core maps each `MirReport` record to the diagnostics adapter (keys, fact table, cause resolver). H3-2 lands it with lowest-layer tests; until H5 it publishes today's rows only (construction and collector records, validator findings), unchanged on every pipeline; from H5 every MIR problem on every pipeline. No pipeline column (review B1) | 2 |
| H0a-2 | Removed (review B1): no mir acceptance before H5; from H5 acceptance is computed from the published rows (H0b-3, ruling R1) | 2 |
| H0a-3 | **H3-1 (bp)** folds the construction records and the collector records into `MirReport` as `MirProblem(code, ControlFlow, subject, Records, all views)`, with their own body value `MirProblem.Records`, so they never share a key with A4's limit records or C1-F4's `InternalInvariant_Kd` (`NoBody`) (R4 review B1); it lands in R4's chain before 2g-1 (R4 review B2); `MirReport` resets with the version in `MirProgram.Clear`, not in `MirAnalysis.Run`; the two `reported` sets and both `Records` lists go. Validator findings stay `MirProgram.Findings` (the driver reads them) and publish through a table row | 3.1 |
| H0a-4 | The merge across bodies by (code, subject, Reason) is a **record view owned by `MirReport`** (`MirReport.Records`), so R4-M132's derived merge has one copy; Core only maps records to the diagnostics adapter. R4's conditions (answer R4-1): it exposes each record's member problems; it is computed once per version and pooled; a cross-body disagreement is its own record | 3.2 |
| H0a-5 | A record's requirement is its problems' own (R4-M67, R5-M23), never derived from the code; a cross-body disagreement is the `InternalInvariant_Kd` that `Add` already gives. Partition = the requirement's (DIAGNOSTICS rule 1). Binding.* → Ownership.* renames stay at H5 (R5-M23) | 3.3 |
| H0a-6 | Key: a record with facts at its subject node, condition = its Reason's ordinal among the records of its (code, subject); a location-only code by its location (`ReportLocation`, R4-M71) | 3.4 |
| H0a-7 | One **fact table**, one row per `MirFactKind` (R4-M132's R5 condition); views give the case evidence and Note through Q8-6's one renderer; no other Note text; explicit rows for `CallBorrows` and the containment edge (Value −1); no new column (answer R4-6, R4 review N1–N3) | 4.1 |
| H0a-8 | A record with `Cause` facts is `PrerequisiteUnavailable_Kd` (R4-M132 OQ-2 (a)) with `DerivedFrom` = its causes' record keys, or `Binding.FailureCauses(site)` for a Binding cause (`MirFact(Cause, Site, −1)`, Site a Binding-failed node, answer R4-3); the owner's finalization suppresses it (DIAGNOSTICS §4.3); a null `FailureCauses` is never an internal error until H3-2's probe shows it unreachable (answer BU-2) | 4.2 |
| H0a-9 | PLAN10 P10's fallback gets **no new producer**: every MIR incompleteness already has its record; DIAGNOSTICS rule 8's contract check stays the net | 4.3 |
| H0a-10 | Effects (PD29): no MIR analysis problem publishes before H5, so A4 needs no hold and the old analysis no edit; A4's problems publish at H5, which deletes the old effect checks (review B1) | 5.1 |
| H0a-11 | G128 (PD30): R4-M153 gave A5's `CallRetention.Limit` `OwnershipDepthLimit_Kd`, so PD30's premise (one code, `Unsupported_Kd`) no longer holds. Recommended (A): no rule; where both meet one call, the collector's `StorageNesting` record and A5's depth record both stay, counted, until R7 removes the bound (Q13); confirmed by R4's `Chain<i32>` reproducer (answer R4-5) | 5.2 |
| H0a-12 | Independence (SPEC 23.1.2, Q13): §6's table; the only dependents are records with `Cause` facts | 6 |
| H0a-13 | Owners: R4 the Mir files and `MirReport.Records`, R5 the Backend and Core files and the publisher, BU Q8-6's renderer and `FailureCauses`; review: H3-1 R, H3-2 D+R under H0 with one review (Q13) | 7 |

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

## 2. The publisher and what it publishes (H0a-1; review B1)

Classes are identified by published columns only; the publisher lives in Core (§7) and is the one place mapping a record to the
diagnostics adapter. No column depends on `KIMI_PIPELINE` (review B1).

| Class | Identified by | Until H5 (every pipeline) | From H5 (every pipeline) |
| --- | --- | --- | --- |
| Construction and collector records | requirement `ControlFlow`, body `Records` (R4 review B1) | publish (today's) | publish |
| Validator findings | `MirProgram.Findings` | publish (today's) | publish |
| A3 (`RequireFallthrough_Kd`) | `ControlFlow`, a body | unpublished (the old CFA's twin publishes) | publish; the old CFA's report stops (ruling R2) |
| A1, A2, A4, A5 problems, `MirReport`'s disagreement record, C1-F4 findings | Ownership partition or `InternalInvariant_Kd` | unpublished (R4-M1) | publish (A4's with H5's deletion of the old effect checks, PD29) |
| Old analysis issues | `Ownership.Issues` | publish | none: the old analysis runs only for its test and benchmark readers (review B3); code deleted at H10b (ruling R2) |

- H3-2 lands the publisher with its lowest-layer tests (§8); until H5 Core calls it for today's rows only (the selection `MirRecords`
  makes today, through H3-1's one helper), so H3 changes no published record on any pipeline (bp) and R5-M156 holds.
- At H5 the selection goes (its removal condition): every record publishes on every pipeline, and `Analysis` and `Problems` read the
  same rows (H0b-3; ruling R1: no `Binding.LintErrors` term). VERIFICATION `:94` is unchanged at H3.

Rejected: publishing MIR problems beside the old analysis's (every failure twice; equal keys with different facts fault in
`DiagnosticOwner.Record`); a mir-only column between H3 and H5 (review B1: it lost its consumer, since H3b runs on the flip branch
with H5 applied; it would be publication steered by the pipeline mode, an A4 hold picked by shape, ~10 lines in the D5-frozen old
analysis, a mir lane failing until H5 and a known unsound mir acceptance).

## 3. `MirReport` as the one record source

### 3.1 H3-1: the fold (H0a-3; R4-M3a's stage ledger item; behavior-preserving)

- `MirProgram.Report(site, code)` adds `MirProblem(code, ControlFlow, site, Records, ulong.MaxValue)` to `Problems`; `CheckedSet.Report`
  does the same through the program it collects. `MirReport`'s key (code, subject, Records, default Reason) gives today's "once per code
  and subject". `Records` is a body value of its own (R4 review B1): with `NoBody`, a construction or collector `Unsupported_Kd` and an
  A4 limit at one subject would be a spurious `InternalInvariant_Kd`, and a construction `InternalInvariant_Kd` beside C1-F4's would make
  `Add`'s disagreement recurse (`MirReport.cs:63-67`); the `reported` sets and the `Records` lists go (`MirProgram.cs:30, :67-84`, `CheckedSet.cs:30, :80, :100-107`).
- `MirReport` belongs to the version: `MirProgram.Clear` resets it with the bodies, and `MirAnalysis.Run` no longer does (`:69`), since
  construction records precede the analyses. `Drops` keeps its reset in `Run`. The collector runs after the analyses and a warm re-run adds
  equal keys, which merge.
- `Compilation.MirRecords`, `MirDump.cs:42` and the 49 test reads of the two lists (about 28 files, grep at the base) read the class rows
  through one helper. MIR goldens and the semantic snapshot stay equal.
- Order (R4 review B2): H3-1 lands in R4's chain before 2g-1 (PD1: `MirReport*.cs` are chain files), so 2g-1 has one key path. R4
  writes the Mir hunks; R5 consents to and reviews the `CheckedSet`, `InstanceCollector` and `Compilation.Pipeline` hunks. The reset move
  is safe (answer R4-2): `MirBuilder.Build` clears only for a new HIR version, before `Run` (`MirBuilder.cs:44-67`).
- Gain: one record representation and merge; a construction record can be a `Cause` by index (2g-1's cause sites include "a MIR
  construction record's subject", plan A5 step 2 row 2g-1). Rejected: folding at H5 (the H5 commit, already ≈ ±300/−2,700, grows, and
  2g-1's causes would need a second key path); folding validator findings (the driver reads `Findings` to skip analysis; one row suffices).

### 3.2 The merge across bodies (H0a-4; R4-M71, R4-M72's AR-4 hand-over)

`MirReport.Records` (R4, new partial `MirReport.Records.cs`) gives one record per (code, subject, Reason) in first-problem order: its
requirement, its first problem's facts, derivedness and causes merged by `Add`'s own rule (direct wins, causes unioned, R4-M132), and
each `Cause` index remapped to its record's index. Pooled; a warm version allocates nothing. Case views stay per body: a record's case
evidence is its first problem's when every grouped problem limits to the same case text, else none (best effort, Q13). R4's conditions
(answer R4-1): `Records` exposes each record's member problems (index range, each with its body and views), since only the publisher's
renderer can decide "same case text"; it is computed once per version and pooled; it emits a cross-body requirement disagreement as a
record of its own (a read cannot call `Add`). `MirReport.cs:15` loses "(R4-M11's merge … is publication's)"; R4-M71 gets its amendment
line in H3-2.

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
| EffectRead, EffectWrite, EffectUnknown | related location: the static Field's declaration; A5's err(effect) rows only. A4's problems carry no facts (`MirEffects.Solve.cs:108-118`), so the Binding effect record → A4 move also loses "the violating effect" (class 3; R4 review N2) |
| Relation (+ the problem's `MirReason`) | the Origin-relation evidence alternative; the C1-F4 row (Value) gives `source` (Fit → "fit", Declared → "declared", WellFormed → "wellFormed", CallBorrows → "fit") and, for a fit, `destination`; the relation and ends come from `MirReason`; rendered through one Binding renderer taking (`MirReason`, `OriginRow`) (answer R4-6) |
| Relation, Value −1 (the containment edge) | its own row: no destination, "wellFormed" (answer R4-6, R4 review N3) |
| Required, Limit; Table | `OwnershipStorageLimit_Kd`'s arguments; its `table` evidence |
| Cause | `DerivedFrom` (§4.2) |
| Inlined | never reaches publication (`Add` drops it) |
| a kind without a row | `InternalInvariant_Kd` at the subject (R5's condition) |

Lost against the old analysis, counted as class 3 at H3b, no new column (answer R4-6, R4 review N3): the Borrow source shown for a
"borrow" end, `FixedBy` and `Substituted`. A `NoBody` record (A4's) takes no case fact (R4-M71; R4 review N1).

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

The Binding cause has one form (answer R4-3): `MirFact(Cause, Site, −1)`, Site a node Binding failed (its `FailureCauses` non-empty),
possibly not the subject (the Opaque site's node, PD17). A cause at a construction record's subject is a MIR cause by index once
H3-1 has folded those records. Keys are stable (answer BU-2): no `Bind` or edit runs between `Analyze` and `ReportAnalysis`
(`Project.cs:538-551`), keys compare nodes by reference, and a twin's cause is Binding's own condition-0 record. But `FailureCauses(site)`
is null where Binding left the site resolved (a rejected assignment target, G353's Field selection, a resolved recovery node, an
unreadable char literal), so 2g-1 records its cause at the failed node. A null becomes `InternalInvariant_Kd` only after H3-2's probe
over those four shapes shows it unreachable; until then a reachable null publishes the record without `DerivedFrom` (reported, never
suppressed, never an internal error; answer BU-2).

### 4.3 PLAN10 P10's fallback (H0a-9)

P10/DIAGNOSTICS rule 8: a phase ending incomplete with no Error reports one `PrerequisiteUnavailable_Kd` fallback. The MIR phase never
ends incomplete without a record: an arena skip records `OwnershipStorageLimit_Kd` (R4-M15a, R4-M132 U4); validator findings stop the
analyses and publish as `InternalInvariant_Kd`; an incomplete Binding has Binding's Errors; an analysis exception is `CheckFaulted_Kd`.
From H5, H0b-3 computes acceptance from the published rows (review B1), so a rejection without a record cannot arise; the owner's unexplained-
rejection check stays the net. Rejected: a MIR fallback producer (no reachable case; a symptom catch-all).

R5-M96's interim (an arena-skipped body refused at generation as a located `Unsupported_Kd` until a record carries the facts) stays on
every pipeline until H5, which deletes the refusal and states its contract (H0b-6, review N2). R4-M132 U4's `Incomplete` index is
not read by H3.

## 5. One producer per failure

### 5.1 Effects (H0a-10; PD29; review B1)

- No MIR analysis problem publishes before H5 (R4-M1, §2), so from H3 to H5 every effect failure keeps its one producer, Binding's and
  the old analysis's effect checks, and A4's problems need no hold. H5 publishes A4's problems with every other record and deletes
  those checks in the same commit (PD29, H0b §2).
- Removed (review B1): the A4 hold under mir (a selection by shape), the old effect tail's ~10 lines in the D5-frozen old analysis
  and its mir-only gap issue (a known unsound mir acceptance).

### 5.2 G128 `Apply` null (H0a-11; PD30)

Confirmed (answer R4-5, R4 review N4): on dev `bdf4eb6b8`, `ComponentSummaryTest`'s `Chain<i32>` program publishes the collector's
`Unsupported_Kd` at `Chain<i32>.init()` and `need(c@move)`, where the ledger's a5-depth row has A5's `OwnershipDepthLimit_Kd`; the codes
differ, so (A) holds. PLAN G128's row correction cites both.

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
  rows), overrides BU-12's owner choice for those sites, needs a rule to tell a collector `NoBody` depth record from A4's, and
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

Q13's regressions apply to H3 as: none may appear on any pipeline (bp: H3 changes no published record, review B1); the analyses'
records are the flip's, classified at H3b and judged at H5. A dependent record removed by rule W in an R4 unit is counted there.

## 7. Owners, units, files and size (H0a-13)

| Unit | Owner and files | Size (H) |
| --- | --- | --- |
| H3-1 fold (bp; in R4's chain before 2g-1, R4 review B2) | R4: `C/Mir/MirProgram.cs`, `C/Mir/Analysis/MirAnalysis.cs` (`:69`), `MirReport.cs`/`MirProblem.cs` contracts (`MirProblem.Records`), `C/Mir/MirDump.cs`; R5 consents and reviews: `C/Backend/Instances/CheckedSet.cs`, `InstanceCollector.cs`, `C/Core/Compilation.Pipeline.cs` (`MirRecords` → the class rows) | +30/−45, t±60 |
| H3-2 publisher (default unchanged, review B1) | R4: `C/Mir/Analysis/MirReport.Records.cs` (new, §3.2). R5: `C/Core/Compilation.Publication.cs` (new partial with its entry contract: what it publishes §2, keys §3.4, fact table §4.1, cause resolver §4.2), `Compilation.Pipeline.cs` (`ReportAnalysis` and `MirRecords` through the publisher for today's rows). Docs: DECISIONS (§10), ARCH "Analysis records" row (`:99`), CODEMAP, PLAN (§5.2's count). No old-analysis, `Compilation.Switch.cs` or VERIFICATION edit (review B1) | MirReport +70; Core +150/−10; t +250 |

Total about +250/−55 source (review B1: no old-analysis edit, no `Analysis`/`Problems` change before H5), above the plan's ±200 (the fact table and keys were not in the row). Prerequisites: H3-1 before 2g-1 and OQ11 (given) (R4 review B2); H3-2 R4's exit with A6
(2g-D–2g-7; R4-M35), G11 (done, R5-M167), Q17 (landed), Q8-6's renderer (H1's first commit, bp, BU, landing early on dev as Q8 allows, before H3-2: integration, H0b §3), OQ11's approval of H0. Review: H3-1
one R review (bp); H3-2 D+R under H0 (plan row H3), one review (Q13); R4 reviews the Core hunk's use of `MirReport.Records`, R5 the
`MirReport.Records` contract. Holds: none after R4's exit (plan §3.5); `Compilation.Pipeline.cs` is R5's from H3 to H5.

## 8. Tests (lowest layer that observes each rule)

- `T/MirAnalysisTest.cs` (`MirReport`'s rules; beside `ARepeatedProblemMergesItsViews` `:117` and `AMergeIsDerivedOnlyWhenEveryReportIs`
  `:184`): records merge across bodies by (code, subject, Reason) (R4-M71's r05: two constructors, one record; two Reasons, two records);
  a record is derived only when every grouped problem is; a `Cause` index names its record after the merge; a cross-body requirement
  disagreement is its own record (answer R4-1); construction and collector records are `Records` `ControlFlow` problems, and a
  construction record and an A4 limit at one subject stay two problems (H3-1; R4 review B1); `Records` exposes member problems; warm
  `Records` allocates nothing (`Purpose=Allocation`).
- `T/MirPublicationTest.cs` (new: the publisher is a new component): one row per fact kind (each kind's published form; a kind without
  a row → `InternalInvariant_Kd`); `OwnershipStorageLimit_Kd`'s arguments and `table`; an Origin relation's evidence; case evidence
  from views; a derived record → `PrerequisiteUnavailable_Kd` naming its MIR cause, and naming `FailureCauses` for a Binding cause
  (from 2g-1), suppressed when its cause publishes; the four null-`FailureCauses` shapes (answer BU-2); the containment edge's row and the `Chain<i32>`
  program's two records (answers R4-5, R4-6); two relations at one subject → conditions 0 and 1; a `for` expansion's siblings stay
  one location-only problem (`PipelineSwitchTest.MirRecordsAtOneLocationAreOneProblem` `:317` stays as the default's row).
- `T/PipelineSwitchTest.cs`: unchanged at H3 (every pipeline publishes the same rows as before, `EveryPipelinePublishesTheMirRecords`
  `:269` re-pointed to the class rows); the flip's rows (MIR's `MovedPlace_Kd` published, the old one not; an old-only rejection
  accepted; an effect violation published once; a lint error still rejecting through `Accepts`) are H5's tests (H0b; review B1).
- Snapshot: default Session with `-DiagnosticSnapshot` equal (bp, both commits) and a mir run equal too; no mir census (H3b's rehearsal
  on the flip branch is the census, review B1). Ledger unchanged (no ledger edit).

## 9. Questions and their answers (revision 2)

| # | To | Answer (source: R4's and BU's answers, 2026-11-02) |
| --- | --- | --- |
| R4-1 | R4 | Yes, with conditions: member problems exposed, once per version and pooled, the cross-body disagreement its own record (§3.2) |
| R4-2 | R4 | Yes: the reset moves to `MirProgram.Clear`; the fold needs B1's key fix and lands before 2g-1 (§3.1) |
| R4-3 | R4 | Withdrawn (review B1). |
| R4-4 | R4 | (index R4-3) Yes: one Binding form, `MirFact(Cause, Site, −1)`; a construction record's cause is by index after H3-1 (§4.2) |
| R4-5 | R4 | (index R4-4) (a) twins: no cause, class 4 counted to H11 (R4-M108's H11 list, H0c); (b) G353's refused stored Type: dependent, a small R4 A5 item after BU-73, class 4 until then |
| R4-6 | R4 | (index R4-5) Yes: the `Chain<i32>` reproducer gives both records at the same calls (§5.2) |
| R4-7 | R4 | (index R4-6) Partly: `source` and `destination` yes; the containment edge gets its own row; Borrow display and `FixedBy` are class-3 losses (§4.1) |
| BU-1 | BU | Yes: H1a and Q8-6 (±60–70 each) before H3-2; Q14 before H1's code; queued after G216 (a) |
| BU-2 | BU | Stable for Opaque sites and twins; null where Binding left the site resolved: 2g-1 sites causes at the failed node; H3-2 probes the four shapes (§4.2) |
| O-1 | owner | None (R5-M182 approved H0). |

## 10. Departures and DECISIONS text (H3 writes them)

- R5-M156 holds through H3: `KIMI_PIPELINE=mir` changes no published record until H5 (review B1: the mir column is dropped).
- R4-M71/R4-M72 (AR-4): the merge across bodies is `MirReport.Records`' (§3.2), accepted by R4 with its conditions (answer R4-1);
  `MirReport`'s contract loses "the merge … is publication's". Reason: one copy of R4-M132's merge.
- R4-M3a's stage item done at H3-1 (construction records folded), extended to the collector's records (plan row H3).
- `MirProgram.Problems`' "only `MirAnalysis` resets them" → `MirProgram.Clear` does.
- PD30's single-owner clause → (A) of §5.2 (two records at a G128 call until R7), if adopted.
- Plan rows H0/H3 "derived requirement … Q17's table": void since R5-M23 (recorded, no change).

Draft entry (H3-2): "R5-M… One publisher of MIR records (R6-check H3). `MirReport.Records` groups the problems by (code, subject,
Reason) with `Add`'s derived merge; Core's publisher maps each record through one fact table (one row per `MirFactKind`), conditions by
Reason ordinal and derived records as `PrerequisiteUnavailable_Kd` with `DerivedFrom`; until H5 it publishes today's rows only, on every
pipeline, and H5 publishes every record (review B1). Supersedes R4-M71's placement and PD30's single owner. Rejected: §2, §3, §5's
rejected options, among them a mir-only publication between H3 and H5 (review B1)."

## 11. Not done

- No reproducer was run (R4-6's co-occurrence, the 49 test reads re-measured) — design only, per the brief.
- The H5 flip, H4, the snapshot classification method, the deletion order, H9 and the SPEC/IMPL pass are other H0 parts.
- The exact role names for related locations (§4.1) follow the old analysis's roles and DIAGNOSTICS' A6 table; H3-2 fixes them.
