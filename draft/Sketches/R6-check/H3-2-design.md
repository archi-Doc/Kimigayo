# R6-check H3-2: implementation design of Core's publisher of MIR records

Status: implementation design under H0 ([H0-design](H0-design.md) row H3-2; [H0a](H0a-publication.md) §2-§10; [H0b](H0b-flip.md) §2.3;
DECISIONS R5-M182 and its amendment). Working record, not authority: the DECISIONS lines of §11 become authority in the units' commits.
Base: dev `a6f8612d0`; H3-1 (R4, not landed) is designed against as GO'd on 2026-11-03 (two body values, one `MirProgram` helper; R4's
note `post-exit/h3-1-design.md` and its commit draft). Working time (Q12): 14:17-14:32 on 2026-11-03, logged in the designer's state file.
No build, test or probe was run. Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. (H) marks an unchecked inference.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| D1 | The publisher is one Core component, `C/Core/Compilation.Publication.cs`: one MIR record → one adapter call. Until H5 Core calls it for today's rows only (construction rows, findings, collection rows, in today's order); its record path has no production caller until H5 (removal condition of the selection: H5) | 1, 3.1 |
| D2 | Input: `MirReport.Records` (R4, U1a/U1b) keyed (code, subject, Reason, class), class = Construction, Collection or analysis (every body ordinal and `NoBody`); members exposed, ordinal, merged derivedness, Cause indices remapped to record indices, own disagreement record, pooled, once per version | 2 |
| D3 | Key form: one predicate `Located(record)`: a class row, or a code of category Unsupported, Resource or Internal without catalog Arguments → `ReportLocation`; every other record → `Koto.Report` at its subject, condition = the record's Reason ordinal | 3.2 |
| D4 | One fact table: one switch arm per `MirFactKind` (build-enforced totality); its discard arm and `Inlined` are `InternalInvariant_Kd` at the subject | 3.3 |
| D5 | One cause resolver: Value ≥ 0 → the cause record's key by the same key function; Value −1 → `Binding.FailureCauses(site)`, null → `InternalInvariant_Kd`; a cause naming no problem is rejected by `Add` when written | 2, 3.4 |
| D6 | **Records resting on a Binding failure 2g-1 has not sited publish as their own direct records** (option A): no code, no identification; dependent under Q13, counted by code, never blocking, never `InternalInvariant_Kd`; 2g-1 (G396) adds their Cause −1 facts and the null rule then covers them | 4 |
| D7 | BU-2's probe: the four shapes and synthetic Cause −1 facts are `MirPublicationTest` rows; the corpus half has no subject before 2g-1 and is H3b's census on the flip branch; the Binding-cause half is 2g-1's contract test (R4 r2 N1) | 5 |
| D8 | Five units, R5: U0 (bp restructuring), U2, U3; R4: U1a, U1b; ≈ src +270/−40, tests +280 | 9 |

## 1. The chain

