# Deep dive Q5: R6-check prerequisites that can land early, and their owners

Base: documents from `origin/dev` = `2aac946b` (`git show origin/dev:<path>`), code from `origin/dev` unless a line says
`5c42b577` or a branch (`claude/a5-1b-mir`, `claude/a5-1b-core`). Read-only: no build, test or native run. "(H)" marks an
inference I did not verify. ARCH = `docs/dev/COMPILER_ARCHITECTURE.md`, DEC = `docs/dev/DECISIONS.md`, PLAN10 = the
2026-10-10 reduction plan, A5P = the 2026-10-16 Complete Polonius A5 Plan, CFA = `Analysis/ControlFlowAnalysis.cs`.
Paths are under `src/Kimi/Compiler` unless they start with `src/`, `tests/`, `docs/` or `draft/`.

## 0. Answer in brief

| # | Prerequisite | Owner (decided where) | Can land before R6-check? | Size | Weight |
| --- | --- | --- | --- | --- | --- |
| 1 | HIR lint for CFA's 13 lint codes | Binding P2–P5s (ARCH:15 "HIR lints and startup checks"; PLAN10 §5.1 P5) | Yes for all 13 (warnings without any owner decision; errors after one owner decision on requirement kinds and node failure) | CFA −≈480, Binding +≈420–500 | full process, then a Q6 group |
| 1b | `FunctionFallthrough_Kd` | **Binding's result validation**, not A3 (SPEC §14.9.3: Structural Completion) | Yes (with the lint errors) | ±25 | in the lint group |
| 1c | `RequireFallthrough_Kd` | A3 (`MirLints`, R4-M7a) — already on MIR | nothing to move; old twin goes with Analysis | — | — |
| 2 | `Ownership.*` requirement vocabulary | Diagnostics (`DiagnosticRequirement.tinyhand` already holds every name; only the kind enum is in old Analysis) | Yes, names and numbers unchanged | ≈ ±30 + ≈130 mechanical renames | full process (shared representation; supersedes part of R4-M3a) |
| 3 | `TransferRequired` in Binding/HIR | Binding (`Binding.BareAcquire` is "the one bare-acquisition rule", ARCH:92) | Yes, but it changes old-path diagnostics in rejected programs (owner trade-off); **hard soundness prerequisite of the switch** | Binding +≈120, Analysis −≈70 | full process |
| 4 | `StructuralCompletion`, `MatchCoverage` into Binding | Binding (R0 disposition; PLAN:33; `Binding.Hir.cs:65`) | Yes, pure move | net ≈ +6 | one review |
| 5 | 36 forwarding properties, Binding's ≈1,268 accesses | Binding | Mechanically yes, practically no (collides with R4's remaining Binding fact units; no measured gain); R2a recovery is **not** real | ≈1,000 lines rewritten, −≈120 net | owner decision R6 vs R7 |
| 6 | Synthetic Koto deletion | Binding (CallPlan pipeline) | No; needs syntax-free call binding (R7's CallPlan pipeline). `ExpansionPlan.Evaluated`'s direct source can land early | +12/−10 early | owner decision R6 vs R7 |
| 7 | Mir/Build syntax-class decisions (80 regex sites) | Mir/Build reading Binding facts | 17 sites yes (existing facts); 29 need an owner decision (operator/form identity); 34 are casts after Op dispatch (ARCH interpretation) | +30/−35 early | one review; hold |
| 8 | Old effect checks deletion | A4 (`confined`) + A5 F6 (`preserves results`, §8.11.3–8.11.6) | No: deleting before MIR acceptance is unsound acceptance on the default path; needs F6 (S2) | −≈2,650/+≈60 | full process |
| 9a | Binding's i128 `/` `%` gate | A3b (R4-M8, R5) | No: removal follows R5's A3b and the switch | −6 | one review |
| 9b | `Binding.ClosureInstances`, `RequirementInstances` | Binding selection until R7; R5 reads `TyCtxt.Witnesses` and a Subst-keyed view | Not R6 items: re-disposition to R5 view + R7 | — | DEC note |

Three findings the surveys did not state:

1. **MIR alone accepts a bare move of a non-Copy local.** For `let b = s` with `s` non-Copy, the HIR publishes the Move
   `BareAcquire` decides (`Binding/Binding.Hir.Adjust.cs:186-198`), the builder moves, and A1 reports only a Move without a
   static Move Path (`Mir/Analysis/MirInit.cs:150-175`); no MIR producer of `TransferRequired_Kd` exists (`git grep` over
   `Mir`). The old path rejects it at `Analysis/OwnershipAnalysis.cs:836`. The difference ledger cannot show this gap:
   TransferRequired is passed through uncompared (R4-M4, R4-M17's compared set), so neither a `missing` nor an `acceptance`
   row appears (the tracked rows `a1-g4`/`a1-g4-acceptance` cover only the opposite direction,
   `tests/xUnitTest/TestData/mir-diff/classification.tsv`). R6-check must not flip acceptance before item 3 lands.
2. **`BindingFailure` is at its closed-vocabulary limit** (88 members ending in a comma plus `Internal` = 89 of 89, ARCH:56;
   `Binding/BindingModel.cs`). Every design that gives the lint's errors their own Binding requirement kind needs owner
   approval, or must reuse an existing kind.
3. **The lint's transfer-target checks are the owner G103 E9b needs.** G103 E9b (MIR, before the R4 exit) wants MIR
   records at "`exit`, `continue`, `yield` and `return` with an unresolved target" to become derived Opaque values. The MIR
   builder may derive them only if a phase it may trust guarantees that a Jump without a target is always reported; today
   that phase is CFA (old path, deleted at R6-check). Moving `InvalidJumpTarget`/`UnlabeledYieldTarget` into Binding gives
   that guarantee an owner the builder may read (unit L3 below).

## 1. HIR lint for CFA's lint, label, unsafe and discard codes

### 1.1 The codes, where each is decided, and what it reads

CFA reports 14 codes (`git grep -ohE 'DiagnosticCode\.\w+_Kd' Analysis/ControlFlowAnalysis.cs`). `RequireFallthrough_Kd`
is already A3's on MIR (`Mir/Analysis/MirLints.cs`, R4-M7a), so the lint set is 13. Producers outside CFA: only
`UnsafeFunctionValue` (Binding: `Binding.Expressions.cs:881`, `Binding.FunctionItems.cs:394`, `Binding.FunctionTypes.cs:518`,
`Binding.RequirementItems.cs:86`) and `FunctionFallthrough` (a derived link at `OwnershipAnalysis.cs:263`).

| Code | Severity | Decided at (CFA line) | Reads | Repair |
| --- | --- | --- | --- | --- |
| `InvalidJumpTarget_Kd` | Error | `VisitJump` :1285 | `KotoHelper.ResolveTransferTarget` (syntax) | — |
| `UnlabeledYieldTarget_Kd` | Error | `VisitJump` :1290 | target kind, `IsValueContext` (syntax) | — |
| `OverlappingLabel_Kd` | Error | `CheckLabel` :1522 | ancestor labels (syntax) | — |
| `UnsafeBlockRequired_Kd` | Error | `CheckUnsafePermission` :1089 | `RequiresUnsafeContext` :305 (resolution state, pointer Type of a `BinaryKoto` left, selected unsafe function), `IsUnsafeContext` | — |
| `UnsafeFunctionValue_Kd` | Error | `Visit` :675 | `GetReferencedFunction` (selected symbol, non-call position) — duplicate of Binding's 4 sites (H: a reproducer must show whether :675 still fires) | — |
| `FunctionFallthrough_Kd` | Error | `VisitFunction` :1250 | `StructuralCompletion.CanComplete(body)`, declared result (`DeclaredResult` :285), pending completion | — |
| `UnnecessaryUnsafeBlock_Kd` | Warning | `WarnUnnecessaryUnsafe` :1100-1124 | whether an operation in the block used or may use the permission (`unsafeUsed`, `unsafeUncertain`) | `Repair.RemoveUnsafe` :1099 (`RemoveUnsafeEdits` :190-239) |
| `DiscardedResult_Kd` | Warning | `Visit` :646 | Discard Context, Type is `Kimi.Result` | `ExplicitDiscard`, `PropagateFailure` (`WarnDiscard` :429-448, `FailureTargetFits` :250) |
| `UnusedTrySuccess_Kd` | Warning | `Visit` :650 | `TryKoto` with non-Unit, non-Never supplied Type | `ExplicitDiscard` |
| `DiscardedValue_Kd` | Warning | `Visit` :654 | `IsEffectFree` :469-509 (literals, locals, comparisons; `ProveCopy` for constructions) | — |
| `DiscardedTail_Kd` | Warning | `WarnUnitTail` :526-576 (from :392, :574) | Structural Completion of a Unit body, supplied Type of its tail | — |
| `StaticWhileTrue_Kd` | Warning | loop visit :1451 | `while true` syntax | — |
| `OwningWriteArgument_Kd` | Warning | call visit :901 | selected call target `WriterWrite`, interpolated literal argument | — |

All of these read only syntax, Binding's inline HirTables columns (`BindingState`, `BoundType`, `BoundSymbol`, which are
written during binding, `Binding/HirTables.cs:60-72`) and Binding queries (`TryGetAdaptation`, `ProveCopy`, `FailureCauses`,
`PropertyCall`, `RangeValueCall`, `ResolvedKeyCall`), plus parser recovery (`CodeContext.RecoveryCause`, `GuessedBy`
:379-424) and directive selection (`HasInvalidDirective`). **None needs the lazily published HIR columns** (`Op`, `Adjust`,
`Completion`), so a Binding P5 lint does not force the publisher (R2b-D2) on the legacy pipeline.

### 1.2 FunctionFallthrough belongs to Binding, not A3

SPEC §14.9.3 (origin/dev `docs/spec/14-control-flow.md`, "Reachability"): Structural Completion "is conservative: no path is
pruned by `bool` literals ...", and its example `if true => return 1  // Error: the false path supplies Unit.` is a result
source check (§14.9.1 "Indented body: Unit on structural end arrival"; §14.9.2 step 4 "Check every source"). Structural
Completion is Binding's (the HIR `Completion` column is "the structural completion Binding's Never rule applies",
`Binding/HirVocabulary.cs`; `Binding.ControlFlow.AddBodyResult` already adds the end-arrival Unit source for inferred results,
`Binding/Binding.ControlFlow.cs:517-529`). Runtime Reachability (A3) differs from it where a cleanup blocks delivery
(`defer => loop => ()`), so an A3 version would accept a body SPEC rejects. PLAN10 §5.5's A3 row ("replaces ... CheckCompletion")
predates the S1015 wording; the planning record should say Binding. MIR records nothing at such a body end (ledger row
`a1-result`: "the MIR records nothing there (R4-M21: a Return checks nothing)"), so a Binding record needs no MIR change.

