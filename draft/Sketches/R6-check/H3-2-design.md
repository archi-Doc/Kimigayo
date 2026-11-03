# R6-check H3-2: implementation design of Core's publisher of MIR records

Status: implementation design under H0 ([H0-design](H0-design.md) row H3-2; [H0a](H0a-publication.md) §2-§10; [H0b](H0b-flip.md) §2.3;
DECISIONS R5-M182 and its amendment), revision 1 (review 1's B1 and N1-N8, R4's and BU's answers; table at the end), accepted by design review 2 (its N-a to N-d folded in by the planning session). Working record,
not authority: the DECISIONS lines of §11 become authority in the units' commits. Base: dev `a6f8612d0`; H3-1 (R4, not landed) is
designed against as GO'd on 2026-11-03 (two body values, one `MirProgram` class helper). Working time (Q12): 14:17-14:31 (revision 0)
and 14:49-14:54 (revision 1) on 2026-11-03, logged in the designer's state file. No build, test or probe was run. Paths: `C/` =
`src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. (H) marks an unchecked inference.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| D1 | The publisher is one Core component, `C/Core/Compilation.Publication.cs`: one merged problem → one adapter call. Until H5 Core calls it for today's rows only (construction rows, findings, collection rows, in today's order); its merged-problem path has no production caller until H5 (removal condition of the selection: H5) | 1, 3.1 |
| D2 | Input: `MirReport.Merged` (U1a/U1b; H0a's `MirReport.Records`, renamed, N7) keyed (code, subject, Reason, class), class = Construction, Collection or analysis (every body ordinal and `NoBody`); members, ordinal, merged derivedness, Cause Values as merged indices, own disagreement record, pooled, once per version | 2 |
| D3 | Key form: one predicate `Located(p)`: a class row, or a code of category Unsupported, Resource or Internal without catalog Arguments → `Locate` (`ReportLocation`); every other problem → `Koto.Report` at its subject, condition = its ordinal. A located problem carries no fact (N3) | 3.2 |
| D4 | One fact table: one switch arm per `MirFactKind`; its discard arm (a kind without a row) and `Inlined` are `InternalInvariant_Kd` at the subject, pinned by a test (N1) | 3.3 |
| D5 | One cause resolver: Value ≥ 0 → the cause's key by the same key function; Value −1 → `Binding.FailureCauses(site)`, null → `InternalInvariant_Kd`; a cause naming no problem is `Add`'s, as a requirement disagreement is | 2, 3.4 |
| D6 | **Problems resting on a Binding failure 2g-1 has not sited publish as their own direct records** (option A, agreed by R4): no code, no identification; dependent under Q13; removal condition 2g-1 (G396), which also owns the probe of every Binding cause | 4, 5 |
| D7 | Five units, all written by R5's implementer (U1a, U1b with R4's consent on the `MirReport` hunks); none merges to dev before H3-2's one review (N8); ≈ src +290/−45, tests +300 | 9 |

## 1. The chain

- **Meaning.** SPEC 23.1.2 (every independent failure reported; a dependent one derived from its prerequisites) and 23.2.2; DIAGNOSTICS
  rules 1-3 and §4.3 (`PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is non-empty; only Errors are derived); Q13 (acceptance
  exact, record selection best effort; three regressions); R4-M132 (Cause facts → derived).
- **Representation.** Read: `MirReport.All`/`Facts(i)`, H3-1's class values and class helper `MirProgram.Records(class)`,
  `MirProgram.Findings`, U1's `MirReport.Merged`, `MirBody.Binders`, `TyCtxt.OriginRows`. Written: adapter calls only (`Koto.Report`,
  `Koto.ReportDerived`, `DiagnosticCollection.ReportLocation`). No table of its own.
- **Owner.** Core's publisher (R5; H0a §7). It decides how a problem is published, never whether it exists, whether it is dependent (a
  Cause fact or nothing; §4) or its requirement (the recording rule's, R5-M23).
- **What becomes unnecessary.** U0 moves `MirRecords` and the MIR half of `ReportAnalysis` out of `Compilation.Pipeline.cs` and folds
  `Compilation.Generation`'s private `ReportLocation` (the same shape: a `SourceRef` record keyed by location, requirement Emission,
  R5-M65) into `Locate` (N7). The selection of today's rows stays until H5: H5 publishes every merged problem and `Analysis`/`Problems`
  read the publisher's rows (H0b §2.1); the class helper then has no Core reader. `MirReport`'s contract loses "(R4-M11's merge … is
  publication's)".