- **Meaning.** SPEC 23.1.2 (every independent failure reported; a dependent one derived from its prerequisites) and 23.2.2 (check
  reports every error of the checked set); DIAGNOSTICS rule 1 (one requirement, one phase), rule 2 (no free text), rule 3 (Unsupported
  is one location), §4.3 (`PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is non-empty; an unknown cause is the unresolved mark;
  only Errors are derived); Q13 (acceptance exact, record selection best effort; three regressions); R4-M132 (Cause facts → derived).
- **Representation.** Read: `MirReport.All`/`Facts(i)`, H3-1's class values and `MirProgram.Records(class)`, `MirProgram.Findings`, and
  U1's `MirReport.Records`. Written: adapter calls only (`Koto.Report`, `Koto.ReportDerived`, `DiagnosticCollection.ReportLocation`).
  The publisher keeps no table of its own: keys come from the record, facts from the fact table, causes from the resolver.
- **Owner.** Core's publisher (R5; H0a §7). It decides only how a record is published, never whether a problem exists, whether it is
  dependent (a Cause fact or nothing; §4) or which requirement it names (the recording rule's, R5-M23).
- **What becomes unnecessary.** U0 moves `MirRecords` and the MIR half of `ReportAnalysis` out of `Compilation.Pipeline.cs`. The
  selection of today's rows stays until H5 (its removal condition): H5 publishes every record and `Analysis`/`Problems` read the
  publisher's rows (H0b §2.1); H3-1's helper then has no Core reader. `MirReport`'s contract loses "(R4-M11's merge … is publication's)".

## 2. Input contract with `MirReport` after H3-1 (answer R4-1)

From H3-1 (as GO'd): `MirProblem.Construction` and `MirProblem.Collection` (no body, distinct from `NoBody`); `MirProgram.Report(site,
code, class)` the one writer; `MirProgram.Records(class)` the one non-allocating path to a class's (code, subject) rows; `MirProblem.IsRecord`;
`MirReport` reset in `MirProgram.Clear`. Class rows: requirement `ControlFlow`, every view, default Reason, no facts.

U1's `MirReport.Records` (R4; new partial `C/Mir/Analysis/MirReport.Records.cs`), computed on its first read after an `Add` or `Clear`,
pooled (a warm version allocates nothing):

- One record per (code, subject, Reason, class), in first-member order. Class keeps the two record classes apart from each other and
  from the analyses' problems, so H3-1's F2 stays fixed (a construction and a collection record at one (code, subject) stay two) and a
  class row never meets an `Ownership.*` requirement (R4 review B1's recursion). Across body ordinals and `NoBody` it merges (R4-M71's r05).
- Each record exposes: requirement, code, subject, Reason, its members (index range into a pooled member list; each member's index in
  `All`, hence its body and views), its merged facts (the first member's non-Cause facts, then the Cause tail merged by `Merge`'s rule:
  direct wins, causes unioned), and `Ordinal`, its Reason's position among the records of its (code, subject, class) in record order.
- Each Cause fact's Value is remapped from the cause's index in `All` to its record's index in `Records` (total: every problem is a
  member of one record); Value −1 (2g-1's Binding cause) is kept. R5's condition (a cause naming no problem is a broken contract, never a
  silent drop) is checked where the fact is written: `Add` records `InternalInvariant_Kd` (`Ownership.Internal`) at the subject for a
  Cause whose Value is neither −1 nor an index of `All` (rule W's `CauseFacts` writes only `All` indices, so no producer reaches it).
- Members naming two requirements give one more record of their own: `InternalInvariant_Kd`, `Ownership.Internal`, at the subject (a read
  cannot call `Add`), merged like any analysis record (§10 Q-R4-2).

## 3. The publisher (R5)

### 3.1 Entry and selection

`Compilation.Publication.cs` (partial of `Compilation`; contract header: responsibility, inputs, outputs, invariants, prohibitions as
§1). Members: `PublishMirRecords()` (Core's call from `ReportAnalysis`: today's rows: `Records(Construction)`, findings,
`Records(Collection)`, each through `Locate`); `Publish(MirProgram, int record)` (one `Records` entry; tests call it; H5 makes
`PublishMirRecords` loop over every record and deletes the class loop); `Locate(requirement, code, subject)` (the location form, shared
by findings and located records). A finding publishes as `InternalInvariant_Kd`, `ControlFlow`, located (today's row; H0a §2). No
pipeline mode, flag or optional parameter steers it (AGENTS symptom list).

### 3.2 Keys and conditions (H0a-6)

`Located(record)` = class Construction or Collection, or the code's catalog entry has category Unsupported, Resource or Internal and no
Arguments. It reproduces H0a §3.4's lists: today's row codes (checked: `DefaultExpansionLimit_Kd`, `GenerationResourceLimit_Kd`,
`InternalInvariant_Kd`, `OversizedLayout_Kd`, `InvalidCLayout_Kd`, `Unsupported_Kd`) and `OwnershipDepthLimit_Kd` are located;
`OwnershipStorageLimit_Kd` (Arguments) and every Language and Proof code of A1-A5 key at their subject. A non-located record calls
`Koto.Report(requirement, code, …, condition: (ushort)Ordinal)`; two relations at one subject are conditions 0 and 1. One key function
`KeyOf(record)` serves emission and the resolver: located → `DiagnosticCollection.LocationKey(requirement, range, document)` (extracted
from `ReportLocation`, which then calls it; U2), else `Koto.KeyOf(requirement, condition)`.

### 3.3 The fact table (H0a-7)

| Kind | Published as |
| --- | --- |
| Anchor, Use, Store, Reservation, Evaluated | related locations, roles and wording from DIAGNOSTICS' A6 fact tables (`loan`, `reservation` as the old analysis names them; U3 fixes the rest there) |
| EffectRead, EffectWrite, EffectUnknown | related location: the Field's declaration, DIAGNOSTICS' effect-fact wording |
| Relation (Value ≥ 0; −1 the containment edge) | evidence and related through `Binding.OriginRelationFacts` (the shared renderer) from an `OriginRelationFact` built of the `MirReason` and the row `TyCtxt.OriginRows(body)[Value]` (source fit/declared/wellFormed, CallBorrows → fit, destination for a fit); −1: no row, `wellFormed`, no destination |
| Required, Limit, Table | `OwnershipStorageLimit_Kd`'s `requiredBytes`, `limitBytes` and `table` evidence |
| Cause | the resolver (§3.4) |
| Inlined; the discard arm | `InternalInvariant_Kd` at the subject (`Add` drops Inlined; reaching here is a broken contract) |

Case evidence, the case Note and binder declarations come from the members' views through BU-82's renderer (`Binding.Diagnostics.Cases`),
only when every member's views give the same case text, else none (Q13 best effort); a `NoBody` member gives none. Lost against the
old analysis, as H0a §4.1: the Borrow source, `FixedBy`, `Substituted`, the hand-built Notes (class 3 at H3b).

### 3.4 The cause resolver (H0a-8)

A record with Cause facts publishes as `Koto.ReportDerived(requirement, causes, condition)`; causes: Value ≥ 0 → `KeyOf(Records[v])`;
Value −1 → `Binding.FailureCauses(SyntaxOf(site))`, null → the record publishes `InternalInvariant_Kd` at its subject (H0a §4.2, R4 r2
N1). The owner's finalization suppresses a derived record whose causes all reach a published direct Error
(DIAGNOSTICS §4.3). Before 2g-1 only rule W's MIR causes (Value ≥ 0, `MirBorrows.Causes.cs`) reach the resolver.

## 4. Records resting on a Binding failure 2g-1 has not sited (the brief's question)

Facts: no `MirFact(Cause, Site, −1)` exists before 2g-1 (`MirFact.cs`: −1 reserved); R4 counted 476 MIR-side records in 356
Binding-rejected compilations at `1eb0cbaa9`, 0 on valid programs. Such a record carries no fact that says it is dependent.

| Constraint | (A) direct MIR record | (B) located `PrerequisiteUnavailable_Kd` without `DerivedFrom` |
| --- | --- | --- |
| Identification | none: it is a record without a Cause fact | needs a rule: Binding incomplete anywhere (a global gate), or `HasFailureCauses(subject)` (2g-1's siting copied into the consumer; misses the Opaque sites that are not the subject, PD17) |
| AGENTS: no analysis deciding dependence beyond direct inputs | holds (nothing suppressed; "otherwise report it") | broken by either rule; the gate also turns independent MIR records of a rejected program into prerequisite records (a lost independent record, Q13) |
| DIAGNOSTICS §4.3 | holds: a direct record has empty `DerivedFrom` | "without DerivedFrom" violates the invariant; it must carry the unresolved mark, which `DiagnosticOwner.Explain` never satisfies: 476 visible records of rule 8's fallback form, which tests treat as defects (H) |
| H0a §5 one producer per failure | holds: the MIR record judges another requirement than Binding's failure | same |
| Null rule (H0a §4.2) | untouched: it applies to Cause −1 only | a temporary branch with removal condition 2g-1 |
| Cost | 0 lines | a gate or a per-record Binding read, tests, a later removal |

**Chosen: (A).** The dependence is a fact its owner (A6's 2g-1, R4) publishes; the publisher must not decide it (AGENTS: a consumer
lacking a fact has the owning component publish it). Published from H5: (A) the 476 records publish with their MIR codes and facts
beside Binding's Errors on the 356 rejected programs, counted by code at H3b (Q13: added on a program the base already rejects), no
acceptance move; at 2g-1 each gains its Cause −1 fact, becomes `PrerequisiteUnavailable_Kd` and is suppressed by its Binding Error's
record (counted in 2g-1's commit). (B) would publish the same records as unexplained `PrerequisiteUnavailable_Kd` from H5 and move them
again at 2g-1: two snapshot moves and a generic message where (A) shows the MIR's evidence.

Superseding line under R5-M182 (U2's commit, naming R4's agreement, §11).

## 5. BU-2's probe (H0a §4.2, §8)

- Binding-cause half (every Cause −1 site has non-null `FailureCauses`): no subject before 2g-1; it is 2g-1's contract test with the
  four shapes, which R4 already took into its exit list (R4 r2 N1), now G396.
- H3-2 checks, in `T/MirPublicationTest.cs`: the resolver with synthetic Cause −1 facts (`MirReport.Add` in the test: one at a node
  Binding failed → `DerivedFrom` = its `FailureCauses`, suppressed; one at a resolved node → `InternalInvariant_Kd`); BU-2's four shapes
  (a rejected assignment target, G353's Field selection, a resolved recovery node, an unreadable char literal; BU-2's reproducers):
  Binding-rejected, every MIR record published once as a direct record, none `InternalInvariant_Kd` (2g-1 moves them, citing Q13).
- Corpus half: H3b's census runs every record through the publisher on `claude/r6-flip` with H5 applied; a publisher
  `InternalInvariant_Kd` there is a Q13 regression and blocks H5. No extra heavy run in H3-2.

## 6. H0a §4.3's fallback against G157 (1) (R5-M183, U2 landed `7ec8030fd`)

The paragraph's "validator findings stop the analyses" is outdated: a body with a finding is excluded from the analyses and the
collector, every other body is judged, and a reader of the excluded body records nothing or composes it as Derived. The MIR phase still
never ends incomplete without a record: the excluded body's record is the validator's own `InternalInvariant_Kd`, published today and by
`PublishMirRecords`. Conclusion unchanged: no fallback producer (H0a-9). H3-2's DECISIONS entry states the corrected sentence.

## 7. No published change at H3 (bp)

U0 moves code; U1-U3 add members without a production caller. Evidence on the last unit's head against H3-1's merge (same worktree):
default Session with `-DiagnosticSnapshot` (0 differences); `SemanticSnapshotTest` with `KIMI_SEMANTIC_MIR=1` unchanged; MIR goldens
unchanged; the mir ledger run (registered heavy run) equal to the base's mir-diff; `compare-fixtures` identical. U0 also shows the
diagnostic and semantic snapshots against its parent (a restructuring commit, VERIFICATION). `PipelineSwitchTest`'s
`EveryPipelinePublishesTheMirRecords` and `MirRecordsAtOneLocationAreOneProblem` stay unchanged.

## 8. Tests (lowest public layer; each fails on its parent)

- `T/MirAnalysisTest.cs` (U1a, U1b; beside `ARepeatedProblemMergesItsViews`, `AMergeIsDerivedOnlyWhenEveryReportIs`): two constructors'
  problems are one record, two Reasons two records with ordinals 0, 1; class rows never merge (a construction and a collection record,
  and a construction record and an analysis problem, at one (code, subject)); members name their bodies and views; derived only when
  every member is, causes unioned; a Cause names its record after remapping; `Add` of a cause naming no problem records
  `InternalInvariant_Kd`; a cross-body disagreement is its own record; warm `Records` allocates nothing (`Purpose=Allocation`).
- `T/MirPublicationTest.cs` (new; U2, U3): located vs node keys (one row per class of `Located`); conditions 0 and 1 at one subject; a
  `for` expansion's siblings stay one located problem; each fact-table row's published form (roles, `OwnershipStorageLimit_Kd`'s
  arguments and `table`, an Origin relation's evidence, the containment edge, an effect fact); case evidence from views and none when
  members differ; a derived record names its MIR cause and is suppressed when the cause publishes; Cause −1 and the four shapes (§5);
  `Chain<i32>`'s two records (H0a §5.2).

## 9. Units (WORKFLOW's one hour each; one review of H3-2 under H0, Q13)

| Unit | Owner, files | Content and completion | Tests | Depends | Size (H) |
| --- | --- | --- | --- | --- | --- |
| U0 bp | R5: `C/Core/Compilation.Publication.cs` (new), `Compilation.Pipeline.cs` | Move `MirRecords` and `ReportAnalysis`'s MIR loop into `PublishMirRecords`/`Locate` with the contract header; DECISIONS publisher entry (§11), ARCH row `:99`, CODEMAP. Done: Unit run (`PipelineSwitchTest`, `VerifiedSpecExampleTest`) and bp snapshots vs parent | none new | H3-1 merged | +50/−35 |
| U1a | R4: `C/Mir/Analysis/MirReport.Records.cs` (new), `MirReport.cs` contract | Records keyed with class; members; ordinal; pooled once per version. Done: `MirAnalysisTest` Unit run incl. Allocation | 4 | H3-1 | +40, t +50 |
| U1b | R4: same, `MirReport.cs` (`Add`'s cause check) | Merged derivedness, Cause remap, `Add`'s cause check, disagreement record; R4-M71 amendment line | 4 | U1a | +40, t +60 |
| U2 | R5: `Compilation.Publication.cs`, `src/Kimi/Diagnostics/DiagnosticCollection.cs` (`LocationKey`) | `Publish(record)`: `Located`, `KeyOf`, condition, the resolver, the fact table's switch with Cause, Inlined and discard rows; R5-M182 superseding line (§4) | keys, conditions, resolver, Cause −1 | U0, U1b | +65/−3, t +70 |
| U3 | R5: `Compilation.Publication.cs` | The fact table's other rows, case evidence through BU-82; PLAN G128 row (H0a §5.2) and F14 rewording (Auto). Done: the last Session and §7's evidence | rows, four shapes, `Chain<i32>` | U2 | +80, t +100 |

U0 and U1a run in parallel (separate worktrees); U1b, U2, U3 stack. If a unit passes the hour it parks on `held/` and splits at a
table row. R4 reviews the Core hunks' use of `Records`, R5 the `Records` contract (H0a §7); the independent reviewer reviews H3-2 once.

## 10. Open questions

| # | To | Question |
| --- | --- | --- |
| Q-R4-1 | R4 | Confirm the class key: `NoBody` merges with body problems (A4's codes and A5's never meet; C1-F4's and `Add`'s `InternalInvariant_Kd` both name `Ownership.Internal`) |
| Q-R4-2 | R4 | Does every analysis `InternalInvariant_Kd` name `Ownership.Internal`, so the view's disagreement record cannot disagree again? |
| Q-R4-3 | R4 | Agree to (A) and the superseding line's text (§11) naming your agreement |
| Q-R4-4 | R4 | The ordinal in the view (U1a) rather than counted in Core: agreed? |
| Q-BU-1 | BU | `OriginRelationFacts` from an `OriginRelationFact` with `Supplier = Longer`, no `FixedBy`/`Substituted`: does it render without Binding's per-pass state? |
| Q-BU-2 | BU | Which member gives BU-82's renderer a MIR body's definition binders (the old Analysis' input), and BU-2's four reproducers' location |

## 11. DECISIONS text (drafts)

- U0, "R5-M… One publisher of MIR records (R6-check H3-2)": `Compilation.Publication.cs` maps one `MirReport.Records` record to the
  diagnostics adapter: `Located` key form, Reason-ordinal conditions, one fact table (one arm per `MirFactKind`), derived records as
  `PrerequisiteUnavailable_Kd` with `DerivedFrom` by one resolver; until H5 Core publishes today's rows only, on every pipeline (review
  B1). H0a §4.3 corrected for R5-M183. Supersedes R4-M71's placement (with R4's U1b line). Rejected: §4's (B), a pipeline column.
- U2, under R5-M182: "Superseding (H3-2; R5 and R4, 2026-11-03): until 2g-1 (G396) a MIR record resting on a Binding failure carries
  no Cause fact and publishes as its own direct record, dependent under Q13 (counted by code, never blocking, never
  `InternalInvariant_Kd`); H0a §4.2's null-`FailureCauses` rule applies to Cause −1 facts only, which 2g-1 produces; BU-2's four shapes
  enter 2g-1's contract test. Removal condition: 2g-1."
- U1b (R4), R4-M71 amendment: the merge across bodies is `MirReport.Records`' (H0a §3.2), keyed with class.

## 12. Not done

No probe: the four shapes' MIR records, R4's 476 by code and the `Chain<i32>` rows are taken from R4's and BU's answers. The role names
beyond `loan` and `reservation` are fixed in U3 from DIAGNOSTICS' A6 tables. Sizes are estimates.