### 1.3 Does A3 cover any?

Only `RequireFallthrough_Kd` (A3 `MirLints`, runtime edges, SPEC §14.9.3 "checks that `require` failure bodies do not
continue"). The old structural `RequireFallthrough` (CFA :1318) and A3's disagree by design (ledger rows `b1`, `b3`, G105)
and the old one is deleted with Analysis. No other lint code has a MIR producer.

### 1.4 What a Binding P5 lint looks like

- **Component.** A new Binding file pair (`Binding/Binding.Lints.cs` and, by responsibility, `Binding.Lints.Unsafe.cs`),
  entry contract first (AGENTS). `Binding.cs` (1,917 lines) is over the 5,000-token limit and may not grow (ARCH structure
  limits), so only the call in `ReportDiagnostics` moves there. One walk over each source module's bodies at Final, after
  `Binding.ReportDiagnostics`' existing Final block (`Binding/Binding.cs:144-170`), reusing `ResultStructure()`
  (`Binding.ControlFlow.cs:533`) for Structural Completion.
- **The evaluated-children walk.** CFA visits Binding's synthesized calls in place of syntax (getter, setter, range value,
  view range, key resolution; CFA :693-719). The lint needs the same enumeration for unsafe uses and discards. That
  enumeration is also what the HIR publisher (`Binding.Hir.Adjust.cs`, ≈30 `Take` sites) and TransferRequired (item 3) need;
  see unit T1a, which makes it one Binding function.
- **Derived records.** The same rule as today (DIAGNOSTICS §4.3 "Later phases"): an Error at a node Binding failed is
  derived from `FailureCauses`, a warning there is dropped, a completion check resting on supplied bodies is derived from
  their syntax Errors. `GuessedBy`/`SuppliedCompletion` move into Binding as one query, which CFA's remaining
  `RequireFallthrough` calls until R6-check (no second copy).
- **Old-path rewiring (mechanical, D5-allowed follow-up).** `OwnershipAnalysis.Analyze` reads `flow.Issues` for
  `earlierRejected` (`OwnershipAnalysis.cs:101`, used by `OwnershipBody.CheckUnreachable`) and `verified` (:116);
  `OwnershipAnalysis.cs:263` derives a missing-result record from `flow.Reported(body, FunctionFallthrough_Kd)`. Each must
  read the lint's errors instead, or old-path diagnostics in rejected programs change (new unreachable-use Unsupported).
  Acceptance is unaffected either way: `Project.CheckFrontEnd` rejects on `HasErrorsThrough(DiagnosticPartition.Ownership)`,
  which includes the Binding partition (`src/Kimi/SolutionAndProject/Project.cs:510`); no other gate distinguishes Binding
  from ControlFlow errors (`git grep HasErrorsThrough|HasErrorsIn`: Binding.cs:165, OwnershipAnalysis.cs:288,
  LlvmEmitter.cs:540, Project.cs:510, :588).