## 2. Input contract with `MirReport` after H3-1 (answer R4-1)

From H3-1: `MirProblem.Construction` and `MirProblem.Collection` (no body, distinct from `NoBody`); `MirProgram.Report(site, code, class)`
the one writer; the class helper `MirProgram.Records(class)` the one non-allocating path to a class's (code, subject) rows;
`MirProblem.IsRecord`; `MirReport` reset in `MirProgram.Clear`. Class rows: `ControlFlow`, every view, default Reason, no facts.

U1's view is named `MirReport.Merged` (new partial `C/Mir/Analysis/MirReport.Merged.cs`), not H0a's `MirReport.Records`, because H3-1's
class helper and `IsRecord` already name the class rows "records" (one name per concept, N7). Computed on its first read after an `Add`
or `Clear`, pooled (a warm version allocates nothing):

- One merged problem per (code, subject, Reason, class), in first-member order. Class keeps the two record classes apart from each
  other and from the analyses (H3-1's F2 stays fixed; no class row meets an `Ownership.*` requirement, R4 review B1). Across body
  ordinals and `NoBody` it merges (R4-M71's r05; Q-R4-1): A4 (`NoBody`, `MirEffects.Solve.cs:112-118`) and A5 (a body,
  `MirBorrows.Reports.cs:104-108`) both produce `Unsupported_Kd` and `OwnershipDepthLimit_Kd` under the same requirements
  (`Ownership.Unsupported`, `Ownership.DepthLimit`), so at one subject and Reason they merge into one and never disagree; A4's
  obligation codes are never A5's.
- Each exposes: requirement, code, subject, Reason, its members (an index range into a pooled list of `All` indices, hence bodies and
  views), its merged facts (the first member's non-Cause facts, then the Cause tail by `Merge`'s rule: direct wins, causes unioned), and
  `Ordinal`, its Reason's position among the merged problems of its (code, subject, class) (Q-R4-4).
- **Cause contract (N2).** In `Merged` a Cause Value ≥ 0 is a merged index: each `All` index is remapped to its merged problem's index
  (total: every problem is a member of one), then duplicate causes are removed (two members naming problems of one merged problem give
  one cause); Value −1 (2g-1's Binding cause) is kept. U1b amends `MirFact`'s contract line accordingly (an `All` index in `MirReport.All`,
  a merged index in `Merged`). R5's condition (a cause naming no problem is a broken contract, never a silent drop) is `Add`'s, in the
  form of its requirement-disagreement rule: a problem with a Cause Value that is neither −1 nor an index of `All` is not added, one
  `InternalInvariant_Kd` (`Ownership.Internal`) is added at its subject, and `Add` returns −1 (rule W's `CauseFacts` writes only `All`
  indices, `MirBorrows.Causes.cs:296`, so no producer reaches it).
- Members naming two requirements give one more merged problem: `InternalInvariant_Kd`, `Ownership.Internal`, at the subject (a read
  cannot call `Add`). It cannot disagree again: every analysis `InternalInvariant_Kd` names `Ownership.Internal` (Q-R4-2;
  `MirBorrows.Reports.cs:90-91`, `MirOrigins.cs:138-141`, `MirReport.cs:65-66`), pinned by U1b's contract line and test.

## 3. The publisher (R5)

### 3.1 Entry and selection

`Compilation.Publication.cs` (partial of `Compilation`; contract header: responsibility, inputs, outputs, invariants, prohibitions as
§1). Members: `PublishMirRecords()` (Core's call from `ReportAnalysis`: today's rows: the class helper's Construction rows, findings,
its Collection rows, each through `Locate`); `Publish(MirProgram, int merged)` (one `Merged` entry; tests call it; H5 makes
`PublishMirRecords` loop over every merged problem and deletes the class loop); `Locate(requirement, code, subject)` (the location
form: class rows, findings, located problems, and code generation's records). A finding publishes as `InternalInvariant_Kd`,
`ControlFlow`, located (today's row). No pipeline mode, flag or optional parameter steers it.

### 3.2 Keys and conditions (H0a-6)

`Located(p)` = class Construction or Collection, or the code's catalog entry has category Unsupported, Resource or Internal and no
Arguments. Today's row codes (checked: `DefaultExpansionLimit_Kd`, `GenerationResourceLimit_Kd`, `InternalInvariant_Kd`,
`OversizedLayout_Kd`, `InvalidCLayout_Kd`, `Unsupported_Kd`) and `OwnershipDepthLimit_Kd` are located; `OwnershipStorageLimit_Kd`
(Arguments) and every Language and Proof code of A1-A5 key at their subject. **Invariant (N3):** a located problem carries no fact, since
`ReportLocation` takes none (class rows have none; A4's and A5's `Unsupported_Kd` and `OwnershipDepthLimit_Kd` rows are "no fact",
DIAGNOSTICS' A6 table); one with a fact publishes `InternalInvariant_Kd` at its subject (a broken contract, U2 test). A non-located
problem calls `Koto.Report(requirement, code, …, condition: (ushort)Ordinal)`; two relations at one subject are conditions 0 and 1. One key
function `KeyOf(p)` serves emission and the resolver: located → `DiagnosticCollection.LocationKey(requirement, range, document)`
(extracted from `ReportLocation`, which then calls it; U2), else `Koto.KeyOf(requirement, condition)`.

### 3.3 The fact table (H0a-7)

| Kind | Published as |
| --- | --- |
| Anchor, Use, Store, Reservation, Evaluated | related locations, roles and wording from DIAGNOSTICS' A6 fact tables (`loan`, `reservation` as the old analysis names them; U3 fixes the rest there) |
| EffectRead, EffectWrite, EffectUnknown | related location: the Field's declaration, DIAGNOSTICS' effect-fact wording |
| Relation (Value ≥ 0; −1 the containment edge) | `Binding.OriginRelationFacts` (the shared renderer; no per-pass diagnostic table, Q-BU-1) from an `OriginRelationFact`: `At` = `SyntaxOf(row.Site)`, `Clause`, `Destination` and source from `TyCtxt.OriginRows(body)[Value]` (CallBorrows → fit), `WellFormed` = source WellFormed, ends from `MirReason`, `Supplier = Longer`; −1: no row, `wellFormed`, no destination |
| Required, Limit, Table | `OwnershipStorageLimit_Kd`'s `requiredBytes`, `limitBytes` and `table` evidence |
| Cause | the resolver (§3.4) |
| Inlined; the discard arm | `InternalInvariant_Kd` at the subject (`Add` drops Inlined; reaching here is a broken contract) |

`Supplier = Longer` takes the row end, resolved after receiver resolution (Q-BU-1): inside a classified Closure the longer end shows as
`call receiver` or `static`, without the Borrow's related location. Accepted by the planning session as a Q13 evidence difference and an
open issue (a C1-F4 supplier column would recover it: R4's view, BU reviewing). Case evidence, the case Note and binder declarations:
`Binding.CaseFact(body.Binders, views & mask, …)` (BU-82), the binders `MirBody.Binders` of each member's body, its views masked by
`MirCases.Count(body)` first, since some producers write every view as `ulong.MaxValue` (Q-BU-2); given only when every member gives the
same case text, else none (Q13); a `NoBody` member gives none. Lost against the old analysis, as H0a §4.1: the Borrow source, `FixedBy`,
`Substituted`, the hand-built Notes (class 3 at H3b).

### 3.4 The cause resolver (H0a-8)

A problem with Cause facts publishes as `Koto.ReportDerived(requirement, causes, condition)`; causes: Value ≥ 0 → `KeyOf(Merged[v])`;
Value −1 → `Binding.FailureCauses(SyntaxOf(site))`, null → the problem publishes `InternalInvariant_Kd` at its subject (H0a §4.2, R4 r2
N1). The owner's finalization suppresses a derived record whose causes all reach a published direct Error (DIAGNOSTICS §4.3). Before
2g-1 only rule W's MIR causes (Value ≥ 0) reach the resolver.

## 4. Problems resting on a Binding failure 2g-1 has not sited

Facts: no `MirFact(Cause, Site, −1)` exists before 2g-1 (`MirFact.cs`: −1 reserved); R4 counted 476 MIR-side records in 356
Binding-rejected compilations at `1eb0cbaa9` (A5 214, A1 97, A4 73, A3 34, U 48), 0 on valid programs. They carry no fact saying they
are dependent.

| Constraint | (A) direct MIR record | (B) located `PrerequisiteUnavailable_Kd` without `DerivedFrom` |
| --- | --- | --- |
| Identification | none: a problem without a Cause fact | a rule: Binding incomplete anywhere (a global gate), or `HasFailureCauses(subject)` (2g-1's siting copied into the consumer; misses Opaque sites that are not the subject, PD17) |
| AGENTS: no analysis deciding dependence beyond direct inputs | holds (nothing suppressed; "otherwise report it") | broken by either rule; the gate also turns independent problems of a rejected program into prerequisite records (a lost independent record, Q13) |
| DIAGNOSTICS §4.3 invariant | holds: a direct record has empty `DerivedFrom` | must carry the unresolved mark, which `DiagnosticOwner.Explain` never satisfies: 476 visible records of rule 8's fallback form (H) |
| DIAGNOSTICS §4.3 "Later phases" | departs for A3's 34 (below) | the same 34 as unexplained prerequisites |
| H0a §5 one producer per failure | holds: another requirement than Binding's failure | same |
| Cost | 0 lines | a gate or a per-problem Binding read, tests, a later removal |

**Chosen: (A)**, agreed by R4 (Q-R4-3; it supersedes R4's earlier suggestion of a located `PrerequisiteUnavailable_Kd`). The dependence is
a fact its owner (A6's 2g-1, R4) publishes; the publisher must not decide it. From H5 the 476 publish with their MIR codes beside
Binding's Errors on the 356 rejected programs, counted by code at H3b (Q13: added on a program the base already rejects), no acceptance
move; at 2g-1 each gains its Cause −1 fact and is suppressed by its Binding Error's record (counted in 2g-1's commit). (B) would publish
them as unexplained prerequisites from H5 and move them again at 2g-1.

- **Departure (B1 c).** From H5 until 2g-1, A3's 34 `RequireFallthrough_Kd` records that read Binding's result at a failed node, or rest
  on a guessed recovery, publish direct, where DIAGNOSTICS §4.3 "Later phases" makes such a control-flow check derived (`Binding.FailureCauses`;
  H0b §2.3: A3's guessed-recovery derived form needs 2g-1's cause). Removal condition 2g-1. The contract changes at H5, so H5's commit
  (not U2's) adds one dated exception note at the end of that bullet, covering both its sentences (the check that reads Binding's
  result, and the `GuessedBy`/`GuessedCompletion` one): "Exception (R5-M182's H3-2 line; from H5 until 2g-1, PLAN G396): the MIR's
  A3 publishes such a `RequireFallthrough_Kd` direct, since its Binding cause or guessed recovery is not yet sited as a Cause fact."
  (Review 2, N-a.)
- **Not covered (N5).** (A) covers dependent Language, Proof, Resource and Unsupported records only. An analysis `InternalInvariant_Kd`
  among the 476 is not a dependent record under (A): it is a Q13 regression at H3b (a new `InternalInvariant_Kd` on any program).

## 5. BU-2's probe (H0a §4.2, §8; B1 a)

- **Every Binding cause has a non-null `FailureCauses`:** no subject before 2g-1 (H3b's census sees no Cause −1 unless 2g-1 lands
  first). Owner: 2g-1's contract test (R4), beside BU-2's four shapes; the superseding line names it, and the planning session adds it to
  G396.
- No MIR-level reproducer of the four shapes exists (BU, 2026-11-03); nearest rows: `ClosureReceiverClassificationTest.cs:293`,
  `DiagnosticPrecisionTest.cs:48`, `PropertyBindingTest.InferredClosures.cs:34-50`, `MirInitTest.cs:509-512`, `HirFormsTest.Errors.cs:94-100`,
  `MirBuildTest.Values.cs:478-499`. U2 and U3 test the resolver with synthetic Cause −1 facts (`MirReport.Add` in the test): at a node
  Binding failed → `DerivedFrom` = its `FailureCauses`, suppressed; at a resolved node (the nearest rows' resolved sites) →
  `InternalInvariant_Kd`.
- Corpus half of the publisher's own contract: H3b's census runs every merged problem through the publisher on `claude/r6-flip` with H5
  applied; a publisher `InternalInvariant_Kd` there is a Q13 regression and blocks H5. No extra heavy run in H3-2.

## 6. H0a §4.3's fallback against G157 (1) (R5-M183, U2 landed `7ec8030fd`)

"Validator findings stop the analyses" is outdated: a body with a finding is excluded from the analyses and the collector, every other
body is judged, and a reader of the excluded body records nothing or composes it as Derived. The MIR phase still never ends incomplete
without a record: the excluded body's record is the validator's own `InternalInvariant_Kd`, published by `PublishMirRecords`.
Conclusion unchanged: no fallback producer (H0a-9). The U0 DECISIONS entry states the corrected sentence.

## 7. No published change at H3 (bp)

U0 moves code; U1-U3 add members without a production caller. Evidence on the last unit's head against H3-1's merge (same worktree):
default Session with `-DiagnosticSnapshot` (0 differences); `SemanticSnapshotTest` with `KIMI_SEMANTIC_MIR=1` unchanged; MIR goldens
unchanged; the mir ledger run (registered heavy run) equal to the base's mir-diff; `compare-fixtures` identical. U0 also shows the
diagnostic and semantic snapshots against its parent (a restructuring commit, VERIFICATION). `PipelineSwitchTest`'s
`EveryPipelinePublishesTheMirRecords` and `MirRecordsAtOneLocationAreOneProblem` stay unchanged.

**Not exercised by that evidence (N6):** `Publish`, `Located`'s non-class branch, `KeyOf`'s node branch and `LocationKey` as a cause
key, the conditions, the fact table and the resolver; the evidence exercises only `Locate` and the class rows. U2's and U3's
`MirPublicationTest` rows cover each of them at the diagnostics layer (§8); H3b's rehearsal is their first corpus run.

## 8. Tests (lowest public layer; each fails on its parent)

- `T/MirAnalysisTest.cs` (U1a, U1b; beside `ARepeatedProblemMergesItsViews`, `AMergeIsDerivedOnlyWhenEveryReportIs`): two constructors'
  problems merge into one, two Reasons stay two with ordinals 0, 1; class rows never merge (a construction and a collection record, and a
  construction record and an analysis problem, at one (code, subject)); A4's and A5's `Unsupported_Kd` at one subject merge; members name
  their bodies and views; derived only when every member is, causes unioned; a Cause names its merged index, duplicates removed; `Add`
  of a cause naming no problem adds `InternalInvariant_Kd` instead; a cross-body disagreement is its own merged problem; every analysis
  `InternalInvariant_Kd` names `Ownership.Internal`; warm `Merged` allocates nothing (`Purpose=Allocation`).
- `T/MirPublicationTest.cs` (new; U2, U3): located vs node keys (one row per branch of `Located`); a located problem with a fact →
  `InternalInvariant_Kd`; conditions 0 and 1; a `for` expansion's siblings stay one located problem; a cause's key by `KeyOf`, node and
  located; a kind without a row (an unnamed `MirFactKind` value) → `InternalInvariant_Kd` (N1); each row's published form (roles,
  `OwnershipStorageLimit_Kd`'s arguments and `table`, an Origin relation's evidence, the containment edge, an effect fact); case
  evidence from masked views, none when members differ; a derived problem names its MIR cause and is suppressed when the cause
  publishes; synthetic Cause −1 facts (§5); `Chain<i32>`'s two records (H0a §5.2).

## 9. Units (WORKFLOW's one hour each; one review of H3-2 under H0, Q13)

| Unit | Owner, files | Content and completion | Tests | Depends | Size (H) |
| --- | --- | --- | --- | --- | --- |
| U0 bp | R5: `C/Core/Compilation.Publication.cs` (new), `Compilation.Pipeline.cs`, `Compilation.Generation.cs` | Move `MirRecords` and `ReportAnalysis`'s MIR loop into `PublishMirRecords`/`Locate` with the contract header; Generation's `ReportLocation` folded into `Locate`; DECISIONS entry (§11), ARCH row `:99`, CODEMAP. Done: Unit run (`PipelineSwitchTest`, `VerifiedSpecExampleTest`, a codegen-record class) and bp snapshots vs parent | none new | H3-1 merged | +50/−41 |
| U1a | R5's implementer, R4 consents: `C/Mir/Analysis/MirReport.Merged.cs` (new), `MirReport.cs` contract | `Merged` keyed with class; members; ordinal; pooled once per version. Done: `MirAnalysisTest` Unit run incl. Allocation | 5 | H3-1 | +40, t +55 |
| U1b | the same: `MirReport.Merged.cs`, `MirReport.cs` (`Add`'s cause rule), `MirFact.cs` contract | Merged derivedness, cause remap and dedup, `Add`'s cause rule, disagreement record, `Ownership.Internal` pin; R4-M71 amendment naming R4's consent | 6 | U1a | +50, t +75 |
| U2 | R5: `Compilation.Publication.cs`, `src/Kimi/Diagnostics/DiagnosticCollection.cs` (`LocationKey`), DIAGNOSTICS §4.3 note | `Publish`: `Located` with its invariant, `KeyOf`, condition, the resolver, the fact table's switch with Cause, Inlined and discard rows; R5-M182 superseding line (§11) | keys, invariant, conditions, resolver, discard arm, Cause −1 | U0, U1b | +70/−3, t +80 |
| U3 | R5: `Compilation.Publication.cs` | The fact table's other rows, case evidence through BU-82; PLAN G128 row (H0a §5.2), F14 rewording and the `Supplier` open issue (Auto). Done: the last Session and §7's evidence | rows, `Chain<i32>` | U2 | +80, t +90 |

U0 and U1a run in parallel (separate worktrees); U1b, U2, U3 stack. Every unit commits and pushes its branch after its checks; **none
merges to dev before H3-2's one review** (WORKFLOW's group rule, N8), after which the planning session merges them together. A unit
past the hour parks on `held/` and splits at a table row. R4 reviews the `MirReport` hunks and the Core hunks' use of `Merged`; the
independent reviewer reviews H3-2 once.

## 10. Questions and answers (2026-11-03)

| # | Answer |
| --- | --- |
| Q-R4-1 | Confirmed, corrected in §2 (A4 and A5 share `Unsupported_Kd` and `OwnershipDepthLimit_Kd` under one requirement each; they merge) |
| Q-R4-2 | Yes (`MirBorrows.Reports.cs:90-91`, `MirOrigins.cs:138-141`, `MirReport.cs:65-66`); pinned in U1b |
| Q-R4-3 | R4 agrees to (A) and to the line naming its agreement; (A) supersedes R4's suggestion of a located `PrerequisiteUnavailable_Kd` |
| Q-R4-4 | Agreed: the ordinal is the view's (U1a). R5's implementer writes U1a and U1b with R4's consent on the `MirReport` hunks |
| Q-BU-1 | Yes; `Supplier = Longer` differs inside a classified Closure: a Q13 evidence difference and an open issue (§3.3) |
| Q-BU-2 | `MirBody.Binders` (`MirBody.cs:57`); `NoBody` has none; mask views by `MirCases.Count(body)` (§3.3). No MIR-level reproducer of the four shapes (§5) |

## 11. DECISIONS text (drafts)

- U0, "R5-M… One publisher of MIR records (R6-check H3-2)": `Compilation.Publication.cs` maps one `MirReport.Merged` problem to the
  diagnostics adapter: `Located` key form, ordinal conditions, one fact table (one arm per `MirFactKind`), derived problems as
  `PrerequisiteUnavailable_Kd` with `DerivedFrom` by one resolver; `Locate` is the one location form (code generation's records too);
  until H5 Core publishes today's rows only, on every pipeline (review B1). H0a §4.3 corrected for R5-M183. Supersedes R4-M71's placement
  (with U1b's line) and PD30's single owner (H0a-11 (A)). Rejected: §4's (B), a pipeline column.
- U2, under R5-M182: "Superseding (H3-2; R5 and R4, 2026-11-03, R4 agreeing): 2g-1 left R4's chain at the exit (G396), so H3-2 does not
  wait for it. Until 2g-1 a MIR problem resting on a Binding failure carries no Cause fact and publishes as its own direct record,
  dependent under Q13 (counted by code, never blocking, never `InternalInvariant_Kd`; an analysis `InternalInvariant_Kd` stays a
  regression). H0a §4.2's null-`FailureCauses` rule applies to Cause −1 facts only, which 2g-1 produces; the probe that every Binding
  cause has a non-null `FailureCauses`, BU-2's four shapes, and no Cause fact on a located problem (§3.2's invariant makes it
  `InternalInvariant_Kd`; A4's causes at Opaque sites on `Unsupported_Kd` rows) are 2g-1's contract items (G396). From H5 until 2g-1, A3's records that
  read a Binding failure or a guessed recovery publish direct, departing from DIAGNOSTICS §4.3 'Later phases' (a dated note there, in
  H5's commit, when the contract changes).
  Supersedes: R5-M182's amendment 'H3-2 lands with a null `FailureCauses` as `InternalInvariant_Kd` after 2g-1 sites every Binding
  cause at its failed node' (the null rule stays; the order goes); H0a-8's 'fixed in R4's chain before H3-2'; H0's
  unit-table prerequisite of H3-2 'R4's exit with A6, G11, Q17, Q8-6, OQ11', of which only 'with A6' goes; H0 §5's chain 'R4's exit (A6 2g-D–2g-7, A-4) → … → H3-2'. Removal condition:
  2g-1."
- U1b, R4-M71 amendment (R4 consenting, 2026-11-03): the merge across bodies is `MirReport.Merged`'s (H0a §3.2), keyed with class; a
  Cause Value in `Merged` is a merged index.
- U1a, under R5-M182's amendment: "`MirReport.Records` (H0a, revision 2) is named `MirReport.Merged`, since H3-1's class helper on
  `MirProgram` reads by class (review N7, N-b)."

## 12. Not done

No probe: R4's 476 by analysis and the `Chain<i32>` rows come from R4's and the reviewer's counts. Role names beyond `loan` and
`reservation` are fixed in U3 from DIAGNOSTICS' A6 tables. Sizes are estimates.

## Revision 1 (review 1, 2026-11-03)

| Item | Fix |
| --- | --- |
| B1 (a) probe owner | §5, §11: the probe of every Binding cause is 2g-1's contract item beside BU-2's four shapes; the planning session adds it to G396; no MIR reproducer exists (BU), so U2/U3 use synthetic Cause −1 facts |
| B1 (b) superseded clauses | §11's U2 line names R5-M182's amendment, H0a-8, H0's unit-table prerequisite and H0 §5's chain |
| B1 (c) DIAGNOSTICS departure, N5 | §4: A3's 34 depart from §4.3 "Later phases" until 2g-1, dated note in U2; an analysis `InternalInvariant_Kd` is not covered (Q13 regression); counts by analysis |
| Q-R4-1..4 | §2 (A4/A5 merge corrected), U1b pin, §4 and §11 (R4's agreement), U1a ordinal; §9: R5's implementer writes U1a/U1b with R4's consent |
| Q-BU-1, Q-BU-2 | §3.3: `Supplier` evidence difference and open issue (U3, PLAN); `MirBody.Binders`, view masking |
| N1 | D4, §8: no build-enforced totality; a test for a kind without a row |
| N2 | §2: `Add`'s cause rule mirrors the disagreement rule; merged indices amend `MirFact`'s contract; duplicates removed after remap |
| N3 | §3.2: a located problem carries no fact, else `InternalInvariant_Kd` (tested) |
| N6 | §7: what the evidence does not exercise and which tests cover it |
| N7 | §11 restores PD30; the view is renamed `MirReport.Merged`; Generation's `ReportLocation` folded into `Locate` (U0) |
| N8 | §9: no unit merges to dev before H3-2's one review |
| Sizes | src ≈ +290/−45 (U0 −6 by the fold; U1b +10, U2 +5 for the new rules), tests ≈ +300 |