### 1.5 Requirement identity and the partition move

- Today every CFA record uses `DiagnosticRequirement.ControlFlow` (CFA :156-172), partition ControlFlow, which
  `OwnershipAnalysis.Analyze` invalidates at its start (`OwnershipAnalysis.cs:68`). A Binding lint cannot keep writing into
  that partition: Binding reports before `Analyze` (Project.cs:502-510), so its facts would be wiped, and the partition
  belongs to P6–P7 (PLAN10 §7; after R6-check it holds the MIR construction records, A3 and A3b).
- A problem key is (subject, requirement, condition) without the code (`Diagnostics/DiagnosticFact.cs:26`), so two lint
  Errors at one node under one requirement need distinct conditions (DIAGNOSTICS §4.3 "Several problems of one node").
- **Options (owner decision):** (a) one new `BindingFailure` member, e.g. `ControlFlow`, named `Binding.ControlFlow`, with a
  fixed condition per code — 89 → 90, closed-vocabulary growth (ARCH:45-48); (b) the existing `Binding` requirement
  (`BindingFailure.None`, which Binding's alias/pattern/position warnings already use, Binding.cs:147-160) with a fixed
  condition per code — no growth, but the requirement name stops telling these checks apart. Warnings can use (b) in any case.
- **Node failure or not (owner decision for the 5 errors).** CFA's errors never fail a node, so the old ownership analysis
  still runs on such programs. If the lint's errors fail nodes, `Binding.Result.IsComplete` turns false and
  `OwnershipAnalysis.Analyze` returns without analyzing (`OwnershipAnalysis.cs:89-92`): every other old-path ownership
  diagnostic of that program disappears until R6-check (precedent: R4-M18 moved `CaseLimit` into Binding this way, "Diagnostic
  sets change only in already-rejected programs"). Recommended: non-failing records (today's semantics, no loss), with the
  Jump-target contract of unit L3 for MIR.
- **Snapshot effects.** SPEC §23.3.6.6, which the task cites, no longer exists after the S1015 rewrite: SPEC §23.1.2 makes
  grouping implementation-defined, and DIAGNOSTICS orders by subject, code name, requirement, condition and context. So the
  move changes order only among records of one subject and code. What changes: the requirement name of these 13 codes'
  records (`ControlFlow` → `Binding[.X]`), the semantic snapshot's `? flow <code>` lines into `? binding <code> <kind>`
  (`tests/xUnitTest/Tests/SemanticSnapshotTest.cs:253-260`), possibly the `ownership <verified>` field of programs whose only
  rejection was a lint Error (CFA issues feed `verified`), and prerequisite keys that name the ControlFlow requirement
  (OwnershipAnalysis.cs:263). Each difference is classified in the commit; no acceptance changes.

### 1.6 Size, tests, dependencies

- Lint logic leaving CFA ≈ 480 lines (ranges above: 187-247, 249-272, 296-317, 352-360, 375-424, 426-576, 641-688, 1084-1124,
  the unsafe-block visit at :849, jump/label/while/write checks, `MultiLineLiteralFinder` :1534-1558). Binding gains ≈ 420–500.
  CFA keeps ≈ 1,070 lines (flow, targets, `PendingBinding`, structural `RequireFallthrough`) for `OwnershipAnalysis` and
  `BodyLowering` (`BodyLowering.cs:55`, `LlvmEmitter.cs:230,436`, `LlvmEmitter.Defaults.cs:31`) until R6-gen/R6-check.
- Tests reading `AnalyzeControlFlow().Issues/Warnings` for lint codes migrate to diagnostics: ≈ 64 calls in 16 files
  (ratchet "Test calls of the old control-flow entries (R6)" = 64, `tests/xUnitTest/Tests/ArchitectureRulesTest.cs`);
  code references in tests are few (`UnnecessaryUnsafeBlock` 18, `FunctionFallthrough` 20, `RequireFallthrough` 24 refs,
  the others ≤ 7; `git grep` over `tests`).
- Compile time: during the overlap the lint walk is a second walk beside CFA's (P3 allows a recorded compiler-time
  regression; measure warm/cold Bind and Analyze).
- Dependencies: none on R5; none on A5 families. L3 interacts with G103 E9b (R4): land L3 before E9b's jump part, or E9b's
  derived rule trusts the old CFA until R6-check. Touches no shared MIR file.
- D5: the old path loses checks to Binding with identical codes at identical nodes; acceptance unchanged. The planning
  session records it as an R6-check item landed early (ARCH R6 row).

## 2. The `Ownership.*` requirement vocabulary

- **Today.** The public names already live in a Diagnostics-owned table: `src/Kimi/Diagnostics/DiagnosticRequirement.tinyhand`
  (682 lines; entries `Ownership.UninitializedUse` … `Ownership.StorageLimit` at :590-642), loaded and validated by
  `DiagnosticRequirements.Load` (`DiagnosticRequirement.cs:150-205`; PLAN10 §9's last row did this in R1). Only the **kind
  enum** is old Analysis: `DiagnosticRequirement.Ownership(OwnershipFailure)` (:42), `All` (:95), `NameOf` (:117) cast to
  `OwnershipFailure` (`Analysis/OwnershipModel.cs:139-176`, 18 values). Users: Analysis 78 references; Diagnostics 3; tests
  ≈ 52 in 17 files (`MirDifferenceLedger.cs` 21, `PipelineSwitchTest` 7, `SemanticSnapshotTest.cs:259`).
- **MIR today.** `MirProblem(Code, Subject, Views)` with no requirement (R4-M3a; same on `claude/a5-1b-core`,
  `Mir/Analysis/MirReport.cs`). The only MIR records published, construction records and validator findings, are reported
  under `DiagnosticRequirement.ControlFlow` (`Core/Compilation.Pipeline.cs:98-109`). The ledger fills its requirement
  column from the old side only (R4-M17).
- **Target.** R6 unit a "derives the published requirement from the code and the facts" (R4-M3a). That mapping
  (code → kind; facts disambiguate, e.g. `UnsatisfiedEffectBound_Kd` → `CallableEffectBound` or `VirtualEffectBound` by the
  obligation's kind) needs the kind enum outside the old Analysis, since Mir may not name `OwnershipFailure`.
- **Early unit, names unchanged.** Put `OwnershipRequirement` (the same 18 names in the same order, so every `Kind` number,
  name and table row stays) beside `DiagnosticRequirement` in `src/Kimi/Diagnostics`, and delete `OwnershipFailure`, the old
  Analysis using the Diagnostics enum directly (78 mechanical renames). Rejected alternative: keep `OwnershipFailure` and cast
  at the one report site (a second copy of 18 names until R6-check).
- **R4-M3a conflict.** R4-M3a rejected "a Requirement through a moved legacy enum" because it "would make old-path vocabulary
  a shared concept". That reason no longer holds for the names: they are already public, shared vocabulary
  (DIAGNOSTICS §4.1, the tinyhand table), and MIR keeps them (PLAN10 §7 "安定名は現行の … `Ownership.*` を引き継ぐ"). The unit
  needs a DEC entry superseding that clause. Old-only kinds leave with their producers: `ExpansionLimit` with
  `DeferredExpansionLimit_Kd` (R6-check), `TransferRequired` with unit T1 (Binding already has `Binding.TransferRequired`),
  `Internal` with Analysis; `UnprovenOrigin` and the three effect kinds follow R4's boundary unit 2a0 and unit a's mapping
  (R4-M17's compared set already names `Binding.OriginRelation` for A5 and `Binding.VirtualEffectBound` for A4).
- Can land now: no dependency on R4 or R5. Coordinate the 21 renames in `tests/xUnitTest/MirDifferenceLedger.cs` (R4 tooling
  owns it).

## 3. TransferRequired moving to Binding/HIR (R4-M4, ARCH:215)

- **Old sites (10, verified):** `OwnershipAnalysis.cs:836` (bare local, parameter or prepared slot consumed), `:1217` (a
  followed reference's selected referent), `OwnershipAnalysis.Borrows.cs:447` (non-Copy Field through a borrowed path),
  `.Closures.cs:60` (bare capture), `.Elements.cs:253` (bare element), `.Objects.cs:27` (object payload), `.Pointers.cs:87`
  (`*p`), `:302` (raw selected referent), `.Sequences.cs:65`, `:109` (sequence element). All are one rule: SPEC §3.5, a bare
  Place whose Copy is unproven never Moves.
- **Binding's own sites (7):** `Binding.ArgumentOperations.cs:926` (arguments, through `RequireAcquisition`),
  `Binding.Conversions.cs:269`, `Binding.ExplicitAdaptations.cs:269`, `Binding.ObjectViews.cs:219`, `Binding.Enums.cs:477`,
  `Binding.ValueCalls.cs:417`, `Binding.Closures.cs:523`; each fails the node (`FailAcquisition`, `Binding.Diagnostics.cs:1253`).
- **What A1 reports instead:** nothing for a Place with a direct Move Path (a local), and `StaticMovePathRequired_Kd` for one
  without (through a Deref, an Index, a handle or a call result: `MirInit.cs:160-170`; ledger rows `a1-g4`,
  `a1-g4-acceptance` (d)). Hence §0 finding 1.
- **Binding change.** The positions where an Op consumes a child are decided today only by the HIR publisher's `Take` calls
  (`Binding.Hir.Adjust.cs:26-156`), and publishing must decide nothing (R4-M61, `PublishIsInvisible`). The design that keeps
  one owner: (1) a behavior-preserving commit extracting "the children an Op consumes, with their adaptation" into one Binding
  function the publisher calls; (2) the rule: at each consumed bare Place with no adaptation, no written `@move`/`@copy`, and
  `BareAcquire == Move` (`Binding.PairCases.cs:251`), fail it with `BindingFailure.TransferRequired` (the existing kind and
  requirement), as the argument sites do; (3) delete the 10 old sites (unreachable once Binding fails those nodes; the old
  analysis does not run on an incomplete Binding).
- **Effects.** HIR publishes the failed node as Error, so MIR builds an Opaque value: no Move, no A1 cascade, and A1's
  `StaticMovePathRequired` there disappears (ARCH:215 "derived"). Old path: the requirement name changes from
  `Ownership.TransferRequired` to `Binding.TransferRequired`, and such a program's other old-path ownership diagnostics
  disappear until R6-check (Binding incomplete). Acceptance unchanged. The ledger's `a1-g4` rows change; coordinate with R4's
  A6 A1 mapping (G105, unit 1c).
- **Timing (owner decision).** Early: smaller R6-check and a clean ledger sooner, at the cost of the old-path diagnostic loss in
  rejected programs (R4-M18 precedent). At R6-check: no loss, but it must precede the flip. Either way it is a prerequisite of
  the switch, not of R4's exit.

## 4. StructuralCompletion and MatchCoverage into Binding

- `Analysis/StructuralCompletion.cs` (278 lines) reads only syntax and a Never predicate (no `using` of Binding or Analysis
  types). Users: CFA (:46, :80, :332, :1152), `Binding.ControlFlow.cs:16,517,533` (result sources), the HIR publisher
  (`Binding.Hir.cs:65-66`, "moves into Binding in R6"). R0 disposition: "置換 R6, HIR の完了要約（Binding）"
  (`artifacts/verify/reduction-R0/disposition.md:74`). PLAN10 §9 lists it under "全削除し、Mir/* で置き換える", contradicted by
  the HIR `Completion` column Binding publishes since R2b.
- `Analysis/MatchCoverage.cs` (45 lines): `FromSyntax` is gone (`git grep FromSyntax` empty). The record is a Binding fact
  (written in `Binding.Patterns.cs:245-738`, `Binding.Sequences.cs:336`; read by `OwnershipAnalysis.Match.cs:24`,
  `BodyLowering.Match.cs:197`).
- Both are file moves in namespace `Kimi.Compiler`, behavior unchanged, plus an entry contract header and CODEMAP rows
  101-102. No dependency; no collision except R4's late C5 (F6, "correlated completion"), which should come after.
- Seen in passing: `MatchCoverage.Describe()` builds Note text inside Binding (`Binding.cs:667`), against AGENTS
  "Diagnostics" (like G89); open issue, not part of the move. G47's "the two completion computations disagree" ends when CFA's
  flow goes at R6-check.

## 5. The 36 forwarding properties and Binding's accesses

- **They already forward.** Every one reads or writes a `HirTables` column or plan row
  (`Parsing/Koto/Koto.cs:479-543`, `BinaryKoto.cs:102-127,166-171,232`, `InvocationKoto.cs:19-37`, `TryKoto.cs:46-51`); there is no
  backing field left (R2a moved them). Some carry semantics: `BoundCall`/`BoundValueCall` (current binding and callee kind),
  `ArithmeticCall`, `ComparisonCall` (state and Active flag), `Formatting` (Active).
- **R2a recovery is not real (H, by reading).** The getters are one-expression wrappers over small static accessors
  (`HirTables.State(node) => node.SyntaxOwner is { } t ? t.Hir.States[node.SyntaxId] : default`, `HirTables.cs:60-72`), which
  the JIT inlines; rewriting `node.BoundType` to `HirTables.Meaning(node) as BoundType` yields the same code. The R2a cost
  (cold +3–4%, warm +1.5–2.1%, PLAN:33) is the column indirection (`SyntaxOwner` load, table, bounds check) and
  `SyntaxTable.EnsureId` on writes (`HirTables.Of`, :129-133); only a representation change removes it (R7's inline writes with
  a held table, R2b-D2). PLAN:33's "R6 also recovers the R2a Bind cost by deleting the Koto semantic fields and forwarding
  properties" should be corrected after one measurement (binding measurement harness).
- **Counts on origin/dev** (`git grep -o '\.<Name>\b'` over `Binding`): 1,268 accesses, ≈ 280 writes; by name `BoundSymbol` 448
  (112 writes, 94 files), `BoundType` 367 (20, 82), `BindingState` 183 (51, 46), `BindingFailure` 77 (13, 22), `BoundConstraint`
  42, `ConversionBinding` 36 (35 writes), `BoundCall` 20, `CallStorage` 17; others ≤ 10 (the regex over-counts property names
  shared with other types, H). Outside Binding: Parsing 48 (FunctionKoto 11, Koto 8, SyntheticKoto 4, BinaryKoto 4, others),
  Analysis 279, Emission 171, root helpers 25.
- **Collision with R4.** R4's remaining Binding units touch the files with the most reads: 2a0 (`Binding.Diagnostics.cs:985`,
  `Binding.TypeOrigins.cs:182-192`, `Binding.OriginProof.cs:443-449`, `Binding.ArgumentOperations.cs:565`; A3 §7), C2
  (`Binding.Closures`), C5 (F6), M39 (defaults), E4/E5 (rejected-node causes), U8p. There is no hold protocol for Binding files
  (ARCH:197-202 covers Mir only). An early rewrite is therefore churn against R4 with no behavior or speed gain.
- **Recommendation (owner decision).** Either (a) as recorded: rewrite Binding's reads at R6-check, after R4's exit and after
  R6-gen/R6-check delete Analysis/Emission (their 450 reads vanish), file group by file group with equal diagnostic, semantic
  and HIR snapshots, then delete the properties; or (b) keep the properties `internal` for Binding with `Transitional("R7")`
  (a later removal stage, owner approval per ARCH:159), remove the Parsing and root-helper reads at R6-check, and let R7's
  Binding consolidation, which rewrites these call sites anyway, delete them. (b) saves ≈ 1,000 edits that R7 would redo; (a)
  keeps ARCH's R6 date and the "At R6, Syntax's restriction extends to the whole Binding layer" rule (ARCH:163).

## 6. Synthetic Koto deletion and `ExpansionPlan.Evaluated`

- **Creation sites (15):** `new SyntheticKoto(` 10 (`Binding.ObjectCreation.cs:52-53`, `Binding.Ranges.cs:268-269,506-507`,
  `Binding.Views.cs:65-66`), `new EvaluatedKoto(` 2 (`Binding.PropertyCalls.cs:109`, `Binding.Ranges.cs:265`),
  `new FormattingKoto(` 2 (`Binding.Interpolation.cs:108,112`), `new RequirementCalleeKoto(` 1
  (`Binding.SelectedRequirements.cs:14`); the surveys' "23" counts all references. `TryKoto`'s inner arms are built by the
  parser (`Parsing/.../TryKoto.cs:19-40`).
- **Why not early, and probably not R6-check.** Each synthetic node exists so that Binding's ordinary, syntax-driven call
  binding can select a call it builds (range `PositionSyntax` calls, view and key calls, property update setters, requirement
  callees, formatting calls). Deleting them needs a call-selection entry that takes a callee and argument Types without
  syntax: R7's CallPlan pipeline (PLAN10 §8 item 6). Readers also include the old Analysis (`EvaluatedKoto` 8, `FormattingKoto`
  5) and Emission (1), so nothing can go before R6-check. Owner decision: move the deletion to R7 (a later removal stage) or
  pull a syntax-free selection entry into R6-check (a Binding rewrite of five desugarings, H: ≈ +400/−300).
- **Early-landable part.** `ExpansionPlan.EvaluatedArgument` recognizes the evaluated argument by
  `Source is EvaluatedKoto or MemberAccessKoto { Left: EvaluatedKoto }` (`Binding/ExpansionPlan.cs:59-69`), a syntax-class
  test inside a published contract the MIR builder reads. Binding knows the position where it creates the node (the two
  `new EvaluatedKoto` sites); recording it on the expansion node's plan row removes the test. Then MIR no longer depends on
  `EvaluatedKoto` when R7 deletes it (PLAN:33 "Evaluated needs a direct source").

## 7. Mir/Build syntax-class decisions

ARCH:135 ("syntax-class decisions in the MIR builder ... reach 0 by the end of R6") and ARCH:149 ("No Koto type test or
KotoKind dispatch in Mir/Build after R6"). My regex (type tests, `case XKoto`, property patterns, casts, `as`, `KotoKind.`)
finds 80 lines on origin/dev and 80 on `claude/a5-1b-mir` (the surveys' ≈ 59 excluded casts). Categories (line numbers on
`claude/a5-1b-mir`):

| Category | Sites | Examples | What removes them |
| --- | ---: | --- | --- |
| C1 casts and child reads after an Op/PlaceKind dispatch | 34 | `Values.cs:51-134` (19 casts after `case HirOp.X`), `MirBuilder.cs:213,226,300-312`, `Places.cs:12,42,45,46`, `Drops.cs:178`, `Expansions.cs:129,139,259`, `Formatting.cs:36` | ARCH allows "syntax children" (ARCH:28); a cast to read a child after the Op decided is not a decision. Needs an ARCH clarification (owner); otherwise a typed child-access representation (R7 scale) |
| C2 answerable from existing facts | 17 | `Control.cs:121` (`is ContinueKoto` → `HirOp.Continue`), `Defaults.cs:341`, `Places.cs:145`, `MirBuilder.cs:204,462` (closure → `HirOp.Closure`/`BoundClosure.Of`), `Places.cs:41` (`HirOp.Group`), `Values.cs:349` (tuple vs array → Type kind), `Values.cs:422` (char literal → `Folded`, G122), `Calls.cs:51` (parameter count → signature), `MirBuilder.cs:119,133,138,166` (definition kind → symbol kind), `Places.cs:175`, `Match.cs:167` (mutability → a small declaration view), `Places.cs:182` (tuple ordinal → a `Folded` row like ConstIndex), `Formatting.cs:15` (literal byte count) | Unit M1; 2–3 need a small Binding view with a reason in the commit (ARCH:35) |
| C3 operator and form identity | 29 | the `KotoKind → MirBinaryOp` table (`Operators.cs:14-29`), `Operators.cs:40-55,119`, `Calls.cs:17`, `Expansions.cs:183,200,334` (prefix/postfix, `^x`), `Formatting.cs:29` (interpolation vs `$tryWrite`), `Values.cs:279,319` (fill vs list literal) | Owner decision: (a) ARCH clarification that an operator token or form read under its published Op is a syntax datum, zero code; or (b) a new HIR vocabulary (≈ 20 entries) Binding writes in `PlanRow.Mode` (closed-vocabulary growth, O1-like) |

All are MIR-only, so D5 is unaffected and any of them can land before R6-check; each touches `Mir/Build/MirBuilder*.cs`,
which is under the active R4 MIR-lane hold (origin/dev PLAN §3) and shared with R5 — a hold is needed. R5's builder additions
(shims, instance key) must not add new syntax-class decisions (review obligation).

## 8. Old effect checks deletion

- **Pieces (origin/dev):** `Binding/Binding.EffectBounds.cs` 2,146 lines (`EffectSummary` :140–≈2,125, `ValidateEffectBounds` :72,
  `ValidateDestructionEffects` :40, `IsEffectLimit` :62, `SummarizePublishedOperation` :24, test-only,
  `tests/xUnitTest/Tests/EffectBoundImplementationTest.cs:119`); `Binding.CallableEffects.cs` 304; `Binding.VirtualEffects.cs` 85;
  `Analysis/OwnershipAnalysis.EffectBounds.cs` 65; `OwnershipBody.CleanupEffects.cs` 95; couplings `Binding.EffectObligations.cs:29,61`
  (violation tables) and `:109` `IsEffectRecord` (ledger, R4-M17/R4-M23); R4-M28's twins (ARCH:215).
- **Why not early.** These checks are part of today's acceptance (Binding partition records, `Binding.cs:549` call; the old
  analysis converts violations, OwnershipAnalysis.EffectBounds.cs:44-63). A4's problems stay unpublished until R6-check
  (R4-M1), so deleting any part earlier makes the default pipeline accept `confined` violations: unsound acceptance.
- **Why after F6.** `EffectSummary` also decides `preserves results` (`StoredValues`, `Conflicts`, `SharesDependency`
  :1848-1975) and delegation (`ValidateDelegations` :1251, SPEC §8.11.5). A4 decides only `confined` (R4-M22); §8.11.3–§8.11.6
  are A5 F6 (A5P App. A row 10), which waits for S2 (A5P App. B C5 "Held. Needs S2, and S5").
- **Published-row verification.** `PublishedOperationBodiesStayWithinTheirRows` checks 11 bodies under both `confined` and
  `preserves` (EffectBoundImplementationTest.cs:95-127). Its replacement needs A4's per-body summary (confined) and F6
  (preserves); only then can `SummarizePublishedOperation` go (R4-M28 "Open ... (R6)").
- **Array rows (R4-M28, ARCH:215).** The old path reads `PublishesSummary` only for `CompilerFunction == None`
  (`OwnershipAnalysis.cs:1671-1673`), so rows for the Array compiler functions (`KimiLibraryCatalog.cs:92-95`) would change only
  A4 (`TyCtxt.Published`, `MirEffects.cs:170`, `MirEffects.Solve.cs:350`). (H) This is an R4 catalog unit that could land in
  shadow; it is not an R6-check prerequisite.
- **Partition move.** Effect-bound records move from the Binding partition (Binding pass) to A4's publication (requirement
  per R6 unit a's mapping); a snapshot classification item.

## 9. The i128 gate, ClosureInstances and RequirementInstances

- **i128.** Binding rejects i128 `/` `%` at definition time (`Binding/Binding.Expressions.cs:1382-1386`,
  `BindingFailure.Unsupported`, "including unreachable bodies"). Further copies: `OwnershipAnalysis.Values.cs:134`,
  `OwnershipAnalysis.cs:1454`, `BodyLowering.BuiltinArithmetic.cs:26`, `BodyLowering.Graph.cs:632`, `NumericArithmetic.cs:12`.
  PLAN10 §5.5 A3b replaces "i128 の除算などの多重ゲート"; R4-M8 puts A3b in R5. Removing the Binding gate before MIR decides
  acceptance would let the default pipeline accept an unreachable i128 division (the old analysis checks no unreachable code
  since R1, PLAN §3 R1 regression). So: R6-check, after R5's A3b, in or after unit a; the record moves from Binding to
  ControlFlow (A3b). The Emission copies go at R6-gen, the Analysis copies with Analysis. In no survey's R6 list (critic §4.5).
- **`Binding.ClosureInstances.cs` (85)**: `ClosureSignature` has 7 Binding callers (`CallableProofs.cs:96`, `Constraints.cs:246`,
  `Diagnostics.cs:251,819,842`, `Expressions.cs:396`, `ValueCalls.cs:276`), one in `EffectBounds.cs:354` and one in Emission
  (`BodyLowering.Closures.cs:146`); `closureContexts` is the closure-context scan that A5P §9.1 makes R5's instance key
  (`ClosureContext` through `TyCtxt.SubstOf`) and PLAN:33 gives R7. **`Binding.RequirementInstances.cs` (198)**: `Witnesses`
  serves A4 (`TyCtxt.Effects.cs:39-40`, `MirEffects.Solve.cs:391`); `InstantiateRequirementCall`, `ProjectWitnessCall`,
  `InstanceReference` serve selection (`RequirementItems.cs:131`, `SelectedRequirements.cs:43`, `Specializations.cs:164`,
  `PropertyWitnesses.cs:112,143`). R0 says "削除 R6, Backend/Mono" (disposition.md:106, :191); PLAN10 §9 already lists
  `RequirementInstances` under R7's ContractSolver row. Disposition: R5 reads Subst-keyed views (`TyCtxt.Witnesses`,
  "Specialization by Subst", A5P §7), the selection parts stay until R7. A DEC note re-dispositions both; nothing in R6.

## 10. Owner decisions

1. **HIR lint error requirement kind:** one new `BindingFailure` member (89 → 90) vs the existing `Binding` requirement with
   fixed conditions. The first keeps the checks distinguishable by requirement; the second needs no vocabulary change.
2. **HIR lint errors and TransferRequired as node failures early:** accepts that, until R6-check, the default pipeline skips the
   old ownership analysis for programs with these errors (other ownership diagnostics disappear; acceptance unchanged; R4-M18
   precedent) — or non-failing lint records (recommended for the lint) and TransferRequired at R6-check.
3. **Forwarding properties:** Binding's ≈ 1,000 reads at R6-check as recorded, or a later stage (R7) for Binding's reads only.
   R6 gains no speed; R7 rewrites those call sites anyway.
4. **Synthetic Koto:** R7 (after the CallPlan pipeline gives syntax-free selection) or R6-check (pull that entry forward).
5. **Mir/Build operator and form identity:** ARCH clarification (operator token read under a published Op is syntax data;
   casts after Op dispatch are child reads) or a new HIR vocabulary for primitive operations and forms.

Planning-session decisions (no owner): supersede R4-M3a's requirement clause (§2); record FunctionFallthrough's owner as
Binding (§1.2); re-disposition ClosureInstances/RequirementInstances (§9); correct PLAN:33's R2a recovery claim after a
measurement (§5); list the early-landed R6-check items in the ARCH R6 row.

## 11. Risks

- R6-check flips acceptance before TransferRequired is in Binding: MIR accepts bare moves of non-Copy locals (§0.1); the ledger
  cannot detect it.
- A Binding lint that fails nodes silently widens the old path's "skip ownership on incomplete Binding" gap (SPEC §23.1.2
  independent errors) for a whole stage.
- The lint's second walk costs compile time until CFA's flow is deleted; it must be measured and recorded (P3).
- The forwarding rewrite, done early, conflicts textually with R4's Binding fact units (no Binding hold protocol).
- Mir/Build units conflict with the active MIR-lane hold and R5's builder work; each needs a hold.
- The requirement-vocabulary unit edits `MirDifferenceLedger.cs`, an R4-owned tooling file.
- Old effect checks deleted without F6 parity lose `preserves results`/delegation rejections (unsound acceptance).
- The i128 gate removed before A3b decides acceptance accepts unreachable i128 division on the default pipeline.

## 12. Corrections to the survey reports

- r6check-scope §0.3 and §4.2 "no owner" for the 13 codes: ARCH:15 names Binding (critic C13); additionally FunctionFallthrough
  belongs to Binding's result validation (SPEC §14.9.3), not A3, and A3 already covers RequireFallthrough only.
- r6check-scope §3 #3 / §9.3 present the requirement vocabulary as missing: `DiagnosticRequirement.tinyhand` already holds every
  `Ownership.*` name with descriptions (lines 590-642); only the kind enum is in old Analysis.
- r6check-scope §2.1 "Every forwarding property is already a thin wrapper": true, but several carry semantics
  (`BoundCall`, `BoundValueCall`, `ArithmeticCall`, `ComparisonCall`, `Formatting`); and PLAN:33's R2a recovery by deleting them is
  not expected (§5).
- r6check-scope §2.1 "SyntheticKoto x23 creation sites": 23 is references; creation sites are 10 (`new SyntheticKoto(`), 15 for all
  synthetic classes.
- r6check-scope §2.2 "≈59" Mir/Build sites: 80 by a regex that includes casts (34 casts, 17 existing-fact, 29 operator/form).
- The task text cites SPEC §23.3.6.6 for order: removed by the S1015 rewrite; order is DIAGNOSTICS-defined (§1.5).
- r6check-scope §1.5 (H) "ClosureInstances/RequirementInstances ... not R6 deletions": confirmed, and PLAN10 §9 already puts
  `RequirementInstances` in R7.
- No survey noted `BindingFailure` at 89 of 89, the TransferRequired soundness gap invisible to the ledger, or the G103 E9b
  dependency on a Binding-owned jump-target check.

## Units

Order: P1, P2, R1 (records, after the owner decisions), L1 (with the lint design), L2, L3, L4, T1a, T1b, E5, M1, M2 — all
early-R6-prereq; then C1–C5 in R6-check. Verification row for every code unit: AGENTS "Compiler, Kimi library or compiler
tests" (unit verification of the related checks, Session at the end of the working session); R1 is documentation
(diff and reference review).

| Id | Title | Stage | What changes (owner, files) | Prerequisites | Deliverable | Verification | Net size | Diff. | Review weight | Shared MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- |
| P1 | Move StructuralCompletion and MatchCoverage into Binding | early-R6-prereq | Binding: `Analysis/StructuralCompletion.cs` → `Binding/StructuralCompletion.cs`, `Analysis/MatchCoverage.cs` → `Binding/MatchCoverage.cs`, entry contracts; CODEMAP rows 101-102 | none | behavior-unchanged restructuring commit | build; `ControlFlowAnalysisTest`, `PatternBindingTest`, `HirColumnsTest`, `MirBuildTest`, `ArchitectureRulesTest`; semantic snapshot equal | +6 (moves 323 lines) | 1 | one review | no |
| P2 | Diagnostics-owned Ownership requirement kinds | early-R6-prereq | Diagnostics: `OwnershipRequirement` in `src/Kimi/Diagnostics/DiagnosticRequirement.cs` (18 names, same order); `OwnershipFailure` deleted from `Analysis/OwnershipModel.cs`, 78 Analysis and ≈ 52 test references renamed; DEC entry superseding R4-M3a's requirement clause | none (coordinate `MirDifferenceLedger.cs` with R4) | MIR and Core may name `Ownership.*` requirements; names, kinds and table unchanged | `DiagnosticOwnerTest` (table anomalies), `DiagnosticContractTest`, `MirDifferenceTest`, `PipelineSwitchTest`, `SemanticSnapshotTest`; `-DiagnosticSnapshot` equal | ±30 (+≈130 renamed lines) | 2 | full process (shared representation) | no |
| R1 | Record owners and dispositions of R6-check prerequisites | early-R6-prereq | DEC entry and ARCH R6 row: HIR lint (Binding P5), FunctionFallthrough → Binding, early-landed items, ClosureInstances/RequirementInstances (R5 view + R7), synthetic Koto and forwarding stages per owner decisions 3–4, PLAN:33 R2a correction | owner decisions 1–5 | one recorded plan for the R6-check prerequisites | diff and reference review | +≈40 doc lines | 2 | one review (DEC/ARCH are authority, not Q6 form 3) | no |
| L1 | HIR lint component; discard, while-true and owning-write warnings | early-R6-prereq | Binding: new `Binding/Binding.Lints.cs` (P5 walk at Final, evaluated-children walk, `GuessedBy` as a Binding query) with `DiscardedResult`, `UnusedTrySuccess`, `DiscardedValue`, `DiscardedTail`, `StaticWhileTrue`, `OwningWriteArgument` and `ExplicitDiscard`/`PropagateFailure`; removed from CFA; DIAGNOSTICS "Later phases" updated | P1; lint design (two designs, design review, DEC entry naming the L-group) | the warnings judged in Binding, requirement `Binding` | `OptionalTryDiscardTest`, `DiscardRepairTest`, `ControlFlowConformanceTest`, `CurrentControlFlowTest`, `Utf8TryWriteTest`; `-DiagnosticSnapshot` classified (requirement only); semantic snapshot; Bind/Analyze timing recorded | +≈230/−≈220 | 3 | full process (new component, phase move); then shared group review (Q6 form 2) if the owner approves the design | no |
| L2 | Unsafe permission in the HIR lint | early-R6-prereq | Binding lint: `UnsafeBlockRequired`, `UnnecessaryUnsafeBlock` with `Repair.RemoveUnsafe`, the one `RequiresUnsafeContext` rule; CFA :675's `UnsafeFunctionValue` deleted if a reproducer shows Binding's 4 sites cover it, else moved; CFA unsafe tracking deleted | L1; owner decisions 1–2 | one owner of the unsafe rule | `UnnecessaryUnsafeBlockTest`, `UnsafeFunctionValueBindingTest`, raw-pointer tests (`RawPlaceAccessTest`, `RawAddressTest`); snapshots classified | +≈130/−≈140 | 3 | L-group review | no |
| L3 | Transfer targets and labels in Binding | early-R6-prereq | Binding: `InvalidJumpTarget`, `UnlabeledYieldTarget` at the jump binding (`Binding.Expressions.cs:716-740` already resolves the target), `OverlappingLabel`; HIR contract "a Jump without a target is reported by Binding" stated for the builder (G103 E9b) | L1; owner decisions 1–2 | Binding-owned jump/label checks the MIR builder may trust | `ContextualLabelTest`, `ControlFlowRevisionParseTest`, `CurrentControlFlowTest`; ledger rows `b-derived` checked | +≈45/−≈40 | 2 | L-group review | no |
| L4 | FunctionFallthrough in Binding's result validation | early-R6-prereq | Binding: function/accessor result source check beside `AddBodyResult` (`Binding.ControlFlow.cs`); old `OwnershipAnalysis.cs:263` link and `earlierRejected`/`verified` (:101, :116) read the lint | L1; owner decisions 1–2 | Structural Completion's end-arrival check in Binding | `ControlFlowAnalysisTest`, `UnreachableOwnershipTest`, `DefaultCompletionTest`; ledger row `a1-result` unchanged | +≈30/−≈20 | 2 | L-group review | no |
| T1a | One Binding enumeration of consumed children | early-R6-prereq | Binding: extract "children an Op consumes, with adaptation" from `Binding.Hir.Adjust.cs` into one function the publisher calls | P1 | behavior-unchanged restructuring commit | `HirColumnsTest`, `MirBuildTest`, HIR snapshot (`KIMI_SEMANTIC_HIR=1`) equal, mir goldens equal | ±20 | 3 | one review (as the first commit of T1's full process) | no |
| T1b | TransferRequired in Binding | early-R6-prereq (or first R6-check unit, owner decision 2) | Binding: fail each consumed bare Place with `BareAcquire == Move` and no written acquisition (`BindingFailure.TransferRequired`); delete the 10 old sites; `Ownership.TransferRequired` leaves P2's enum | T1a, P2; coordinate with R4 G105/1c | SPEC §3.5 judged once in Binding; MIR sees Opaque values, A1 rows derived | Transfer/acquisition tests (`git grep TransferRequired tests`), `MirInitTest`, `MirDifferenceTest`; snapshots classified; ledger `a1-g4` rows reclassified | +≈100/−≈70 | 4 | full process (cross-phase) | no |
| E5 | Direct source for `ExpansionPlan.Evaluated` | early-R6-prereq | Binding: record the evaluated argument's position at `Binding.PropertyCalls.cs:109` and `Binding.Ranges.cs:265` on the expansion node's plan row; `ExpansionPlan.cs:59-69` reads it | none | no syntax-class test in a published contract | `MirBuildTest` (index, compound update), `HirColumnsTest`, mir goldens and MIR snapshot equal | +12/−10 | 2 | one review | no |
| M1 | Mir/Build decisions answerable by existing facts | early-R6-prereq | Mir/Build: the 17 C2 sites (§7); 2–3 small Binding views (symbol mutability, tuple ordinal, literal bytes) with reasons | MIR lane merged; hold recorded in PLAN §3 | 17 fewer syntax-class decisions | `MirBuildTest`, `MirAnalysisTest`, dump goldens, `KIMI_SEMANTIC_MIR=1` snapshot equal | +≈30/−≈35 | 2 | one review | yes (`Mir/Build/MirBuilder*.cs`) |
| M2 | Mir/Build operator and form identity | early-R6-prereq or R6-check | per owner decision 5: ARCH clarification only (C1 casts and C3) or a Binding-published primitive-operation vocabulary in `PlanRow.Mode` read by `MirBuilder.Operators/Expansions/Formatting/Values` | owner decision 5; M1; hold | Mir/Build at 0 syntax-class decisions under the decided reading | as M1, plus `HirColumnsTest` | 0 (clarification) or +≈70/−≈45 | 3 | full process if a vocabulary is added (owner O1); one review for the ARCH text | yes |
| C1 | Delete the old effect checks | R6-check | Binding/Analysis: `EffectSummary`, Callable/Virtual checking parts, `ValidateEffectBounds`, `IsEffectRecord`, violation-table reads, R4-M28 twins, `OwnershipAnalysis.EffectBounds.cs`, `OwnershipBody.CleanupEffects.cs`; published-row verification on A4 + F6 | R4 exit with F6 (S2 decided); unit a (MIR acceptance) | effects judged once (A4, A5 F6) | `EffectBound*Test`, `EffectBoundImplementationTest` rewritten, `MirEffectsTest`; snapshot classified (partition move) | −≈2,650/+≈60 | 4 | full process; R6-check final review | no |
| C2 | Remove Binding's i128 definition-stage gate | R6-check | Binding: `Binding.Expressions.cs:1382-1386` | R5 A3b deciding in acceptance (unit a) | one capability decision (A3b) | i128 division tests (G69 rows), A3b tests; snapshot: Binding → ControlFlow | −6 | 1 | one review | no |
| C3 | Forwarding-property removal | R6-check (or R7 for Binding's reads, owner decision 3) | Binding then Parsing/root: reads rewritten per file group (≈ 8–10 commits), then the 36 properties deleted | R4 exit; Analysis and Emission deleted | ARCH temporary structure removed | per group: diagnostic, semantic and HIR snapshots equal; Bind allocation tests unchanged; `ArchitectureRulesTest` semantic-slot ratchet | ≈1,000 lines rewritten, −≈120 net | 2 | one review per group, or one group review if the R6-check design names the group | no |
| C4 | Synthetic Koto deletion | R6-check (owner may move to R7, decision 4) | Binding/Parsing: syntax-free selection for the five desugarings; delete `EvaluatedKoto`, `SyntheticKoto`, `FormattingKoto`, `RequirementCalleeKoto`, `TryKoto` inner arms | E5; C-stage Analysis deletion; decision 4 | no synthetic syntax | `PropertyCall*`, range, view, interpolation, try tests; semantic snapshot; `ArchitectureRulesTest` auto-property ratchet 7 → 0 | +≈400/−≈300 (H) | 4 | full process | no |
| C5 | Delete CFA's remainder with the old Analysis | R6-check | Analysis: CFA flow, targets, `PendingBinding`, structural `RequireFallthrough` (≈1,070 lines); `Compilation.AnalyzeControlFlow`; remaining test calls (ratchet 64 → 0) | L1–L4, T1b; R6-gen (BodyLowering gone); unit a | no old control-flow analysis | Session with milestones; `ControlFlowAnalysisTest` MIR half only | −≈1,100 | 2 | in R6-check unit b's full process | no |
