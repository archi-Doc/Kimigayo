# R5, R6-gen and R6-check: Implementation Plan

Date: 2026-10-17

Status: Proposal (plan only). It changes no SPEC, compiler or test. The owner asked for a unit-level plan for R5, R6-gen and
R6-check and for an assessment of the parts that are too large or too hard (§6). An independent six-lens review with a
skeptic pass checked this text; its confirmed findings are applied.

Base: `dev` at `5c42b577`; current records read from `origin/dev` at `2aac946b`. Counts are grep counts at the base and
planning figures: the unit that uses a count re-measures it.

Conventions:

- PLAN10: [2026-10-10 Compiler Size Reduction and Single MIR Plan](2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md);
  A5P: [2026-10-16 Complete Polonius A5 Plan](2026-10-16%20Complete%20Polonius%20A5%20Plan.md); PRE5:
  [2026-10-12 Pre-R5 Work Items](2026-10-12%20Pre-R5%20Work%20Items.md); DEC, ARCH, PLAN, STATUS, VERIFICATION: the
  documents in `docs/dev/` and `docs/`.
- PD1–PD32 are this plan's planning-session decisions (§2.1) and OQ1–OQ17 its owner questions (§2.2); O0 and O2 are
  optimization levels.
- External IDs keep a prefix so they never collide with this plan's unit IDs: `DEC Q6`, `DEC O3`, `A5-F6` (an A5 family),
  `A5P-S2` (A5P's SPEC proposal), `PLAN10-X1` (the abstract-destruction hole of PLAN10 App. B), `G103-E9a/E9b`.
- Paths: `C/` = `src/Kimi/Compiler/`, `B/` = `src/Kimi/Compiler/Backend/` (new), `T/` = `tests/xUnitTest/Tests/`.
- "sel" = `KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir` (PD4): the old analysis decides, MIR construction, validator and
  collector records are published, and MIR generates. "bp" = VERIFICATION's behavior-preserving triple: the diagnostic
  snapshot equal to the previous run, the semantic snapshot equal (`KIMI_SEMANTIC_HIR`/`KIMI_SEMANTIC_MIR` when Binding or
  MIR changes) and the legacy fixture `.ll` byte-identical (E4).
- Verification rows (AGENTS): **Doc** diff and reference review; **Comp** unit checks of the named classes plus a Session at
  the end of the working session (`KimiLibraryShapeTest` added for library changes); **Tool** affected workflow checks;
  **Bench** Session with the whole-solution build plus the named measurement; **Evid** evidence only, no source change.
  Every Session builds the whole solution, `src/Benchmark` included.
- Review: **FP** full process (two independent designs, design review, final review); **D+R** one design and one review
  under an owner-approved full-process design recorded in DEC (DEC Q6 form 1; the design is named); **R** one review;
  **G:name** the shared review of a group that an owner-approved design names (DEC Q6 form 2; no merge into dev before it
  passes; each unit still verified and pushed); **—** none (evidence, or a DEC Q6 form 3 record).
- Hold: **W1/W2** R5 windows on shared MIR files (§3.5); **adv** a PLAN §3 hold line for a file outside ARCH's protocol
  (PD20); **no** R5-owned or unshared files.
- Difficulty: 1 mechanical; 2 small with a clear rule; 3 contract fixed, correctness needs care; 4 inherent switch risk;
  5 open algorithm (none in this plan).
- "(H)" marks an estimate or an inference not checked by running code.

---

## 1. Do formal plans exist?

No. There is no unit-level plan for R5, R6-gen or R6-check, and since DEC R4-M35 superseded PLAN10's single R6 row,
neither R6 stage has a written exit gate. What exists:

- PLAN10 §12.2's R5 row (scope; exit "every fixture at O0/O2 and every milestone matches or is recorded (e)", LLVM verifier,
  O2 within variation, O0 recorded) and §5.7–§5.8 (the target backend).
- A5P §9.1: R5 step 0 (an audit table and two measured codegen designs), start conditions, instance collection before
  acceptance (OD4), generated-code requirements, five evidence parts, timing, and R6 in two stages with `AnalyzeInstance` a
  "check-only step" until R6-check (OD10).
- ARCH's R5/R6 ledger rows and about 39 scattered "R6 unit a/b" obligations in DEC (R4-M1, R4-M3a, R4-M4, R4-M13a, R4-M17,
  R4-M23, R4-M28, PR-1, R2b-D5 and others).
- PRE5: baselines B1–B5, the P34 R1/R2 reproducers, G87 and record backups.

R5 start conditions at the base:

- Landed: the MIR fact contracts that R4-M35 approved (A3 §4.1) and the `MirAnalysis`/`MirDump` restructuring (`5ef517a6`).
  A5P §6.2's "publish 'not analysed' apart from check-only Drops" is not in A3 §4.1's approved table (its G3 row: "Group −1
  means check-only"), so it is not an open condition; this plan does not need it (PD12).
- The timing baselines were taken on `49cb2b5d` (ARCH R0 row, PLAN §3), but the evidence directory PLAN §3 cites
  (`artifacts/benchmarks/o2-corpus/20261017-111950-pre-r5-baseline`) exists neither in the main checkout nor in any worktree.
- PRE5 §2 (an owner gate, 2026-10-12) assumed that R5 starts at R4's exit. OD4 (R4-M35) starts R5 in parallel under A5P
  §9.1's conditions, and ARCH's R0 row records §9.1's baselines as taken. Whether that meets or supersedes PRE5 §2 is OQ2;
  until it is answered, no R5 code unit starts.
- The instance key, A3b's slot and the codegen selector land with their first R5 consumers, so they are R5's first units here.

---

## 2. Decisions

### 2.1 Planning-session decisions

Each is recorded in DEC in the commit that first implements it (AGENTS "Record decisions"). † marks a decision that departs
from an owner-approved text; it is repeated in §2.2.

| ID | Decision | Reason | Deletes or avoids |
| --- | --- | --- | --- |
| PD1 | Codegen design **B**: a direct `FunctionCodegen` writing LLVM text from validated MIR; design A (MIR into `EmissionModule`) judged on paper † (OQ3) | PLAN10 §5.7 already decided "no physical IR layer"; A maps MIR onto at most 38 of 49 opcodes, 11 encode old-IR concepts and about 20 sub-opcodes are strings; the writer stage is 0.016–0.066 ms of a 21–43 ms compilation, so a spike cannot discriminate | ~3,050 writer lines and `EmissionModule` kept alive; a second codegen rewrite; ~500 throwaway spike lines |
| PD2 | **Promote, don't copy**: syntax-free module-level writers and records move into Backend components that both the old writer and the new codegen call during shadow (`LlvmModuleText`, the runtime ABI and abort-reason tables, `LegacyRuntimeHelpers` [Transitional R8], `ObjectRuntime`, `LlvmScalarText`, `Layout`, `Abi`) | PLAN10 §12.1 names "R5 の IR 補助関数の移設と共有部品の整理" as an allowed D5 follow-up; PLAN10 §9 replaces `AggregateLayout`, `FunctionAbi*` and `WindowsLowering*` at R5/R6; AGENTS forbids a second copy of a rule; bp checks each move directly | Second copies of checked-arithmetic, helper, ABI and layout rules; structure-limit exceptions (members are split while moved) |
| PD3 † | **No `MirBodyKind.Shim`**. DropGlue, VTable and ObjectDescriptor are type-driven Backend mono items (PLAN10 §5.7 lists them as MonoItems). Entry, TestEntry and the call-forwarding shims (closure erasure, item adapter, comparison, format, arithmetic) are Backend mono items that emit every call, projection and switch only through `FunctionCodegen`'s primitives, so one ABI and attribute writer serves all | No MIR consumer needs them as bodies; departs from PLAN10 §5.4's body-kind list and A5P §9.1's start condition ("`MirBodyKind` has no Shim yet"); OQ4 offers the alternative | Shared-file changes to `MirVocabulary`, `MirValidator`, `MirProgram.Begin`, `MirDump` under R4's hold |
| PD4 | Codegen selector **`KIMI_CODEGEN`** (`legacy` default, `mir`), read once, `[Transitional("R6-gen")]`, marked in `CompilerIdentity` and the ArtifactId manifest. Until G2, `KIMI_CODEGEN=mir` is valid only with `KIMI_PIPELINE=mir` and any other combination throws, as `PipelineOf` does; "sel" is the pair. From G2 the default pipeline has mir semantics, so G3's default `KIMI_CODEGEN=mir` is valid on it | sel is exactly R6-gen's target (old acceptance, MIR records published, MIR codegen); R4's `-Pipeline mir` runs with legacy codegen stay unchanged; a third `KIMI_PIPELINE` value would make the baseline compiler `49cb2b5d` throw | A mixed (legacy, mir-codegen) cell and a second way into the MIR analyses |
| PD5 | Instance key `Instantiation(MirDefKey, TyCtxt.Erase(Subst))`, **erase late**: each instance keeps one representative unerased Subst on which witness, Copy, Sealed and applicability proofs run; `BoundType.ClosureContext` becomes a `Subst` (A5P-C1, A5P §9.1's start condition) | SPEC 8.13.3 and 3.3.1 exclude Origins from identity, but proofs read Origins (OJ3) | The never-cleared linear closure-context scan (`Binding.ClosureInstances.cs`); `SameModuloOpenOrigins` (8 Emission callers) at R6-gen |
| PD6 | The collector is a Backend component (`B/Instances/`) run by `Compilation.BuildMir()` after `MirAnalysis.Run`, before acceptance; library bodies enter by edges only; **Generated** is a mark pass at emit from the output roots (IMPL §21.3.1); A3b is its first capability pass | The driver may not decide acceptance (`MirAnalysis.cs:11`) and is R4's next shared edit (1b-5); layout obligations need the Backend; test discovery stays after acceptance | Any R5 edit of the shared `MirAnalysis` driver; the five old collectors (2,054 lines) at R6-gen |
| PD7 † | Collector records are published under sel during R5, in the ControlFlow partition with R3-1's MIR records, and decide sel acceptance; PLAN10 §7's "P11–P12 → Emission" yields for collection records (it follows from OD4 and is recorded in K4's OD4 entry); codegen contract failures are `InternalInvariant_Kd` | Resource codes have severity Error and reject at check, as `OwnershipStorageLimit_Kd` already does (DIAGNOSTICS:292 concerns rendering limits only) | A generation-time publication path for instance findings |
| PD8 | **Keep `EmitFixture`'s three built-in checks** (`ScalarEmissionTest.cs:130-142`: `__kimi_f<N>`/`__kimi_entry_body` naming, no `select i1` outside the shared failure block, no `store i1`) | Design B satisfies them by construction: instances are named `__kimi_f<N>`, the shared failure block (P4a) writes the `%zero`/`%minusone` select, bool is stored as `i8`, `SwitchInt` becomes `br`/`switch`; reopened only if a family finds one blocking for a reason SPEC does not require | An owner decision on the critical path; churn on 1,180 call sites |
| PD9 | Evidence compares **native behavior only**. bp is used for behavior-preserving legacy units (D5 evidence part 1); MIR-versus-legacy IR text and old-versus-new instance sets are information, never gates | A5P §2.1: parity with the old path stalled R4 | IR-shape parity work |
| PD10 | R5 metric machinery: a tracked **pending ledger** (one file per review group under `tests/xUnitTest/TestData/codegen-pending/`, unioned by Verify; an exact-equality gate that only shrinks; "passes now" reported apart from "no longer executed") and one canonical **deviation form** (`Codegen.Expect(target, legacy, item, class)` plus a `.deviation` sidecar, modeled on R4-M60; target under sel, legacy otherwise); both temporary, removed at G4 | R5 needs a metric that moves with every unit and cannot be gamed by deleting tests; per-group files keep lanes from serializing on one ledger | Per-test pipeline branches; ad-hoc xfail markers |
| PD11 † | **Codegen coverage rows**: a temporary whitelist of the MIR forms, intrinsic rows and Body-kind rows the MIR codegen implements; any other form under sel is a located `Unsupported_Kd` published as a typed record on `build` and `kimi test`; a temporary departure from ARCH:45-47 (Unsupported only from Binding, MIR construction and the capability check), under sel only; at R5's exit the remaining rows are decided D2 rows moved into the capability table (X2) | Closes the open family "not yet generated" with a whitelist (AGENTS) and gives each slice a visible metric; no silent wrong code | A capability table polluted with "not written yet" rows |
| PD12 † | **Generation gate**: codegen runs only on programs the deciding analysis accepted and reuses `Compilation.Generates` (MIR construction records, validator findings). It refuses a runtime-reached body for which A1 or A2 recorded a problem (R4-M6a's exactness holds only for programs A1 accepts; A3 problems do not gate). A body A1 skipped at the arena limit (R4-M15a: `OwnershipStorageLimit_Kd` at the body, no A2 rows) reports that Resource record at the body's subject. A refusal record carries the A1/A2 problem's own code at its subject, in the Emission partition; this departs from R4-M1 (C4a writes the superseding DEC entry for sel, G0 extends it to the default pipeline); its stage until H5 is OQ5. `InternalInvariant_Kd` is only for a runtime-reached Drop with `Group == -1` in a body A1 and A2 completed. New code returns no out-string failure text (ARCH structure limits). The gate's refusal path goes at H5, when MIR decides | A runtime-reached `Group == -1` arises only from the arena skip (`MirInit.cs:55-59`, `MirAnalysis.cs:79-82`); the two arenas differ (R4-M6a), so the old path can accept such a body | An internal fault on a resource-limited valid program; drops generated from an inexact table |
| PD13 | **Pooling is part of every codegen family's definition of done**: a warm allocation site leaves the pending ledger only at 0 B, or at its asserted bound for the 3 bounded sites | 186 generation measurement sites, 183 asserting 0 B | A final pooling unit that would freeze those sites in the ledger |
| PD14 | R4-M13a's 40 emission-pairing and 87 defect-injection `Ownership.Bodies` reads and R3-3's 119 `TryPrepare(out var module)` reads migrate at **R6-gen**, not R5 (their ratchet rows are relabeled); the 23 library-reach reads stay in R5 (K12) | They guard the legacy generator, the default until R6-gen (D5) | Premature deletion of legacy guards |
| PD15 | Tuple-operator comparisons are expanded by the MIR builder in window W1, reading the published `CallPlan.TupleOperator` and the operator-mode plan of `ComparisonPlan(CallPlan)`; both stay as builder facts under ARCH's "Calls" row (H11 keeps them) | MIR drops the operator mode today (`MirBuilder.Calls.cs:14-36, 118-124`), so `(1.0, nan) > (1.0, 2.0)` would be true under MIR codegen; the flag is "selected from source syntax, never inferred again" (`CallPlan.cs:117-118`), so nothing else can derive it | A new callee mode, or two `CompilerFunctionKind` entries (a DEC O1 request) |
| PD16 | Requirement witness by Subst: one resolver extracted from `InstantiateRequirementCall` (behavior-preserving first), then the view `TyCtxt.Witness` | One rule, two consumers; a synthesized CallPlan or a widened `Witnesses` would mix contracts | A second copy of the witness rules; `InstantiateRequirementCall` stays as the bind-time CallPlan materializer until R7 (Q8), and only `InstantiateForwardedCall` goes at R6-check |
| PD17 | Runtime type identity is the Origin-erased Type (SPEC 3.3.1); descriptor token = module-local ordinal; no Binding declaration-key fact | `WriteKey`'s text only deduplicates tokens and fills a constant nothing reads | `ObjectGenerationPlan.WriteKey`/`WriteContainers`' syntax walk; one cross-phase fact unit |
| PD18 | DropGlue never reuses `__kimi_drop_aggregate`: a drop-declaring struct gets its destructor only; other parts come from `Layout.Of` plus `MirTypes.MayNeedDrop` in SPEC 16.3.2 order | R4-M6b: MIR destructor bodies already drop their fields and base; the legacy template drops them again (`LlvmModuleWriter.Aggregates.cs:197-261`) | Double destruction |
| PD19 | Statics follow SPEC 22.4.3 and PLAN10 §5.6.6: `KIMI_E_STATIC_CYCLE` at the accessing Place (an (a) deviation from the legacy `KIMI_E_ABORT` at the declaration); generic-container, mutable, aggregate and drop-needing statics stay located `Unsupported_Kd` until P42; a constant-only `StaticInit` folds to a constant global (O2 parity). Closure environments of 8 bytes or less stay inline even when they need destruction (IMPL §21.1.3; one allocation fewer than legacy) | SPEC and IMPL are authority; P42 resumes after R6 | Reproducing legacy wrong output |
| PD20 † | **Hold lines beyond ARCH's protocol** for `Core/Compilation.Pipeline.cs`, `C/Binding/TyCtxt.cs`, `TyCtxt.Effects.cs`, `TyCtxt.Retention.cs`, `BindingModel.cs`, `Binding.ClosureInstances.cs`, `Binding.RequirementInstances.cs`, `KimiLibraryCatalog.cs`, `Checking/Hover/HoverBuilder.cs`, `src/Kimi/Diagnostics/DiagnosticRequirement.cs` and `tests/xUnitTest/MirDifferenceLedger.cs`; new Binding views go into new partial files (`TyCtxt.Erasure.cs`, `TyCtxt.Dispatch.cs`, `Binding/VTableDef.cs`); shared MIR edits are batched into W1 and W2 (OQ8 makes it an ARCH rule) | Both streams edit these files, but ARCH's protocol covers only `Mir` | Silent merge conflicts with R4's fact units |
| PD21 | No new `IntrinsicId`, `CompilerFunctionKind` or `BindingFailure` entries in R5/R6 (34/34, 76/76, 88/89 used); the seven producer-less R8 intrinsics get no producer, edge or codegen row | Two vocabularies are at their limits and the third has one slot, which is still an owner-approved entry; R8 owns those intrinsics | Owner vocabulary approvals |
| PD22 | `TestRecordValue` is produced by the MIR builder (F8); test phases are written by codegen around A2's runtime drops, deferred scopes and static initialization (PLAN10 §5.6.7) | A codegen rule reading a condition's defining rvalue would decide what the builder should publish | Per-pipeline `kimi test` failure output |
| PD23 | `MoveBytes` is lowered by its contract ("ranges may overlap", `KimiLibraryKinds.cs:116`) as `llvm.memmove`; the Dictionary kernel's O2 check measures it. If non-overlap is wanted instead, C8a changes the contract and the library declaration in the same unit | The contract decides; its two callers (`DictionaryStorage.kimi:107, 151`) do not overlap (H), so legacy `memcpy` is not a miscompile | A caller-reading rule and an R8 issue |
| PD24 | Debug information in R5 is a **design record only** (in S4): `StatementStart` gaps for control headers, match arms and `=>`; scopes; debug names, unique per module for `__kimi_f<N>`; slot policy; the artificial mark on the runtime text promoted into `LlvmModuleText`, `LegacyRuntimeHelpers` and `ObjectRuntime`; it replaces the Kimi Debug Adapter plan's `EmissionModule` placement. KD1 is implemented after the plan | PLAN10's R5 row and §17.3; A5P OD-P8 defers the debug adapter | Emission work before R6 |
| PD25 | IR-assertion triage: only what can still pass vacuously after E2 is classified up front (E1: the ad-hoc rewrites and negative assertions); a positive IR-text assertion is classified by the family unit that makes its test pass, under class rules the owner approves once † (OQ16) | A failing positive assertion is visible in the pending ledger; a vacuous one is not | A ~450-row upfront table on the critical path |
| PD26 | Keep the six benchmark stage names through R6-gen: collection is timed in stage 3 (inside `BuildMir`); under sel stage 5 times layout and stage 6 MIR codegen, documented in `src/Benchmark/CompilerPipeline.md`; at R6-check stage 4 is redefined (H9) | `benchmark-o2-corpus.ps1:119-125` rejects differing stage names against the baseline | Harness stage-map code |
| PD27 | **Escape valve**: a sub-form that no milestone, library or corpus program uses may be deferred as a D2 (e) row in STATUS and a PLAN §3 "Reduction regressions" line (ARCH:178); all such rows go to the owner as one list at R5's exit (the OD13 pattern). A narrow subset of a used form is never accepted (AGENTS "Diagnostics") | Stall-proofing without narrowing SPEC | Long tails holding R5's exit |
| PD28 | **R6-gen lands as soon as R5 exits**, before R4's exit if R5 is first, given OQ5 and OQ6. R6-gen still waits for the R4 MIR lane's merge, A5 step 1 (through Q2), G103-E9a/E9b and the lint group's jump part (§3.4). Cost: after G3 every R4 unit touching Mir/Build, `MirTypes`, `MirInit` or `MirDrops` changes default generated code and runs the affected native families and O2 kernels (ask A-10) | It deletes ~18k lines and the double-fix burden of D5 early | Two generators until R4 exits |
| PD29 | The R6-check flip and the deletion of the old effect checks are **one commit** (H5); the A4-based verification of published rows lands before it (H4); TransferRequired in Binding (H1) is in the same review group and merges together with H5, so no dev state has one without the other; under mir, A4's effect problems and H4's published-row records publish from H5 (H3 publishes the other MIR problems) | Once MIR problems publish, A4's effect records and the old Binding effect checks would report the same failure twice (suppressing one afterwards is a symptom fix); a Binding node failure without the flip would make the old analysis skip the program (`OwnershipAnalysis.cs:89-92`) | Duplicate effect records; a window that loses independent errors (SPEC 23) |
| PD30 | **G128**: `TyCtxt.Apply`'s storage-depth bound is not removed before R7 (PLAN G128's removal condition). The collector reports an `Apply` null at the requesting call with R4-M51's code (`Unsupported_Kd`, as A4 and A5 report the same cause), Until H3, A4/A5 problems are unpublished, so the collector's record is the only one published; H0 decides the single owner when `MirReport` absorbs the collector records (the collector reports no `Apply` null that A4/A5 already report, or an R4 ask has A4/A5 read an instance fact computed before them), never by suppression afterwards. From G2 such a program is an (e) row in STATUS and PLAN until R7; DEC O3 applies only if a library or milestone program is affected (K0 checks) | R4-M51 already decided the code for this cause; a second code for one cause would need a suppression rule | An R7 contract change pulled into R5 |
| PD31 | HIR lint error records reuse the existing `Binding` requirement (`BindingFailure.None`) with a fixed condition per code and are non-failing records, so today's independent diagnostics stay (SPEC 23); a new `BindingFailure` member goes to the owner only if Q8's design finds the reuse insufficient | No vocabulary change; node failures would make the old analysis skip whole programs | An owner question |
| PD32 † | FunctionFallthrough moves to Binding's result validation (SPEC 14.9.3 judges it by Structural Completion), not A3 as PLAN10 §5.5/§9 said; Q8's DEC entry names that departure and its reason | The rule is a typing check on the published Completion column | A second completion rule in A3 |

### 2.2 Owner decisions

Three packets, each answerable "as recommended" (the OD13 pattern). "Blocks" names the first unit that cannot start.

**Packet 1: send now (blocks R5 code).**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| OQ1 | Adopt this plan as the R5/R6 implementation plan (this document in `draft/Proposals`, step-0 design records in `draft/Sketches/R5 Step 0/`, one PLAN §3 pointer line) and its three exit gates (§5)? | Adopt | S1–S4 |
| OQ2 | Is PRE5 §2 met or superseded, so that R5 code may start? And the lost pre-R5 evidence | Superseded (DEC entry): OD4's §9.1 timing condition (ARCH R0 row) plus E6 and E7 replace B1 and B3; B4 is met once the audits fail closed (E2), because their test-coded counts become the PLAN10-P2 baseline; B5's command rows are added; RawPointers is not restored; the A=B variance is retaken on a fresh `49cb2b5d` checkout kept at a named location with a backup (E6) | S5 and every R5 code unit (P0–P8, E2, E6, E7 directly), and every O2 comparison from C1a on |
| OQ3 | Waive the measured A spike of A5P §9.1 ("both measured"), accepting B plus an independent review of "A on paper" as the two designs | Waive; B's first slice is measured against legacy instead (E13 at C1a's group review) | S5 |
| OQ4 | Approve the R5 design package (S2–S4) once, after its design review, as the owner-approved design under which R5 units take DEC Q6 form 1 and its named groups form 2. It includes PD3 (alternative: `Shim(kind)` MIR bodies for the call-forwarding shims, written by `FunctionCodegen`), the R6-gen switch contract in S2 (G0 cites it; it names G:R6-gen-switch and G:R6-gen-del), PD11's temporary ARCH:45-47 exception (coverage rows under sel, removed at X2), and two ARCH dependency notes (the Backend reads Core's runtime-location text and the Testing catalog's symbol column) | Approve as one package (otherwise about 8 more double designs) | All R5 code units after S5; G0 |
| OQ5 | From G2, publish at `check` on the default pipeline the MIR construction records, validator findings and collector records (departing from R3-1, R4-M1 and OD10's split, each with a superseding DEC entry in G2); and does `check` report instance-stage `Unsupported_Kd` and Resource records for the checked set (a Library with an unbuildable generic instance then fails check)? Between G2 and H5, is a PD12 refusal of an old-accepted program (an A1/A2 problem, or A1's arena record) reported at generation as a typed Emission-partition record, or published earlier at check? Accept that every default Analyze runs the shadow MIR driver (A3, A1, A2, A4 and A5 once landed, unpublished) from G2 until R6-check, with its cost measured in G2; PD7's partition choice (collector records in ControlFlow, so PLAN10 §7's "P11–P12 → Emission" yields) | Yes; yes (SPEC 23.2.2 leaves it implementation-defined; IMPL 21.3.3 requires a report for every instantiation of the checked set); at generation as a typed record (no duplicate with the old path's own `OwnershipStorageLimit_Kd`, so no suppression rule); accept, with G2's measurement | S5, K4's OD4 entry, G2 |
| OQ6 | `AnalyzeInstance` between R6-gen and R6-check. OD10 keeps it as a "check-only step", but `check` never runs it (`Project.cs:502-515`), it is the old lowering's plan provider, and its only callers are in the Emission that R6-gen deletes (`LlvmEmitter.cs:429`, `LlvmEmitter.Defaults.cs:15`) | Option (e): reproduce the two definition-level holes it covers (PLAN10-X1 abstract destruction; abstract temporaries; Q1), fix both in the old definition run as D5 miscompile fixes (Q2, Q3; DEC O3 applies if a library or milestone program is rejected), supersede OD10's clause (Q4, a G0 prerequisite) and delete it with its callers (G7). R6-gen then waits for A5 step 1 through Q2. Fallbacks: (d) R6-gen waits for R4's exit; (b) a check-only Subst→CallPlan bridge (+150–200 transitional lines, full process; not recommended) | Q2–Q4, G0, G7 |
| OQ7 | Native deviation classes allowed at R5's exit (PLAN10: "match or (e)") | Also (a) legacy-wrong and (d) intended, each with item and clause (static cycle code, inline closure environments, Abort caller locations per SPEC 22.5); otherwise MIR must reproduce wrong legacy output or the frozen legacy path gets D5 fixes | C3b, C5a, C7, X1 |
| OQ8 | Make PD20's hold list an ARCH rule | Adopt | none (PD20 applies meanwhile) |
| OQ9 | R5 metric and stop rule (§5.2), recorded in ARCH and DEC; the VERIFICATION change for R5 (a sel Session beside the default one at the end of every R5 working session, and the pending-ledger gate, removed at G4); ask A-10's checks on R4's MIR-builder merges (PD28's cost to R4); and whether CI gets a sel lane | Adopt; no CI lane (sweeps at group reviews and X1) | E11, C0b2, G3 |

**Packet 2: send now as well (R6-check prerequisites and the first codegen family).**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| OQ10 | Approve Q8 (after its design review) as the owner-approved design for Q7, the lint group G:L (Q9–Q12), Q16's 2–3 small Binding views, Q17's requirement table and H1's TransferRequired design, including PD32's departure from PLAN10 §5.5/§9 | Approve; otherwise each unit takes its own review and Q16/Q17 the full process | Q7, Q9–Q12, Q16, Q17, H1 |
| OQ11 | Approve H0 (after its design review) for H1–H5, G:R6-check-flip, G:check-del and G:check-tests | Approve; otherwise each unit takes its own review | H1–H15 |
| OQ12 | Binding's ~1,000 reads of the 36 Koto forwarding properties, and ARCH:163 ("At R6, Syntax's restriction extends to the whole Binding layer"): R6-check or R7 | R7 for both (a later removal stage): no speed gain, since the properties already forward to `HirTables` and PLAN §3's "R2a Bind cost recovery" should not be expected; R7's inline writes and CallPlan pipeline redo these sites. Parsing and root-helper reads go at R6-check (H14) | H14 |
| OQ13 | Synthetic Koto deletion (`EvaluatedKoto`, `SyntheticKoto`, `FormattingKoto`, `RequirementCalleeKoto`, `TryKoto` arms) | R7, after the CallPlan pipeline gives syntax-free selection (pulling that entry into R6-check costs about +400/−300 at difficulty 4) | H14 |
| OQ14 | ARCH:135 ("syntax-class decisions in the MIR builder reach 0 by the end of R6") and ARCH:149 ("No Koto type test or KotoKind dispatch in Mir/Build after R6"): 80 sites (34 casts to read a child after an Op dispatch, 29 operator or form identities, 17 answerable by facts) | Amend both: an operator token or form read under its published `HirOp`, and a cast to read a child, are syntax data; :149 then forbids only type tests and `KotoKind` dispatch that choose an operation no published `HirOp` decided. The 17 real sites are fixed (Q16). Alternative: keep :149, and H13 replaces the sites with facts or a HIR primitive-operation vocabulary (FP, DEC-OQ1-like) | Q16, H13 |
| OQ15 | SETTLED "Instance-evaluated generic defaults": PLAN10 §11 (approved through §16-15) rewrites its mechanism text at R6 unit a, while A5P OD9 (not adopted) says "the inline fold and the SETTLED entry stay" | Clarify which holds; the inline fold landed, so either cancel the rewrite or perform it at H15 as an explicit revision | H15 |
| OQ16 | IR-assertion class rules (R required by SPEC/IMPL, P behavior proxy, I incidental, S old-Emission structure) with delegated application: each family unit deletes the I and covered-P assertions of its tests and lists them in its commit | Approve the rules once (PD25) | E3's deletions; C1a's group review |

**Packet 3: later.**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| OQ17 | Minor: does the no-LINQ rule extend to tests? Is static shutdown pulled into R5 (+60 lines, ahead of P42)? Is one NativeAOT publish plus one milestone run added at the R6-gen exit (AGENTS runs NativeAOT tests only on explicit instruction, and the distributed compiler is NativeAOT)? | No; no; yes, one run | G11 |

---

## 3. Structure and order

### 3.1 Streams

| Stream | Content | Files | Can start |
| --- | --- | --- | --- |
| **S** step 0 | Audit table; design package (chapters K, F, C); owner-outcome records | `draft/Sketches/R5 Step 0/`, DEC, ARCH | Now (OQ1) |
| **E** evidence | Vacuous-pass classification, fail-closed instrumentation, fixture comparison, O2 harness, baseline retake, kernels, PRE5 carry-overs, pending ledger, deviation form, O2 metrics, pipeline benchmark, IR matcher | tests, scripts, `src/Benchmark`, Library (G87) | E1, E4, E5, E8, E9 now; E2, E6, E7 after OQ2 |
| **P** promotions | Behavior-preserving moves of module-level writers and records into `B/`; Core location text; Testing symbol column; `Layout` and `Abi` | old Emission (D5-allowed), `B/`, Core, Testing | After OQ2 (P5, P7, P8 after S5) |
| **F** facts | Binding views with first consumers; builder rules in W1; one `MirProgram` helper in W2 | new Binding partials, Mir/Build (W1), `MirProgram` (W2) | After S5 |
| **K** collector | Skeleton before acceptance, closure contexts as Subst, erasure, generic edges and limits, edge families, capability, layout obligations, Generated | Binding (adv), `B/Instances/`, Core (adv) | After S5 |
| **C** codegen | Selector, frame, then families | Core (adv), `B/Codegen/` | After K1, P7a, P8 |
| **Q** early R6 prerequisites | Old-path fixes for OQ6; R6-check's Binding-side prerequisites; requirement derivation and Problem facts with R4's A6 lane; ledger readings | old Analysis, Binding, Diagnostics, Mir/Build (W1), tests | Q1, Q6, Q13, Q15 now; the rest by gate |
| **R6-gen** | Design, file split, switch in two units, then deletions in call-graph order | Core, old Emission, tests, `src/Benchmark` | After R5's exit, OQ5, OQ6, Q4, G103-E9 |
| **R6-check** | Design (may start during R4), TransferRequired, publication, rehearsal, A4 row checks, the flip with the effect-check deletion, then deletions | Core, old Analysis, Binding, tests, `src/Benchmark` | H0 during R4; the rest after R4's exit and R6-gen |

### 3.2 Critical path

```
NOW ── packets 1+2 to the owner
 ├─ S1 audit; S2–S4 (two writers each; the deep dives are the first designs) ─► design review ─► OQ3/OQ4/OQ5 ─► S5
 ├─ E1, E4, E5, E8, E9 ─► (OQ2) ─► E2 ─► E3 · E6 ─► E7 · P0 ─► P0b ─► P1a–d, P2a/b, P3a/b, P4a/b (bp)
 ├─ Q1, Q6, Q13, Q15 · Q8 design ─► (OQ10) ─► Q9–Q12 lint group
 │                                                                              ▼
 │      S5 ─► F3 ─► K1 skeleton ─► C0a ─► C0b1 ─► C0b2 (first pending ledger) ─► C1a ─► C1b ─► C2a ─► C2b ─► C3a ─┬─► C3b
 │      S5 ─► P7a ─► P8 (P7a ─► P7b)                                                                               │
 │      K2 ─► K3 ─► K4 (generic, limits, OD4) ─► F5b ─► K6 ──────────────────────────────────────► (C3b) ──────┤
 │      W1 at max(MIR-lane merge, S5): F0 ─► F7 ─► F8 ─► F9 ─► Q16 (if OQ14)                                    │
 │                         drop chain: C4s ─► C4a ─► C4b ─► C4c ─► C5a ─► C5b  /  C6a ─► C6b ─► C6c  /  C8d ─► C8e │
 │                         branches from C3a: C7 · C8a ─► C8b · C8c ─► C9a ─► C9b ─► C9c · C8f · C10a ─► C10b   │
 │                                                                                                               ▼
 │                                               X1 sel sweep ─► X2 ─► X3 (R5 exit) ─► G0 ─► G1 ─► G2 ─► G3 … G11
R4: MIR lane merge ─► 1b-5 ─► 1c (A5 step 1) ─► step 2 (A5-F4, A5-F5, A5-F7, A5-F8, A5-F9, A5-F6) ─► A6 ─► exit tasks ─► final review
        │                │                                  │                                     │
        └► K7, C4a       └► Q2/Q3 ─► Q4 ─► G0               └► H0 design (Q8, A6 facts)           └► H1 … H15
                          G103-E9a/E9b (+ Q11 jump part) ─► G0
```

- The whole: max(R4's exit, R6-gen's exit) + R6-check. R5 and R6-gen do not wait for R4's exit, but they do wait for the R4
  MIR lane's merge (K7, C4a), A5 step 1 (Q2/Q3 → Q4 → G0), G103-E9a/E9b and the lint group's jump part (G0). If A5 step 1
  has not merged by X3, OQ6's fallback (d) applies.
- R5's longest chain (about 21 units): S4 → S5 → F3 → K1 → C0a → C0b1 → C0b2 → C1a → C1b → C2a → C2b → C3a → C4s → C4a →
  C4b → C4c → C5a → C5b (or C6a–C6c, or C8d–C8e) → X1 → X2 → X3. The library and formatting chains branch from C3a and run
  beside the drop chain. K2 → K3 → K4 → F5b → K6 is one chain feeding C3b, with about 1.5 sessions of slack (H).
- R6-check's chain: H3 → H4 (H3b beside it) → H5 → H9 (H8a–c beside it) → H10 → H11/H12/H14 → H15; H1 runs
  beside H3 and joins at H5. H0 starts during R4, off the post-R4 path.

Rough calendar in working sessions (one session lands 2–4 small or 1 large unit, review latency included; H):

| Chain | Estimate |
| --- | --- |
| Step 0 with owner turnaround | 2–3 |
| R5 after the package (two to three lanes; O2 A/B runs on a quiet machine pause the other lanes) | 18–26 |
| Q stream (one lane, beside R5) | 4–6 |
| R6-gen | 3–5 |
| R4's remainder (outside this plan; step 1 is an input to R6-gen) | 16–28 |
| R6-check after R4's exit and R6-gen (including the classification of the remaining ledger rows) | 6–10 |
| Now to R6-check's exit: max(R4, step 0 + R5 + R6-gen) + R6-check | ≈ 29–44 |

### 3.3 What can start now

Without an owner answer except OQ1 for the design records: S1–S4 drafting; E1, E4, E5, E8 (G87, PRE5 §4, adopted), E9 (PRE5
§3, adopted); K0 once S2 has a draft (a probe, no commit); Q1 (probe), Q6 (file move), Q13 (consumed-children extraction),
Q15 (Evaluated source). With OQ2: E2, E6, E7 and the promotions P0–P4, P6.

### 3.4 How R4 gates this plan

- **R5 and R6-gen** need from R4: the MIR lane's merge (it changes `MirTypes.MayNeedDrop`'s signature; before K7 and C4a),
  A5 step 1 (before Q2/Q3, so the same `Observes` rule exists on MIR), window W1, and G103-E9a/E9b before G0.
- **R6-check waits for R4's exit**: every A5 family A5-F1–F10 (A5-F6 needs A5P-S2 decided), A6, the exit tasks, no blocking
  ledger row and the final end-to-end review (R4-M35), plus R6-gen.
- **What R6-check banks during R4**: Q6–Q20 and H0's design.

Asks to the R4 stream (small items R4 owns; none adds a family):

| ID | Ask | Needed by |
| --- | --- | --- |
| A-1 | Merge dev before the next builder edit after R5's planning session records W1 | W1 |
| A-2 | (Optional) `MirDropTable` publishes per-body completeness; PD12's runtime test works without it | C4a diagnostics |
| A-3 | A5 rule rows for PLAN10-X1 and abstract temporaries with concrete twins in `MirBorrowsTest` (Q5) | none (kept after R6-check) |
| A-4 | OldRule, spec-case and pair-case target sides per family as it lands (A5P §8; Q20) | H5 |
| A-5 | A6: Loan facts on `MirProblem` and the requirement derivation in `MirReport` (Q17, Q18) | H3, H5 |
| A-6 | G103-E9a, all of G103-E9b, and every other MIR construction `Unsupported_Kd` in a valid fixture source, or its (e) record | X1, G0 |
| A-7 | A5-F6 (with A5P-S2) before R4's exit | H5 |
| A-8 | G103-E9b's jump part after Q11 publishes Binding-owned jump targets | G0 |
| A-9 | Merge the `MayNeedDrop` signature change before K7 | K7, C4a |
| A-10 | From C0b2, R4's MIR-builder merges run the sel pending check and record any rise; from G3, they select the affected native families and O2 kernels | C0b2, G3 |

### 3.5 Hold schedule with R4

| Window | Files | Stream | Units | Timing |
| --- | --- | --- | --- | --- |
| R4 MIR lane (active) | `Mir/Build/MirBuilder*.cs`, `Mir/MirValidator*.cs`, `Mir/MirTypes.cs`, `tests/mir` goldens | R4 | `claude/a5-1b-mir` | Until its group review merges |
| R4 1b-5 | `Mir/Analysis/MirAnalysis.cs` (driver) | R4 | 1b-5 | Next; R5 never edits the driver (PD6) |
| **W1** | `Mir/Build/MirBuilder.Calls*.cs`, `MirBuilder.Control.cs`, the `$abort` site, the Q16 sites, `tests/mir` goldens | R5 | F0 → F7 → F8 → F9 → Q16 (Q16 only if OQ14 is answered, else a later short window) | Opens at max(MIR-lane merge, S5); R5's planning session records it when F0 starts; 1–2 sessions |
| **W2** | `Mir/MirProgram.cs` | R5 | F10 | One short unit beside K7; may join W1 |
| adv | `Core/Compilation.Pipeline.cs` | R5 | K1, C0a, K4, G1 (split), G2, G3, G4 | A hold line per unit |
| adv | `Binding/TyCtxt.cs`, `TyCtxt.Effects.cs`, `TyCtxt.Retention.cs`, `BindingModel.cs`, `Binding.ClosureInstances.cs`, `Binding.RequirementInstances.cs`, `Checking/Hover/HoverBuilder.cs` | R5 | K2, F3, F5a, F5b, K10b, P8 (F2b), G7 | One hold line per unit |
| adv | `src/Kimi/Diagnostics/DiagnosticRequirement.cs`, `tests/xUnitTest/MirDifferenceLedger.cs` | Q | Q7, Q17 | One hold line per unit |
| none | `TyCtxt.Erasure.cs`, `Binding/VTableDef.cs`, `B/**` | R5-owned | K3, F4, all Backend | — |

After R4's exit no R6-check unit needs a hold. `Compilation.Pipeline.cs` is changed by K1, C0a and K4, so G1 splits it
(behavior-preserving) before G2 changes it again (AGENTS "restructure a file changed by three consecutive units").

---

## 4. Units

Net size: code `+added/−removed`; "move" marks a behavior-preserving move; `t` = test lines. Every unit that moves or
creates a component adds or updates its CODEMAP row; every new canonical component gets its ARCH canonical-component row
and its contract entry file in the commit that creates it.

### 4.1 R5 step 0 (S)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | Step-0 audit table | `draft/Sketches/R5 Step 0/audit.md`: one row per `CompilerFunctionKind` (75: 53 Body, 18 Intrinsic, 4 Shim), `IntrinsicId` (33), MIR callee/const/cast/aggregate form, old collector responsibility (11) and Emission→Binding call site (42 members at 82 sites: 29 published, 36 dropped re-checks or display, 6 new facts, 7 declaration-table reads, plus the facts hidden in syntax reads), with its old site and closing unit | OQ1 | Doc; a second reader samples rows | ≈ 450 rows | 2 | R | no |
| S2 | Chapter K: instance key, collection before acceptance, R6-gen switch contract | Second independent design beside the deep dive: erase-late key and A5P-C1; roots; edges per MIR form; the four limits in one `Admit` (growing key 16, depth 128, set 1,024; `Apply` null per PD30); Generated at emit; partitions and publication per stage (OQ5); A3b; capability table; layout obligations; the R6-gen switch (MIR in every Analyze, publication at check, PD12's gate, rollback, deletion order in call-graph order); review groups G:K-core, G:K-edges, G:C-frame, G:C-scalar, G:C-aggregate, G:call, G:C-drop, G:closure, G:object, G:library-1, G:library-2, G:format, G:test, G:R6-gen-switch and G:R6-gen-del | OQ1 | Doc | doc | 3 | FP (one design review for S2–S4) | no |
| S3 | Chapter F: R5 fact schema | Second design: `VTableDef`/`TyCtxt.Applies`, `TyCtxt.Witness` sharing `Witnesses`' core, `TyCtxt.Comparison(self, equality, operators)`, the tuple-operator expansion's inputs (PD15), `AdtDef.IsCLayout`, symbol columns (startup, imports, Dictionary storage, test cases), Core runtime-location text, `TyCtxt.Instantiates`; for each: producer, the post-Bind mutations it removes, R7 shape, first consumer | OQ1 | Doc | doc | 3 | FP | no |
| S4 | Chapter C: codegen, Layout/Abi, DropGlue, selector, coverage rows, debug-info record | Second design: `FunctionCodegen` contract (inputs, outputs, invariants, prohibitions), A on paper, the O2 parity list from LLVM_OPTIMIZATION's implemented rows (align, `noundef nonnull dereferenceable`, memcpy `noalias`, `noreturn`, no `mustprogress`/`willreturn`), PD2's sharing rule, `Layout.Of(erased Type)`/`Abi.Of`, DropGlue (PD18), `KIMI_CODEGEN` (PD4), the gate (PD12), coverage rows (PD11), deviation form (PD10), statics (PD19: the cycle Abort at the access, the `KIMI_E_STATIC_CYCLE` reason row, the constant-StaticInit fold, the D2 rows), the successor code and partition for output, native-configuration and backend failures after `GenerationFailed_Kd` (and whether `DiagnosticPartition.Emission` and the `Emission` requirement name remain), debug-info record (PD24) | OQ1 | Doc | doc | 3 | FP | no |
| S5 | Owner-outcome records | DEC: the OQ1–OQ9 answers; the S2–S4 package as the owner-approved design with its review groups (each PD, component and temporary structure is recorded by its first implementing unit); ARCH: the three exit gates (§5), the hold rule (OQ8), the "Temporary structures" list extended with the R5 temporaries; PLAN §3 pointer line | S1–S4 reviewed (and S6 if OQ3 is declined); OQ1–OQ5 | Doc | ≈ +60 doc | 1 | R (DEC/ARCH keep their review) | no |
| S6 | (Only if OQ3 declines) measured A and B slices | Throwaway A and B slices of the scalar families, timed before S5 | S4; OQ3 | Bench | ≤ 800 throwaway | 2 | — | no |

### 4.2 R5 evidence, tooling and PRE5 carry-overs (E)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| E1 | Classify what E2 cannot make fail closed | Planning record (`artifacts/verify/r5-evidence/`, backed up): the 23 ad-hoc rewrites in 12 files, the 33 negative `__kimi` lines in 16 files and the other negative IR lines; class, basis, covering fixture, disposition | — | Doc; a second reader on every row | ≈ 70 rows | 2 | R | no |
| E2 | Fail-closed native instrumentation | `tests/xUnitTest/NativeAllocationAudit.cs`: one import interposer for `@HeapAlloc(` and `@HeapFree(` references except the declare line, asserting one declaration and no stray reference; the existing `__kimi_exit` hook made fail closed (exactly one definition, no stray call); the transfer anchor required whenever a bound is given; a regression showing an unrewritten reference fails | OQ2 | Comp: the 88 audit classes; one full sweep `-Fixtures '*.ll'` (each audit fixture changes once); Session | +70/−35 | 2 | R | no |
| E3 | Ad-hoc rewrites and negative assertions | The 12 files use the interposer or assert their anchors (`Utf8FloatTest`, `StringComparisonEmissionTest`, `WorkloadCostTest`, `DynamicArrayCostTest`); the 33 negative `__kimi` assertions become positive counts or fixtures, or are deleted under OQ16 | E1, E2; OQ16 for deletions | Comp: the 28 files; their families O0/O2 | ±150 t | 2 | R | no |
| E4 | Fixture comparison script | `scripts/compare-fixtures.ps1`: `.ll` set and bytes (repo and worktree roots normalized in raw, `/` and `\5C` forms), sidecars with `.deviation` acceptance, `results.json` per fixture, identical-IR report; `tests/scripts/FixtureComparisonTest.ps1`; VERIFICATION "Behavior-preserving commits" names it | — | Tool: the new regression script, `VerificationInputsTest.ps1`; smoke run on two existing Sessions | +260 | 2 | R | no |
| E5 | O2 harness per-variant environment | `src/backend/windows-x64/benchmark-o2-corpus.ps1 -CandidateEnvironment` for B's builds and `--compiler-pipeline`, recorded per variant, A asserted selector-free; `src/Benchmark/O2Corpus.md` | — | Tool: A=B runs with an empty and a dummy candidate variable on a quiet machine | +30 | 2 | R | no |
| E6 | Retake A=B and back up records | Fresh `49cb2b5d` checkout at a recorded persistent location; evidence `artifacts/benchmarks/o2-corpus/<stamp>-pre-r5-retake`; backups of `artifacts/verify/reduction-R0`, `artifacts/handoff/r3`, `r4`, `artifacts/verify/mir-diff` (PRE5 D3); PLAN §3 baseline line corrected | OQ2, E5 | Evid; PLAN line record-only | 0 code | 1 | — | no |
| E7 | `Strings` and `Arithmetic` corpus kernels | `src/Benchmark/Kimi/Corpus/{Strings,Arithmetic}.kimi`, checksums, O2Corpus.md rows | OQ2, E5, E6 | Bench: A=B against `49cb2b5d` | +150 | 2 | R | no |
| E8 | G87: `Kimi.Result` conditional Copy (PRE5 §4, adopted) | Library clause, a `KimiLibraryShapeTest` Copy-set row, a regression that fails before; before the first pending ledger | — | Comp + library row | +10, t+30 | 2 | R | no |
| E9 | P34 R1/R2 reproducers (PRE5 §3, adopted) | Reproducers with their SPEC results as (a) rows; they become passing tests in C6c (R1, base receivers through reference layers) and in A5 (R2, string comparison) | — | Evid | 0 code | 1 | — | no |
| E10 | Pending ledger: report | `scripts/codegen-pending.ps1` beside `verification-selection.ps1`: failing tests by `type.method` with counts from the result XML, failing fixtures from `results.json`, `codegen-pending.json` ("passes now" apart from "no longer executed"), per-group files (PD10); `summary.json` fields | C0a | Tool: `tests/scripts/CodegenPendingTest.ps1`; Session | +150 | 2 | G:C-frame | no |
| E11 | Pending ledger: gate | `scripts/verify.ps1 -Codegen mir -UpdatePending`; the test and native steps pass iff the failing set equals the tracked ledger; the native step runs listed fixtures without failing the run; VERIFICATION rows (OQ9) | E10, C0b2, OQ9 | Tool: `VerificationSelectionTest.ps1`, `CodegenPendingTest.ps1`, `NativeFixtureRunnerTest.ps1`; Session | +110 + data | 2 | R | no |
| E12 | Deviation form | `Codegen.Legacy`, `Codegen.Expect(target, legacy, item, class)`, `LegacyNative` accepted by `ScalarEmissionTest.WriteFixture`/`EmitFixture` and `NativeAllocationAudit.WriteFixture`, writing `<name>.deviation` in both runs; E4 reads it; DEC temporary structure (removal at G4); lands with the first of C5a, C7 and K13a | E4, C0a | Comp: its first consumer's class plus a well-formedness test | +70 t | 2 | with its first consumer | no |
| E13 | Per-function O2 metrics | `compare-fixtures.ps1 -O2Metrics`: instructions and blocks per `define` in `<work>/native*/*.O2.ll`, `o2-metrics.json`, fixtures ranked by MIR/legacy growth (evidence part 5); C1a's group review uses it (OQ3's measurement) | E4, C0b2 | Tool: a case in `FixtureComparisonTest.ps1` | +90 | 2 | R | no |
| E14 | Compiler pipeline benchmark under sel | `CompilerPipelineBenchmark.cs`, `CompilerPipelineMeasurements.cs`, `CompilerPipeline.md` (PD26); the split-IR check compares with the public `WriteIr` under the same selector | C3a | Bench: slnx build, `--compiler-pipeline-check`, an A/B run | ±50 | 2 | R | no |
| E15 | Pipeline-neutral IR matcher | One test helper for generated-code requirement tests that names no old SSA value or helper; its own tests | C0b1 | Comp | +120 t | 3 | G:C-frame | no |

### 4.3 R5 promotions, Layout and Abi (P)

Every P unit is behavior-preserving on the legacy path (bp), and every member it moves ends within ARCH's limits (PD2).
`.ll.in` runtimes stay in `C/Emission/` with their resource names until R8. The units creating a canonical component write
its DEC component entry.

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| P0 | `B/` and `LlvmModuleText` | Header, Footer, memory/overflow declarations, runtime template load and splice, `ExternalName`/`SymbolFromName`, `WriteExternals`, number formatting and `LlvmConstantPool` move to `B/LlvmModuleText.cs`, `B/LlvmConstantPool.cs`; `NativeToolchain.cs:448`, `LlvmEmitter.cs:332` re-pointed; DEC component entry; the one ARCH code-rules edit that activates ARCH:164's no-LINQ rule for Backend | OQ2, E4 | Comp: `Kernel32ImportsTest`, `EmissionArtifactsTest`, a `ScalarEmissionTest` sample; bp | move +390/−370 | 1 | R | no |
| P0b | Runtime ABI table, abort reasons and representation records | `WindowsLowering.cs` (runtime `FunctionAbi` table, `RuntimeDefinitions`, `GetValue`, `GetCompilerFunction`), `WindowsLowering.Abort.cs` (17 reasons, `ExpandAbortReasons`, its LINQ `.Any` rewritten) and the records `FunctionAbi`, `AbiParameter`, `TypeLayout`, `ValueLowering` move to `B/`; P0's splice calls them | P0 | Comp: `AbortEmissionTest`, `ForeignEmissionTest`; bp | move +260/−250 | 1 | R | no |
| P1a | `LegacyRuntimeHelpers` core [Transitional R8] | `B/LegacyRuntimeHelpers.cs`: request records (`ArrayHelpers`, `DictionaryHelpers`, `Needs*`, `Dictionary*` ABIs, formatting lists) leave `EmissionModule`; an element descriptor (`ValueLowering`, drop name, text flag, Option offsets) replaces `AggregateLayout`; `StoredValues` kernels; the string-comparison text; DEC entry and ARCH temporary-structure row | P0b | Comp: `DynamicArrayCostTest`, `StringComparisonEmissionTest`, `KimiLibraryShapeTest`; bp | +260/−230 | 2 | R | no |
| P1b | Array helpers | `LlvmModuleWriter.Arrays.cs` (433) becomes a `LegacyRuntimeHelpers` partial; `BodyLowering` request sites follow | P1a | Comp: `DynamicArrayCostTest`; fam Array*; bp | move +440/−433 | 1 | R | no |
| P1c | Dictionary helpers | `Dictionaries.cs`, `DictionaryHelpers.cs`, `DictionaryStorage.cs`; the seven library ABIs become declared callees of the rows | P1a | Comp: `DictionaryLibraryTest`; fam Dictionary*; bp | move +200/−190 | 2 | R | no |
| P1d | Formatting helpers | `LlvmModuleWriter.Formatting.cs`, the formatting runtime splice (`LlvmModuleWriter.cs:185-211`), `WindowsLowering.Formatting.cs` | P1a | Comp: `Utf8ConsoleTest`; fam Utf*; bp | move +170/−165 | 1 | R | no |
| P2a | Dictionary iteration kernels | Split `WriteDictionaryIteration` (≈ 112 statements, a per-instruction serializer) into decoding (stays) and kernels as `LegacyRuntimeHelpers` rows | P1c | Comp: Dictionary* classes; fam Dictionary*; bp | +160/−120 | 2 | R | no |
| P2b | Sequence storage kernels | `Sequence` `FixedStorage`/`RawSlice`/capacity branches → kernels | P1b | Comp: fam Slice*, Fixed*, Raw*; bp | +70/−50 | 2 | R | no |
| P3a | Split `WriteObjects` in place | Constant runtime text, `WriteObjectDescriptor`, `WriteObjectCreation` (≈ 100 statements today) | P0 | Comp: `SharedObjectRuntimeTest`, `SharedArcRuntimeTest`; fam Object*, Shared*, Dynamic*; bp | ±240 | 2 | R | no |
| P3b | `ObjectRuntime` (permanent, PLAN10 §5.8) | The split runtime and `LlvmModuleWriter.SharedObjects.cs` move to `B/ObjectRuntime*.cs`; DEC component entry | P3a | as P3a | move +400/−390 | 1 | R | no |
| P4a | `LlvmScalarText`: checked arithmetic | `B/LlvmScalarText.cs`: checked arithmetic, division/remainder/shift checks, the failure block with the `%zero`/`%minusone` select; old `WriteScalar` decodes and calls it; DEC component entry | P0b | Comp: `ScalarEmissionTest`; fam Overflow*, Wrapping*, Bitwise*, Scalar*; bp | +250/−220 | 2 | R | no |
| P4b | `LlvmScalarText`: conversions | Bounds-checked conversions | P4a | Comp: `BitConversionTest`; conversion families; bp | +130/−120 | 2 | R | no |
| P5 | Runtime location text by `SourceRef` (Core) | `SourceLocationTable` moves to Core keyed by `SourceRef` (SPEC 22.5.1–22.5.2 text); `BodyLowering.cs:544` and `LlvmEmitter.cs:168` pass `SourceRef.Of(node)` | S5 (ARCH note) | Comp: `AbortEmissionTest`; fam Overflow* (stderr); bp | +75/−69, t+20 | 2 | D+R (S3) | no |
| P6 | Test-case symbol column | `src/Kimi/Testing/TestCatalog.cs`: `TestCase` gains a `BindingSymbol`; `TestRuntime.Create(catalog, names)` takes entry names in case order | OQ2 | Comp: `TestProtocolTest`; `test-testing.ps1` selected; bp | +20/−12 | 1 | R | no |
| P7a | `Layout.Of`: scalars, handles, sequences | `B/Layout.cs`: pure memoized `Layout.Of(Type)` for scalars, handles (8/16 B), sequences (2/3/7 words; managed field counts as constants marked for R8, `AggregateLayout.cs:297`); `AggregateLayoutPool.GetCore` calls it for these kinds | S5, P0b | Comp: new `T/LayoutTest.cs` goldens, `InlineLayoutTest`, `KimiLibraryShapeTest`; bp | +130/−100, t+80 | 2 | D+R (S4) | no |
| P7b | `Layout.Of`: struct, tuple and enum placement | Alignment-descending groups, C written order (F1), enum tag plus union, zero-sized parts; `GetCore`'s placement code goes, its destruction callbacks are split out, its `Open` re-check is deleted (Binding rejects C layout on open structs, `Binding.Attributes.cs:466-470`) | P7a, F1 | Comp: `InlineLayoutTest`, `LayoutAttributeBindingTest`; fam Aggregate*, Struct*; bp | +180/−150, t+60 | 3 | D+R (S4) | no |
| P7c | Layout limits as typed results | Size, count and depth bounds (`AggregateLayout.cs:47, 171-175`) returned as typed results; the old pool maps them to its existing failure | P7b | Comp: `InlineLayoutTest` limit rows; bp | +50/−30 | 2 | R | no |
| P8 | `Abi.Of(signature instance)` | `B/Abi.cs` from `FunctionAbiPool.Build`'s pure core plus the `#LibraryImport` C ABI branch; `FunctionAbiPool.Get(FunctionKoto)` calls it; no `Supports` gate in `Abi` (the old one stays in the old pool until R6-gen) | P7a, F2b | Comp: `ForeignEmissionTest`; fam Function*, Result*; bp | +150/−120, t+40 | 2 | D+R (S4) | adv |

### 4.4 R5 Binding facts and the builder windows (F)

Each fact lands with its first consumer (R4-M2a, no stubs).

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F0 | Split `MirBuilder.Calls.cs` (behavior-preserving) | By the responsibilities present: call assembly; comparison relation (where F7's expansion goes); intrinsic and serve helpers | W1 | Comp: `MirBuildTest`, `MirDumpTest`, goldens equal; bp | ±130 | 1 | R | W1 |
| F1 | `AdtDef.IsCLayout` | Set in `Binding.Attributes.cs` `ValidateLayoutFragments`; first consumer P7b | S5 | Comp: `LayoutAttributeBindingTest` | +8, t+15 | 1 | D+R (S3) | no |
| F2a | Startup symbol column | `StartupResult` symbol plus module ordinal (`Binding.Startup.cs:25`); first consumer C0b2 | S5 | Comp: `StartupBindingTest` | +8, t+10 | 1 | D+R (S3) | no |
| F2b | Library-import symbol column | `LibraryImport` (`BindingModel.cs:672`); first consumer P8 | S5 | Comp: `LibraryImportTargetBindingTest` | +6, t+10 | 1 | D+R (S3) | adv |
| F2c | Dictionary-storage symbols | Symbol view of the library's `DictionaryStorage` functions that the legacy rows call; first consumer C8e | S5 | Comp: `KimiLibraryShapeTest` | +10, t+10 | 1 | D+R (S3) | no |
| F3 | `TyCtxt.Instantiates(BindingSymbol)` | Declaration-table view replacing the syntax-only `FunctionKoto.RequiresInstantiation`; first consumer K1 | S5 | Comp: `TyCtxtTest` | +15, t+15 | 1 | D+R (S3) | adv |
| F4 | `VTableDef` and `TyCtxt.Applies` | `Binding/VTableDef.cs` filled in `PrepareVirtualOverrides` (base slots, then own virtuals in member order, SPEC 6.2.1, no path/span sort); `TyCtxt.Applies(original, declaring)` over `VirtualApplicability` (null for a closed Type = `InternalInvariant_Kd`) | S5 | Comp: new `T/VTableDefTest.cs` (fragments, override replacement, inherited override, Refuted → null, generic instance, edit invalidation, warm 0 B); `Virtual*Test` unchanged | +90, t+80 | 3 | G:object | no |
| F5a | One requirement-witness resolver (behavior-preserving) | Extract `ResolveRequirementWitness` from `InstantiateRequirementCall` and its property, fixed-array and integer-position helpers (`Binding.RequirementInstances.cs:53-128`); the CallPlan materializer calls it | S5 | Comp: comparison, format, iterator and fixed-array families; bp | ±150 (net ≈ +20) | 3 | R | adv |
| F5b | `TyCtxt.Witness(requirement, closed Subst)` | `TyCtxt.Dispatch.cs`: the view shares F5a's core with `TyCtxt.Witnesses`; a twin test against `InstantiateForwardedCall` over every requirement call of the generic library and milestone bodies | F5a, K4 | Comp: `TyCtxtTest` twin; `MirEffectsTest` unchanged | +60, t+100 | 3 | G:call | adv |
| F6 | `TyCtxt.Comparison(self, equality, operators)` by Type | View over the private `ComparisonPlan` overload, with the operator mode, leaves as `(Target, Subst)`; first consumer C8f | F5b | Comp: `ComparisonContractTest`, `ComparisonCompositionTest` | +25, t+40 | 2 | G:library-2 | no |
| F7 | Tuple-operator comparisons expanded in MIR (PD15) | Mir/Build: expand a comparison whose `CallPlan.TupleOperator` is set by the operator-mode plan of `ComparisonPlan(CallPlan)`: numeric leaves as IEEE `Binary`, string and Contract leaves as calls, nested tuples recursively, a lexicographic chain with early exit, SPEC 13.4's unordered rule; ARCH "Calls" row names both builder facts | F0 | Comp: `tests/mir` golden (f64 tuple `<`, nested tuple, string element), `MirBuildTest`, `MirValidatorTest`, `MirEffectsTest`; an R4 reviewer signs off (A1–A5 see the expansion) | +60, t+50 | 3 | D+R (S3) + R4 reviewer | W1 |
| F8 | `TestRecordValue` producer | `MirBuilder.Control.cs` `Observe`: `TestRecordValue(side, operand)` for each scalar operand of a built-in comparison condition, before `TestObserve` | F0 | Comp: golden, `MirBuildTest`, `MirEffectsTest` | +25, t+25 | 2 | R | W1 |
| F9 | `$abort` takes the macro's `SourceInfo` | The Abort call's `SourceInfo` is the `$abort` expression (SPEC 22.5.1), not the inner invocation | F0 | Comp: golden | +3, t+20 | 1 | R | W1 |
| F10 | `MirProgram.StaticInit(body, operand)` | One helper for "the StaticInit body a `staticEnsure` names" (third consumer: K7); `MirBuildTest.cs:39`'s copy replaced; A4's copy (`MirEffects.cs:160`) left to R4 as an open issue | W2 | Comp: `MirBuildTest` | +8/−6 | 1 | R | W2 |

### 4.5 R5 instance collector (K)

Until G2 the collector runs, and its records are published, only under sel (PD4), so R4's `-Pipeline mir` runs stay unchanged.

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| K0 | Spike: collector inputs (no commit) | Probe: does the old build accept a deep finite Subst (`pick<Option^32<ref/i32>>`, G128), and does any library or milestone program have one? Instance counts and old collection time over the library, milestones and the Generic shape 1–128; a check-time cost baseline; one inferred C-layout zero-sized instantiation through today's path | S2 draft | Evid `artifacts/verify/r5-k0/` | 0 | 1 | — | no |
| K1 | Collector skeleton | `B/Instances/Instantiation.cs`, `CheckedSet.cs`, `InstanceCollector.cs` (contract entry): own-module nongeneric roots (F3), edges for closed Fn/Getter/Setter/DefaultTemplate/closure calls, runtime bits from `MirReach`, per-body edge templates, pooled arrays; `Compilation.BuildMir()` runs it after `MirAnalysis.Run` under sel; no records yet | S5, F3 | Comp: new `T/InstanceCollectorTest.cs` (roots, closed edges, check-only edges in the checked set, runtime bits), warm rebuild 0 B (`Purpose=Allocation`), `ArchitectureRulesTest` | +200, t+100 | 3 | G:K-core | adv |
| K2 | Closure contexts as `Subst` (A5P-C1) | `BindingModel.cs:517` field type; `TyCtxt.Intern(closureContext: Subst?)`, `SubstOfClosure`, a closing factory; `Binding.ClosureInstances.cs` deletes the never-cleared list and scan; `Binding.ClosureCall` bridge `[Transitional("R6-gen")]` for `GenericStoragePlan.cs:312` and `BodyLowering.Closures.cs:84` (D5 follow-up this plan names); Hover `SubstIdentity` in a new `HoverBuilder` partial (the file is at its token limit); DEC R5-C1; ARCH "Types and substitutions" row | S5 | Comp: `TyCtxtTest`, `HoverProjectionTest`, `MirEffectsTest`, `MirBuildTest`, closure emission classes; bp on closure families | +40/−40 | 3 | D+R (S2) | adv |
| K3 | `TyCtxt.Erase` and the instance key | `C/Binding/TyCtxt.Erasure.cs`: Erase of Type and Subst, memoized, closure contexts recursive, idempotent | K2 | Comp: `TyCtxtTest` (idempotent; `Erase(Apply(t,s1)) == Erase(Apply(t,s2))` for Origin-only differences; closure contexts) | +100, t+80 | 2 | G:K-core | no |
| K4 | Generic edges, limits and records (OD4) | Representative unerased Subst; edges through `TyCtxt.Compose` and `Implementation(…, 0)`; one `Admit` for growing key, depth and set size (located `GenerationResourceLimit_Kd` at the requesting call) and `Apply` null (PD30); `MirRecords` adds collector records under sel (ControlFlow); the DEC OD4 entry and the ARCH pipeline correction (the first commit whose collection decides acceptance); DIAGNOSTICS:306; `CompilerPipeline.md` stage 3 | K1, K3, K0 | Comp: `InstanceCollectorTest` (Origin-only variants → one instance; finite recursion reuse; limits located; the G128 reproducer; warm 0 B; 2×/4× work counters); zero collector records over the library and the 40 harnessed milestones under sel; check cost measured (stage 3, Generic shape 1/32/64/128, warm LSP Analyze); slnx build | +230, t+220 | 3 | G:K-core | adv |
| K5 | Closure, item and value edges | `Aggregate(Closure)`, `Const(FnItem)`, Value callee (closure through `SubstOfClosure`; ordinary item through a pure item Subst), `Cast(Erasure)` → ClosureErasure/ItemAdapter items | K4 | Comp: collector closure and item cases | +90, t+60 | 2 | G:K-edges | no |
| K6 | Requirement edges | Requirement callee → `TyCtxt.Witness` → Fn edge, built-in shim item, or a located `Unsupported_Kd` for the OCC-X refusal | K4, F5b | Comp: collector requirement cases | +50, t+50 | 2 | G:call | no |
| K7 | Drop, destructor and static edges | Drop → `DropGlue(Ty)` through a `MirTypes.Destruction` visitor (A2 verdicts ignored for the checked set); destructor instances (`AdtDef.DestructorSymbol` + `Synthesized`, destructor-only, R4-M6b); owning-handle payload glue (Sealed proved on the representative); `staticEnsure` → StaticInit through F10 | K4, F10, A-9 | Comp: collector cases (generic destructors, growing destructor contexts located, statics) | +120, t+80 | 3 | G:K-edges | no |
| K8 | Object descriptors and VTables | `MakeObj`/`MakeRc`/`MakeArc`/`Clone`/`TypeTest` → `ObjectDescriptor(Ty)` and `VTable(Ty)` items, entries by F4 proved on the representative; bodies of inapplicable overrides stay checked-only roots (R4-M28) | K4, F4 | Comp: collector cases mirroring `VirtualGenericDispatchTest` and `VirtualConditionalDispatchTest` | +120, t+80 | 3 | G:object | no |
| K9a | Capability table and A3b | `B/Capability.cs`: one function and its rows (i128 `/` `%` with compound forms; i128 ↔ f32/f64); A3b over every body including check-only blocks; Opaque operands skipped, so Binding's gate never duplicates; `Unsupported_Kd` once per location; DEC capability entry (implements R4-M8); ARCH row | K4 | Comp: `GenericGenerationDiagnosticTest`'s sources under sel (direct, remainder, compound, independent locations); unused nongeneric i128 still reported; no duplicate | +100, t+80 | 3 | G:K-edges | no |
| K9b | Instance-stage capability pass | Per-body templates of Subst-dependent capability sites, checked per closed instance; K9a's once-per-location rule; requesting-call versus body location decided as K13a expects | K9a | Comp: forwarded i128 cases | +50, t+40 | 3 | G:K-edges | no |
| K10a | Layout limits of collected instances | By-value uses → `Layout.Of(erased)` typed limits → located Resource records | K4, P7c | Comp: `InlineLayoutTest` equivalents under sel | +40, t+40 | 2 | G:K-edges | no |
| K10b | Inferred C-layout zero-sized Field obligation | Binding's existing rule (`Binding.CheckCLayoutInstance`, `Binding.Attributes.cs:382-403`) published as a `TyCtxt` view; the collector reports its Language record at the by-value use (SPEC 8.15.3); one owner of the rule | K10a, K0 | Comp: an inferred C-layout instantiation test with a valid twin | +60, t+50 | 3 | D+R (S2) | adv |
| K11 | Generated column at emit | Mark pass from the output roots (startup closure; test roots after `Tests.Discover`; Library public roots) over runtime edges; codegen reads Generated instances only | K4, C0b1 | Comp: test builds generate only the test closure; unreferenced instances are not generated; a codegen request outside Generated raises `InternalInvariant_Kd` (negative test) | +70, t+60 | 2 | G:C-frame | no |
| K12 | Library-reach `Bodies` reads → Generated | The 23 reads R4-M13a tagged R5 | K11 | Comp; `Ownership.Bodies` ratchet 183 → 160 | ±100 t | 2 | R | no |
| K13a | OD4 capability expectations | `GenericGenerationDiagnosticTest` i128 rows get the target (an acceptance-time record under sel) with the legacy value through E12 | K9b, E12 | Comp: the class under sel and default | ±100 t | 2 | G:K-edges | no |
| K13b | OD4 limit expectations (non-object) | Limit rows of `GenericStorageEmissionTest`, `GenericDestructorEmissionTest`, `InlineLayoutTest`, `RecursiveDefaultTest`, `GenericPointerEmissionTest`; `OwnershipStorageLimitTest`'s instance row marked for G7 | K7, K10a, E12 | Comp: the named classes under sel and default | ±100 t | 2 | G:K-edges | no |
| K13c | OD4 limit expectations (objects) | Limit rows of `VirtualGenericDispatchTest` and `GenericObjectFactoryTest` | K8, E12 | Comp: the named classes under sel and default | ±50 t | 2 | G:object | no |

### 4.6 R5 code generation slices (C)

Common to every C unit: under sel, every test and fixture whose MIR uses only the coverage rows implemented so far leaves
the pending ledger; the others stay listed as the located `Unsupported_Kd` PD11 reports. Named families are information,
each completed by the last unit that touches it; M1's three-unit rule counts only the rows a unit adds. Requirement tests use
E15's matcher; warm allocation sites reach 0 B or their bound (PD13); the unit's I and covered-P assertions are deleted under
OQ16; bp on the legacy path; a fixture that times out under sel stays in the ledger, and raising its `.timeout` needs a
recorded O0 measurement and reason in the commit. New files are `B/Codegen/FunctionCodegen.<Concern>.cs`, each member at
most 60 statements, no LINQ.

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C0a | Codegen selector | `Core/Compilation.Pipeline.cs`: `KIMI_CODEGEN` read once, valid only with `KIMI_PIPELINE=mir` (PD4); `WriteIr`, `Validate`, `EmissionArtifacts.Publish` and the `kimi test` path (`TestCommand.cs:131-135`) route through one entry and publish coverage-row and gate records as typed located records; PD12's gate on `Compilation.Generates`; `CompilerIdentity` and ArtifactId mark; `scripts/verify.ps1 -Codegen`, `summary.json` | S5, K1 | Comp + Tool: `PipelineSwitchTest` (throwing combinations, gate refusals), `VerificationSelectionTest.ps1`; default output unchanged | +160 | 2 | G:C-frame | adv |
| C0b1 | `FunctionCodegen` frame | `B/Codegen/ModuleCodegen.cs`, `FunctionCodegen.cs` (contract entry): coverage rows (PD11); prologue from `Abi.Of`; one alloca per local (bool `i8`, none for zero-sized); labels for runtime-reached blocks (`MirReach` without CheckTarget); `Goto`/`SwitchInt`/`Return`/`Unreachable`; scalar `Use`/`Const`; storage pooled per `Compilation` | C0a, P0, P7a, P8 | Comp sel: a selected list of `MinimalEmissionTest` and scalar-return tests plus new frame fixtures; a test that an uncovered MIR form yields one located `Unsupported_Kd`; warm 0 B on its inputs; determinism (two sel runs byte-equal) | +250, t+100 | 3 | G:C-frame | no |
| C0b2 | Entry, `writeLine`, first program | `Shim(Entry)` from F2a; the console `writeLine` legacy row; module assembly with `LlvmModuleText`; Library output form (no entry, `_fltused`); the first per-group pending files | C0b1, F2a, E10, OQ9 | Comp sel: a selected list of console-output tests and new hello fixtures; `test-library.ps1` selected | +120, t+40 | 2 | G:C-frame | no |
| C1a | Arithmetic and comparisons | `FunctionCodegen.Arithmetic.cs`: operator table by (`MirBinaryOp`/`MirUnaryOp`, Type) over P4a; Proven checks omitted; located Abort text through P5 | C0b2, P4a, P5, E15 | Comp sel: Scalar*, Overflow*, Wrapping*, Bitwise*, Char* rows; group review with E13 metrics and E6's baseline (OQ3's measurement) | +200, t+60 | 2 | G:C-scalar | no |
| C1b | Casts | `FunctionCodegen.Casts.cs`: `MirCastKind` table over P4b | C1a, P4b | Comp sel: Float* and conversion rows | +150, t+40 | 2 | G:C-scalar | no |
| C2a | Places and projections | `FunctionCodegen.Places.cs`: the 7 projections, bounds-checked `Index` over fixed array, Array and Slice with located Abort text, `PairLayer` per selected view, Deref kinds | C1b, P7b, P5 | Comp sel: Element*, Borrowed*, Fixed* rows | +250, t+60 | 3 | G:C-aggregate | no |
| C2b | Rvalues and aggregates | `FunctionCodegen.Rvalues.cs`: `Use`, `Ref`, `Aggregate` (Tuple/Adt/FixedArray, enum tag and payload), `Discriminant`, `Len`, `Adapt` (`MirCases.Adapt`), `PairAcquire`, memcpy copies, zero-sized values | C2a | Comp sel: Aggregate*, Struct*, Composite* rows; a multi-view body from `tests/mir/cases.kimi` natively | +250, t+80 | 3 | G:C-aggregate | no |
| C3a | Calls and ABI | `FunctionCodegen.Calls.cs`: Fn callees named by the collector (`__kimi_f<N>`), result slots, prepared slots, `noreturn` and `unreachable` after target-less calls | C2b, K4 | Comp sel: Function*, Result*, Never*, Generic* rows; an ABI result-slot requirement test | +200, t+60 | 3 | G:call | no |
| C3b | Requirement callees and caller location | Requirement callees through K6's instance; hidden caller-location operands per `KimiLibraryCatalog.RequiresCallerLocation`, forwarded inside caller-location bodies | C3a, K6, P5, F9, OQ7 | Comp sel: Requirement*, Associated* rows; `AbortEmissionTest`, `LibraryAbortContextTest`, bounds stderr fixtures | +110, t+60 | 3 | G:call | no |
| C4s | Drop test matrix | MIR-level and native tests fixing exactly-once destruction: Keep/Elide/Flag/Open rows, multi-view bodies, sealed recursion, partial moves, destructor bodies that drop their own fields (R4-M6b), an A1-skipped body (PD12); exact free counts through E2 | E2, E3, C3a | Comp: pass on legacy; enter the pending ledger under sel | t+220 | 2 | G:C-drop | no |
| C4a | DropGlue, Keep and Elide | `B/Codegen/DropGlue.cs`: struct, tuple, enum, fixed-array and string glue (PD18); Drop terminator Keep → glue call, Elide → nothing; PD12's refusal of A1/A2-problem and A1-skipped bodies, with the DEC entry superseding R4-M1 for generation-time refusals under sel | C4s, K7, A-9 | Comp sel: `MirDropTest` cases, `GenericDestructorEmissionTest`; destructor rows with exact free counts | +200, t+60 | 3 | G:C-drop | no |
| C4b | Drop flags | Flag allocas from `MirDropTable.FlagsOf`, flags start clear, the selected view's flag points written before each terminator's branch; Flag verdict → `load i8` + `br` + glue | C4a | Comp sel: C4s Flag rows; conditional and partial-move rows | +100, t+40 | 3 | G:C-drop | no |
| C4c | Open verdict rows | Per child row: project with the row's step and apply its verdict; a component without a row follows the own bit | C4b | Comp sel: C4s Open rows; Owned*, Ownership* rows | +80, t+40 | 3 | G:C-drop | no |
| C5a | Closure environments and value calls | `FunctionCodegen.Closures.cs`: `Aggregate(Closure)` environments (8 bytes or less inline, PD19), Value-callee calls, FnItem constants | C4c, K5, OQ7, E12 | Comp sel: Closure*, Callable*, Concrete* rows; an inline-environment requirement test; `ContextualClosureTest` "Object" through E12 | +150, t+60 | 3 | G:closure | no |
| C5b | Erasure and item-adapter shims | `Cast(Erasure)` adapter shim; ItemAdapter shim (successor of `CompilerFunctionAdapters`, compiler items included), both through `FunctionCodegen`'s call primitives (PD3) | C5a | Comp sel: Callback* rows; the ErasedCalls kernel O2 A/B | +150, t+60 | 3 | G:closure | no |
| C6a | Object descriptors and handle operations | `B/Codegen/ObjectDescriptors.cs` (token = ordinal of the erased payload Type, bases by `AdtDef.Base`, Copy by `TyCtxt.Copy`, destroy = DropGlue, factory ABI) and `MakeObj`/`MakeRc`/`MakeArc`, `Clone`, `TypeTest`, `Upcast`, object `Deref` over `ObjectRuntime` | C4c, K8, P3b | Comp sel: `SharedObjectRuntimeTest`, `SharedArcRuntimeTest`, `GenericObjectFactoryTest`, `PipelineObjectTest`; Object*, Shared*, Dynamic* rows with allocation counts | +200, t+80 | 3 | G:object | no |
| C6b | Handle glue | obj/rc/arc release per SHARED_OBJECT_RUNTIME; a Sealed payload → direct glue, else descriptor finalize | C6a | Comp sel: `SealedObjectFinalizationTest`, `SharedObjectCleanupTest`; the rc/arc order test | +80, t+40 | 3 | G:object | no |
| C6c | VTables and virtual dispatch | `VTable(T)` from F4 and `Synthesized`; Virtual callee slot load; the erased virtual-Item thunk; E9's R1 reproducer passes | C6a, F4 | Comp sel: `VirtualDispatchTest`, `VirtualConditionalDispatchTest`, `VirtualGenericDispatchTest`, `VirtualEntryErasureTest`; Virtual* rows; Milestone 44; the virtual kernel O2 | +150, t+60 | 3 | G:object | no |
| C7 | Statics | `B/Codegen/Statics.cs`: a global slot per `MirStatic.Symbol`, the `staticEnsure` state machine, StaticInit calls, the cycle Abort at the access (PD19; a new `KIMI_E_STATIC_CYCLE` row in the shared reason table is a declared legacy `.ll` change listed in the unit and in E4's comparison), the constant-StaticInit fold; generic-container and drop-needing statics are D2 coverage rows | C3a, K7, OQ7, E12 | Comp sel: `StaticInitializationTest` (cycle row through E12), Static* rows, `tests/mir/statics.kimi` programs natively | +160, t+100 | 3 | D+R (S4) | no |
| C8a | Raw memory and failure rows | `RawAllocate`/`Release`/`Initialize`/`Slice`, `TryAllocateBytes`, `MoveBytes` as `llvm.memmove` (PD23); Abort and Fail (the 7 MIR codes → reasons) | C3a | Comp sel: Raw*, Abort* rows; `MoveBytes` checked through C8e's Dictionary rows and kernel | +120, t+40 | 2 | G:library-1 | no |
| C8b | `replace`/`exchange`/`swap` and storage kernels | Rows over P2a/P2b kernels | C8a, P2a, P2b | Comp sel: update and storage rows | +150, t+40 | 2 | G:library-1 | no |
| C8c | Strings | `BinaryOp` on strings, string patterns, string glue, `BuiltinEquals`/`Compare` on strings | C8a, P1a, E7 | Comp sel: String* rows; `StringComparisonEmissionTest`; Strings kernel O2 | +100, t+40 | 2 | G:library-1 | no |
| C8d | Array family | 8 operations, 8 storage-boundary kinds, `Capacity`, element glue rows, item adapters for compiler items; zero-sized elements | C8b, P1b, C4c | Comp sel: Array* rows; `DynamicArrayCostTest`; Array kernel O2 | +200, t+80 | 3 | G:library-2 | no |
| C8e | Dictionary family | 7 storage kinds, empty-Dictionary initialization, `placeEntry`, element glue; the collector roots the hidden `DictionaryStorage` instances (F2c) | C8d, P1c, F2c | Comp sel: Dictionary* rows; `DictionaryLibraryTest`, `DictionaryCostTest`; Dictionary kernel O2 | +150, t+60 | 3 | G:library-2 | no |
| C8f | Comparison and arithmetic shims | `BuiltinEquals`/`BuiltinCompare` composite shims over F6; the BuiltinArithmetic shim; the Dictionary key-equality row; NaN and ±0 tuple fixtures (after F7) | C8c, F6, F7 | Comp sel: `ComparisonContractTest`, `ComparisonCompositionTest`, `BuiltinComparisonTest`, `ElementComparisonTest`, `ComparisonOperatorTest` | +150, t+80 | 3 | G:library-2 | no |
| C9a | Text, Buffer, Window and Writer rows | The 25 fixed-symbol rows through `LegacyRuntimeHelpers` | C8c, P1d | Comp sel: Utf* writer rows | +100, t+40 | 2 | G:format | no |
| C9b | ErasedWriter and BuiltinFormat | ErasedWriter dispatch (writer kind plus user `reserve` through `TyCtxt.Witness`), the BuiltinFormat shim, user-witness wrappers, `toString`/`tryFormat` | C9a, F5b | Comp sel: the Utf8 writer classes | +150, t+60 | 3 | G:format | no |
| C9c | Interpolation | `FormatBuffer`/`FormatHint`/`FormatFinish` with per-instance estimates (port of `FormattingBound`), a stack buffer of at most 1,024 B when bounded and direct, the literal fast path | C9b | Comp sel: `Utf8ConsoleTest` (33-byte stack, no heap), utf8-formatting §6.2 counts; interpolation kernel O2 | +150, t+80 | 3 | G:format | no |
| C10a | Test intrinsics and TestEntry | `TestObserve` (Location → `TestCatalog.SiteOf`), `TestMessage`, `TestRequireAbort`, `TestTempDirectory`, `TestRecordValue` (F8); `Shim(TestEntry)` through P6 | C3a, P6, F8 | Comp sel: `TestProtocolTest`; `test-testing.ps1` O0/O2 | +100, t+40 | 2 | G:test | no |
| C10b | Test phases | `TestPhaseEnter`/`Leave` around runtime Drops (A2), deferred scopes and static initialization, in test builds only (PD22) | C10a, C4c, C7 | Comp sel: `TestProtocolTest` cleanup-phase cases | +60, t+40 | 2 | G:test | no |

### 4.7 R5 exit (X)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| X1 | Exit sweep (the G2+G3 rehearsal) | A full Session under sel with `'*.ll'` at O0/O2, the 40 harnessed milestones and the CLI harnesses (`test-library`, `test-modules`, `test-testing`, `test-emission`, `test-cli`): its failure set equals exactly the methods G2 and G3 rewrite (the 20 instance-failure methods, `StaticInitializationTest:127`, the `.deviation` targets) or decided (e) rows; the diagnostic snapshot under sel classified; the legacy run; E4 parts 1–4; O2 corpus B/A on every kernel; O0 times; the PLAN10-P3 stage record | every C, K and E unit; A-6 | Evid `artifacts/verify/reduction-R5/` | 0 | 2 | — | no |
| X2 | Close the coverage rows | Coverage rows reduced to decided D2 rows (one owner list, PD27), moved into the capability table; STATUS and PLAN (e) rows | X1, OQ7 | Comp | ≈ −50 | 1 | R | no |
| X3 | R5 exit records | ARCH R5 done; area sizes, change hotspots and duplication candidates in `artifacts/benchmarks/`; exceptions and Transitional declarations reviewed with the owner; the end-to-end codegen review | X2 | Doc | ≈ +20 | 1 | R + end-to-end review | no |

### 4.8 Early R6 prerequisites (Q), landing during R4/R5

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Q1 | Reproduce PLAN10-X1 and abstract temporaries (no commit) | CLI probe: a generic `f<C: Iterable>` holding `local.iterate()` across `local = other@move`, with an observing iterator and with `Array<i32>`; `make: () -> T; let r = make()@ref` used later; `check` and `WriteIr` verdicts | — | Evid | 0 | 1 | — | no |
| Q2 | Old definition run: abstract destruction observes | `C/Analysis/OwnershipAnalysis.Cleanup.cs` `CleanupObservesBorrows` and `OwnershipAnalysis.cs:698`: an abstract part not proven Copy observes (mirrors `MirTypes.Observes`, R4-M36); D5 miscompile basis (A5P §9.2) | Q1 reproduces; A5 step 1 merged; OQ6 | Comp: `GenericGenerationDiagnosticTest`, `BlockEscapeLoanTest`, Borrowing*, `GenericOwnershipCaseTest`, `PairFollowTest`; `-DiagnosticSnapshot`; Session with milestones and `KimiLibraryShapeTest` | +15/−2, t+30 | 3 | R | no |
| Q3 | An abstract temporary is a Loan root | `C/Analysis/OwnershipBody.Borrows.cs` `IsTemporaryOriginRoot` (`:2745-2751`); STATUS "Generic temporaries" row removed | Q1; A5 step 1 merged; OQ6 | as Q2 plus `ScalarTemporaryBorrowTest` | +5/−1, t+25 | 3 | R | no |
| Q4 | DEC: `AnalyzeInstance` is deleted at R6-gen | A superseding entry for OD10's clause naming the reason that no longer holds; ARCH R6 row | Q2 and Q3 (or Q1 shows no hole); OQ6 | Doc | +8/−2 | 1 | R | no |
| Q5 | (Ask A-3) A5 rule rows for the same programs | `T/MirBorrowsTest.cs` rows with concrete twins | A5 step 1 | R4's group review | t+20 | 1 | A5 group | no |
| Q6 | `StructuralCompletion` and `MatchCoverage` into Binding | File move `C/Analysis/` → `C/Binding/`, entry contracts, CODEMAP rows | — | Comp: `ControlFlowAnalysisTest`, `HirColumnsTest`, `MirBuildTest`, `ArchitectureRulesTest`; bp | +6 (moves 323) | 1 | R | no |
| Q7 | Diagnostics-owned Ownership requirement kinds | `OwnershipRequirement` (18 names, same order) in `src/Kimi/Diagnostics/DiagnosticRequirement.cs` (the names already live in `DiagnosticRequirement.tinyhand`); `OwnershipFailure` deleted; 78 Analysis and ≈ 52 test references renamed; DEC superseding R4-M3a's requirement clause | Q8, OQ10 | Comp: `DiagnosticOwnerTest`, `DiagnosticContractTest`, `MirDifferenceTest`, `PipelineSwitchTest`, `SemanticSnapshotTest`; bp | ±30 (+130 renamed) | 2 | D+R (Q8) | adv |
| Q8 | R6-check prerequisite design package | FP: requirement kinds (Q7); the HIR lint for the 13 ControlFlowAnalysis codes (`DiscardedResult`, `DiscardedTail`, `DiscardedValue`, `UnusedTrySuccess`, `StaticWhileTrue`, `OwningWriteArgument`, `UnsafeBlockRequired`, `UnnecessaryUnsafeBlock`, `UnsafeFunctionValue`, `InvalidJumpTarget`, `UnlabeledYieldTarget`, `OverlappingLabel`, `FunctionFallthrough`) with PD31's records and PD32's departure, Binding-owned jump targets for G103-E9b; Q16's 2–3 small Binding views; Q17's requirement table; TransferRequired in Binding (H1's design); dispositions (`Binding.ClosureInstances`/`RequirementInstances` → R7); after OQ10, a DEC entry recording Q8 as the owner-approved design for Q7, G:L (Q9–Q12), Q16's views, Q17's table and H1 | OQ1 | Doc | doc | 3 | FP | no |
| Q9 | L1 HIR lint: discard, while-true, owning write | `C/Binding/Binding.Lints.cs` (a Binding pass-5 walk at Final): `DiscardedResult`, `UnusedTrySuccess`, `DiscardedValue`, `DiscardedTail`, `StaticWhileTrue`, `OwningWriteArgument` with their repairs; removed from `ControlFlowAnalysis.cs`; DIAGNOSTICS "Later phases" | Q6, Q8, OQ10 | Comp: `OptionalTryDiscardTest`, `DiscardRepairTest`, `ControlFlowConformanceTest`, `CurrentControlFlowTest`, `Utf8TryWriteTest`; snapshot classified (requirement only); Bind/Analyze timing recorded | +230/−220 | 3 | G:L | no |
| Q10 | L2 unsafe permission checks | `UnsafeBlockRequired`, `UnnecessaryUnsafeBlock` with `Repair.RemoveUnsafe`, one `RequiresUnsafeContext` rule; CFA's `UnsafeFunctionValue` removed if Binding's four sites cover it (reproducer), else moved | Q9 | Comp: `UnnecessaryUnsafeBlockTest`, `UnsafeFunctionValueBindingTest`, `RawPlaceAccessTest`, `RawAddressTest`; snapshots | +130/−140 | 3 | G:L | no |
| Q11 | L3 jump targets and labels | `InvalidJumpTarget`, `UnlabeledYieldTarget`, `OverlappingLabel` at the jump binding (`Binding.Expressions.cs` ≈ 717); the HIR contract "a Jump without a target is reported by Binding" | Q9 | Comp: `ContextualLabelTest`, `ControlFlowRevisionParseTest`, `CurrentControlFlowTest` | +45/−40 | 2 | G:L | no |
| Q12 | L4 FunctionFallthrough in Binding (PD32) | An end-arrival result check beside `AddBodyResult` (`Binding.ControlFlow.cs`); the old derived link reads it | Q9 | Comp: `ControlFlowAnalysisTest`, `UnreachableOwnershipTest`, `DefaultCompletionTest` | +30/−20 | 2 | G:L | no |
| Q13 | One consumed-children enumeration | Extract the children an Op consumes, with their adaptation, from `Binding.Hir.Adjust.cs` (≈ 30 `Take` sites) into one function the HIR publisher calls | Q6 | Comp: `HirColumnsTest`, `MirBuildTest`; bp | ±20 | 3 | R | no |
| Q14 | Spike: TransferRequired parity probe (no commit) | Compare the old analysis's TransferRequired sites with the Binding rule over tests, corpus and milestones | Q13 | Evid (feeds H1) | 0 | 2 | — | no |
| Q15 | Direct source for `ExpansionPlan.Evaluated` | Record the evaluated argument position at `Binding.PropertyCalls.cs:109` and `Binding.Ranges.cs:265`; `ExpansionPlan.cs:59-69` reads it instead of testing `EvaluatedKoto` | — | Comp: `MirBuildTest`, `HirColumnsTest`; bp | +12/−10 | 2 | R | no |
| Q16 | Mir/Build syntax-class decisions answered by existing facts | The 17 sites (`is ContinueKoto` → `HirOp.Continue`, closure → `HirOp.Closure`, `HirOp.Group`, Type kind, `Folded`, signature, symbol kind) plus Q8's 2–3 small Binding views | F0 (W1); OQ10, OQ14 | Comp: `MirBuildTest`, `MirAnalysisTest`; bp | +30/−35 | 2 | D+R (Q8) | W1 |
| Q17 | (Ask A-5) Requirement derivation in shadow | Q8's code → `Ownership.*`/`Binding.*` requirement table in Diagnostics; `MirReport` (R4-owned) disambiguates by facts; the ledger compares the requirement column | Q7, OQ10 | Comp: `MirDifferenceTest` requirement column, `DiagnosticOwnerTest` | +80 | 3 | D+R (Q8), R4's A6 lane | adv |
| Q18 | (Ask A-5) Problem Loan facts; pair-case and Issues readers on MIR facts | A6 publishes the Loan location on `MirProblem`; the 21 rejecting `pair-cases.json` rows, 15 `Ownership.Issues` and 26 `OwnershipFailure` test lines read MIR problem facts | A6's facts | Comp: `GenericOwnershipCaseTest`, `PipelineSwitchTest`; snapshot | +100, t±300 | 3 | D+R (A3 §6 MirProblem fact rows, R4-M35) | no |
| Q19 | Ledger readings at R4 checkpoints (no branch) | After A5 step 1 and after A5-F6 and A6: the ledger summary (R4-M45, R4-M47) mapped to test methods and snapshot rows that would change at the flip; the residual list goes to R4's work list and to H0 | R4 checkpoints | Evid `artifacts/verify/r6-readings-<n>/` | 0 | 1 | — | no |
| Q20 | (Ask A-4) OldRule target sides per A5 family | Target-side assertions added as each family lands (A5P §8) | each A5 family | R4's group reviews | t+150 | 2 | A5 groups | no |

### 4.9 R6-gen (G)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| G0 | R6-gen switch design | Applies S2's switch contract: the default pipeline gets mir semantics plus the collector; `BuildMir` (the whole `MirAnalysis.Run`) in every Analyze; publication at check (OQ5) with DEC entries superseding R3-1, R4-M1 (for these records) and OD10's split; PD12's reporting until H5; rollback for one unit; deletion order in call-graph order | X3, OQ5, OQ6, Q4, A-6, A-8 | Doc | doc | 3 | D+R (S2) | no |
| G1 | Split `Compilation.Pipeline.cs` (behavior-preserving) | By responsibility (switch, records, generation entry), before its fourth change | G0 | Comp: `PipelineSwitchTest`; bp | ±200 | 1 | R | adv |
| G2 | Default pipeline gets mir semantics; instance findings at check | Core: MIR, its driver and the collector in every `Analyze`; `MirRecords` published on the default pipeline per OQ5; the 20 instance-failure methods (including `GenericGenerationDiagnosticTest`'s six `FailedInstance` reads) lose their legacy values; `tests/milestones/stage-baselines.json` and `MilestoneSourcesTest` re-baselined with reasons; DIAGNOSTICS :56, :285, :306; STATUS rows (including PD30's G128 (e) row); `KIMI_PIPELINE=legacy` now means "old acceptance with MIR records" (documented) | G1 | Comp: Session with `'*.ll'`, all milestones, the diagnostic snapshot; the 20 methods and the CLI test; a recorded measurement of default check and warm LSP Analyze time, allocations and retained memory with the full shadow driver (K4's Generic shape 1/32/64/128); the mir-pipeline Allocation tests | Core +30/−20, ±250 t | 4 | G:R6-gen-switch + final review at G11 | adv |
| G3 | Default codegen = MIR | `KIMI_CODEGEN` defaults to `mir`; legacy still selectable for one unit (rollback); `StaticInitializationTest.cs:127` changes with the SPEC 22.4.3 reason; every `.deviation` reduced to its target | G2 | Session + `'*.ll'` + all milestones + snapshot + O2 corpus A/B | ±20, ±150 t | 4 | G:R6-gen-switch | adv |
| G4 | Remove the R5 scaffolding | The selector (ending G3's rollback window; lands only after G3 passed its Session and sweep), the pending-ledger tooling and data, the deviation form and every `legacy:` value, `.deviation` support in E4, coverage-row remnants; DEC and ARCH rows; VERIFICATION rows of OQ9 removed | G3 | Tool + Comp; Session | ≈ −400 | 2 | G:R6-gen-del | adv |
| G5 | Delete Emission-structure tests | 113 `TryPrepare` methods (keeping the native parts of 37), 8 `Validate*Flags` methods, `EmissionOpcode` reads; ratchet rows 119/21/11 → 0 | G4 | Comp | ≈ −2,300 t | 2 | G:R6-gen-del | no |
| G6 | Delete old-plan injection and pairing tests | The 87 injection and 40 pairing reads (PD14); `Ownership.Bodies` → 33 (24 once R4's U10 migrations land) | G4 | Comp | ≈ −1,800 t | 1 | G:R6-gen-del | no |
| G7 | Delete the old lowering, plans, driver and instance machinery | `BodyLowering*` (37 files), `LlvmEmitter*`, `GenericStoragePlan*`, `ObjectGenerationPlan`, `VirtualGenerationPlan`, `DefaultGenerationPlan`, `CompilerFunctionAdapters`, `NumericArithmetic`, `StaticScalarEntry`; `AnalyzeInstance`, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `Resolve`'s instance branch, the `this.instance` branches and `OwnershipModel`'s instance half (with `GenericPointerEmissionTest:94-99`; PLAN G69 closed); the `Binding.ClosureCall` bridge; `EmissionArtifacts`, `Project.Emit` and `TestCommand` re-pointed (the selector and its legacy branch went in G4); the four `src/Benchmark` `.Emission.` readers and the split-IR check | G4, G5, G6, Q4 | Comp: Session + `'*.ll'` | ≈ −13,900 | 2 | G:R6-gen-del | adv while R4 is active |
| G8 | Delete the physical IR layer | `EmissionModule`, the per-instruction serializers and the rest of `LlvmModuleWriter*`, `AggregateLayoutPool`'s destruction half, `FunctionAbiPool`'s `FunctionKoto` keying, the old-path adapters of the P units; `KimiLibraryShapeTest`'s pool use re-pointed; the unit lists what remains in `C/Emission` (toolchain files, `ArtifactPaths`, the `.ll.in` resources) | G7 | Comp: Session + `'*.ll'` | ≈ −3,600 | 2 | G:R6-gen-del | no |
| G9 | Emission-only Binding members and `GenerationFailed_Kd` | `IsInapplicableVirtualBody`, `ImplementationContext`, `IsVerifiedSpecialization`, `PrepareTypeStorage`, `SameModuloOpenOrigins`, the `DictionaryStorageCall` readers (each after a grep over `C/Analysis` and `C/Mir` finds no reference); `GenerationFailed_Kd` replaced by S4's successor for output, native-configuration and backend failures, with a DIAGNOSTICS §4.2 row; "Emission failure strings" ratchet 446 → 0 | G8 | Comp: `DiagnosticContractTest`, `EmissionArtifactsTest`; snapshot | ≈ −400 | 2 | G:R6-gen-del | no |
| G10 | Zero-reference rows | `ArchitectureRulesTest` zero-reference rows for `BodyLowering`, `EmissionModule`, `LlvmModuleWriter`, `LlvmEmitter`, `GenericStoragePlan` over src, tests and `src/Benchmark` (`LegacyRuntimeHelpers` excluded until R8); EmitFixture's constants re-pointed to P0b's records | G9 | Bench: slnx build; `ArchitectureRulesTest` | +10/−10 | 1 | G:R6-gen-del | no |
| G11 | R6-gen exit | STATUS (old-Emission defects and support rows), CODEMAP (Emission → Backend), DIAGNOSTICS, VERIFICATION (fixture policy), ARCH ledger (R6-gen done; Emission freeze ends); area sizes, change hotspots and duplication candidates in `artifacts/benchmarks/`; exceptions and Transitional declarations reviewed with the owner; OQ17's NativeAOT run; the exit-gate evidence (§5.4); final review | G3–G10, OQ17 | Doc + Session | docs | 1 | final review | no |

### 4.10 R6-check (H)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H0 | R6-check design | FP: unit a (MirReport publication, construction and collector records folded into `MirReport`, the published requirement derived from code and facts with Q7's kinds and Q17's table, `PrerequisiteUnavailable_Kd` fallback), the flip (H5, one commit with the effect deletion, PD29), the A4 row checks (H4), the snapshot classification method, the deletion order in call-graph order, the benchmark successors (H9); the one producer of each effect failure under mir between H3 and H5 (A4's effect problems publish from H5, PD29) and of a G128 `Apply` null once `MirReport` absorbs the collector records (PD30); a check-only pass over PLAN10 §11's R6 SPEC/IMPL rows and R4-M15a's IMPL text, any change routed as an explicit revision; OQ12–OQ15 recorded as answered; after OQ11, a DEC entry recording H0 as the owner-approved design for H1–H5 and naming G:R6-check-flip (H1, H5), G:check-del (H7, H9–H12, H14) and G:check-tests (H8a–c). May start during R4 | Q8, Q19 second reading, A6's design (A3 §6) | Doc | doc | 3 | FP | no |
| H1 | TransferRequired judged in Binding | Fail each consumed bare Place with `BareAcquire == Move` and no written acquisition (`BindingFailure.TransferRequired`, existing); delete the 10 old-Analysis report sites; `Ownership.TransferRequired` leaves Q7's enum; ledger rows reclassified; in G:R6-check-flip and merged together with H5 (PD29) | R4's exit, G11, Q13, Q14, Q7, H0, OQ10, OQ11 | Comp: TransferRequired tests, `MirInitTest`, `MirDifferenceTest`; snapshots classified | +100/−70 | 4 | final review of Q8's design; G:R6-check-flip | no |
| H2 | (Conditional) Problem Loan facts | Only if Q18 has not landed through R4's A6 | Q18 not landed | Comp; snapshot | +100 | 3 | D+R (A3 §6) | no |
| H3 | Unit a, part 1: publish MIR problems under mir | `MirReport` publishes the MIR problems other than A4's effect problems (those publish from H5, PD29); construction and collector records fold into it (R4-M3a); the derived published requirement; `PrerequisiteUnavailable_Kd` fallback (PLAN10 P10); default acceptance still old | R4's exit, G11, Q17, H0, OQ11 | Comp; snapshot under mir | ±200 | 3 | D+R (H0) | no |
| H3b | Flip rehearsal and classification of the remaining ledger rows | A branch applying H1, H4 and H5 as H0 designs them: Session, milestones, diagnostic snapshot; classify the non-blocking ledger rows R4's exit left (location and code choice, MIR-stricter rows outside the library and milestones) with clauses | H3 | Evid + classification table (Doc) | 0 | 3 | R | no |
| H4 | A4-based verification of published rows | The successor of `SummarizePublishedOperation` (R4-M28); the Array `append`/`insert`/`pop`/`remove` catalog rows; its records publish from H5 with A4's effect problems (PD29) | H3, OQ11 | Comp: `MirEffectsTest`, `EffectBoundImplementationTest` rewritten | +60/−40 | 3 | D+R (H0) | no |
| H5 | Acceptance flip with the old effect checks deleted (one commit) | `Compilation.Analyze`/`Analysis`/`Problems`/`ReportAnalysis` and `Project.cs:510` decide by MIR; delete `EffectSummary` (≈ 1,985), `Binding.CollectCaseDestructions` and its callers (`OwnershipAnalysis.Cases.cs:141, 156`), the Callable/Virtual checking parts, `ValidateEffectBounds`, `IsEffectRecord` with the ledger's effect filter (`MirDifferenceLedger.cs:44, 279`), the violation-table reads in `EffectObligations` (R4-M23), the R4-M28 twins and `CompilerFunctionEffect.Formatting`, `OwnershipAnalysis.EffectBounds.cs`, `OwnershipBody.CleanupEffects.cs`; `VerifiedSpecExampleTest` MIR-only; OldRule traits (19 methods), the 22 spec-cases and 1 pair-cases Old sides deleted; `stage-baselines.json` and `MilestoneSourcesTest` re-baselined; STATUS's pipeline-lag line removed; PD12's generation-time refusal path and its typed record removed (A1/A2 problems now reject at check through `MirReport`; the `InternalInvariant_Kd` check for `Group == -1` in an analysed body stays). From H5, `KIMI_PIPELINE` no longer selects old acceptance (until H12 it only gates the ledger harness); the rollback is a revert of the whole G:R6-check-flip group while the old Analysis exists | H1, H3b, H4, Q9–Q12, Q20, Q18 or H2, A-7 | Comp: Session + milestones + `'*.ll'` + snapshot classified (G-Corpus) | ±300, ≈ −2,700 | 4 | FP (H0) + final review at H15; G:R6-check-flip | no |
| H6 | Hover "Ownership checks" line from MIR problems | `HoverBuilder.Virtual.cs:75`; HOVER row | H5 | Comp: Hover tests | ±20 | 1 | R | no |
| H7 | Remove Binding's i128 definition gate | `Binding.Expressions.cs:1382-1386` (A3b owns it) | H5, K9a | Comp: i128 tests, A3b tests; snapshot | −6 | 1 | G:check-del | no |
| H8a | Old-type test reads: Bodies residue | The remaining `Bodies` reads and the 3 `Body(c)` helpers (28 calls) → dump goldens or deletion | H5, Q18 or H2 | Comp | ≈ −900 t | 2 | G:check-tests | no |
| H8b | Old-type test reads: control-flow entries | 64 `AnalyzeControlFlow`/`ControlFlowAnalysis.Analyze` calls (16 files) → lint observations | H5, Q9–Q12 | Comp; ratchet 64 → 0 | ≈ −700 t | 2 | G:check-tests | no |
| H8c | Old-type test reads: kind enums | `OwnershipOperationKind` and other old kind enums (114 methods, 52 files) | H5 | Comp | ≈ −1,000 t | 2 | G:check-tests | no |
| H9 | Benchmark readers at R6-check | The 12 `src/Benchmark` files reading the old Analysis: precondition reads → `Compilation.Analyze()`; benchmarks of old-only storage (`BorrowStorageMeasurements`, `OwnershipAnalysisBenchmark`, `MatchOwnershipBenchmark`, the `BorrowStorageBytes`/`PeakLocalLoanCells` columns, `PatternBindingBenchmark`'s control-flow reads) deleted with their `.md` series and `Program.cs` registrations (listed in H0 for the owner); stage 4 redefined as MIR A3–A6 under the same name (PD26), which §5.5's PLAN10-P3 comparison uses | H5 | Bench: slnx build, `--compiler-pipeline-check` | ≈ −1,200 | 2 | G:check-del | no |
| H10 | Delete the old Analysis | `OwnershipAnalysis*`, `OwnershipBody*`, `OwnershipModel`, `OwnershipFlow`, `BorrowLiveness`, `PackedAnalysisTable`, `OwnershipStorage*`, `InterpretationContext` and ControlFlowAnalysis's remainder (flow, targets, `PendingBinding`, structural `RequireFallthrough`); `Compilation.Ownership`, `Compilation.AnalyzeControlFlow`, `ReportAnalysis`'s `Ownership.ControlFlow` read (`Compilation.Pipeline.cs:100`); the `ElementAccess` `OwnershipBody` overloads; `VerifiedSpecExampleTest`'s record split moved into the test, then the ledger harness (`MirDifferenceLedger`, `LegacyRecords`, `MirDifferenceTest`, `Compilation.Analyzed` (superseding R4-M17's "unit a" timing), `KIMI_MIR_DIFF`); `SemanticSnapshotTest`'s LegacyRecords use; zero-reference rows for `OwnershipAnalysis`, `OwnershipBody`, `OwnershipModel`, `ControlFlowAnalysis` over src, tests and `src/Benchmark`; the `.Ownership.Issues`, `.Ownership.ControlFlow` and `.Ownership.Bodies` limits → 0 | H8a–c, H9 | Comp: Session with milestones | ≈ −16,200 | 2 | G:check-del | no |
| H11 | Delete old-Analysis-only Binding members | ≈ 22 methods (including `PropertyUpdateStorage`; after checking `CollectWitnesses`' library reach on MIR), `MatchTypes`, `ElementAccess.IsIndexerCall`/`DesignatedType` (PR-1), A3's gates (`Binding.Diagnostics.cs:985`, `Binding.TypeOrigins.cs:182-192`), `DeferredExpansionLimit_Kd`, `StaticScalar.cs`, the call-keyed helpers (`DestructionCall`, `TryResolveFormattingCallback`, the CallPlan overload of `SelectSpecialization`, `InstantiateForwardedCall` with its `TupleOperator` copy (`Binding.Specializations.cs:162`) and the tests calling it retargeted (F5b's twin, `TyCtxtTest.cs:245`, `VirtualCallSelectionTest.cs:87, 91`)); `CallPlan.TupleOperator`, `ComparisonPlan(CallPlan)` and `InstantiateRequirementCall` with its copies (`Binding.RequirementInstances.cs:63, 78`) stay until R7 (Q8): it has bind-time callers (`Binding.SelectedRequirements.cs:43`, `Binding.RequirementItems.cs:131`) and its result is the published `BoundCall` whose `TupleOperator` F7 reads; `BoundMatch`/`BoundClosure` renamed or their R2b-D5 deferral recorded | H10 | Comp; snapshot | ≈ −700 | 2 | G:check-del | no |
| H12 | Remove the pipeline switch | `CompilerPipeline` and its 6 declarations, `KIMI_PIPELINE`, the `[mir]` identity, `TestCatalog.cs:108`, `PipelineSwitchTest`, `verify.ps1 -Pipeline`, VERIFICATION:93-95 | H10 | Tool + Comp | ≈ −300 | 1 | G:check-del | no |
| H13 | Mir/Build syntax-class decisions | Per OQ14: the ARCH amendment of :135 and :149 (no code) for the 63 sites, or the HIR vocabulary (FP) | Q16, OQ14 | Doc (or Comp) | 0 (or +70/−45) | 2 | R (FP if vocabulary) | no |
| H14 | Forwarding properties and synthetic Koto | Per OQ12/OQ13: Parsing and root-helper reads rewritten with their zero-reference rows; Binding's reads, the 36 properties, the synthetic Kotos and ARCH:163 stay `[Transitional("R7")]`/postponed with the owner's approval in the commit | H10, OQ12, OQ13 | Comp per group; snapshots equal | ≈ −120 | 2 | G:check-del | no |
| H15 | R6-check exit | STATUS (pipeline-lag line, D2 regressions recovered or recorded (e)), PLAN (milestone order resumes), CODEMAP (≈ 65 lines naming old components), DIAGNOSTICS :106-166, `tests/milestones/README`, ARCH ledger and freeze end, OQ15's SETTLED outcome; area sizes, change hotspots and duplication candidates; exceptions and Transitional declarations reviewed with the owner; exit-gate evidence (§5.5); final review | H6–H14, OQ15 | Doc + Session | docs | 1 | final review | no |

---

## 5. Evidence, metrics, stop rules and exit gates

### 5.1 Metrics

Every unit's commit states the metrics it moved, and every working session's evidence reports them.

| ID | Metric | Moves with | Direction |
| --- | --- | --- | --- |
| M1 | Pending ledger: failing cases and fixtures under sel ("passes now" apart from "no longer executed") | every C unit, K13a–c | falls only; 0 at R5's exit |
| M2 | Coverage rows implemented of the total (MIR forms, 33 intrinsic rows, 75 kinds, mono items); classes passing under sel | C units | rises |
| M3 | S1 audit rows closed | every R5 unit naming a row | rises |
| M4 | Emission lines remaining / Backend lines; bp on each P unit | P units, then G7–G9 | Emission falls |
| M5 | Vacuous instrumentation sites (60 zero-expect audits, 33 negative names, 23 ad-hoc rewrites) | E2, E3 | 0 before C4s |
| M6 | Collector edge forms handled (of 12); collector records over the library and the 40 harnessed milestones (must stay 0) | K units | rises / 0 |
| M7 | Deviations (`.deviation` count), each with a decided item | E12, K13a–c, C3b, C5a, C7 | small, decided |
| M8 | Ratchets: `TryPrepare` reads 119, generation failure facts 21, instance reads 11, Emission failure strings 446, `Ownership.Bodies` 183 | K12, G5–G9 | fall |
| M9 | Warm generation allocation sites passing under sel: 183 at 0 B and 3 at their bound | every C family | rises to 186 |
| M10 | O2: per-family kernel B/A within variance; per-function O2 instruction growth | C1a on, E13 | within variance |
| M11 | Old Analysis lines; `[Transitional("R6")]` declarations (44) | Q and H units | fall |
| M12 | R6-check readers: OldRule traits 19, spec-cases Old 22, pair-cases Old 1, `Ownership.Issues` 15, control-flow entry calls 64, old kind-enum methods 114, Mir/Build syntax-class sites 80 | Q16–Q20, H units | 0 |

Before C0b2 exists M1 cannot move, so the first sessions move M3–M6.

### 5.2 Stop rules

- **R5 (OQ9).** The owner is consulted after two consecutive working sessions on R5 that land no unit. A unit lands when it
  adds a planned deliverable with its first consumer, completes a promotion with bp, or lowers M1 by passing tests or
  fixtures. A planning- or record-only session lands nothing.
- **Unit timebox.** A unit not landed within two working sessions stops; it is re-split or becomes an owner question.
- **Metric rule.** From C0b2, if M1 does not fall across three consecutive C units (counting only the rows each adds), the
  session states why; a missing input becomes a fact or builder unit, never a workaround.
- **Review rule (AGENTS).** Two review rounds on one family of findings go to the owner.
- **O2 rule.** A kernel regression beyond the A=B variance blocks that family's merge; the fix is in codegen, never a
  tolerance.
- **R6-gen and R6-check.** The same two-session rule. G3's rollback is the legacy selector for one unit; H5's rollback is a
  revert of the whole G:R6-check-flip group while the old Analysis exists. A switch unit that shows a regression in its
  Session or sweep is reverted on its branch, not patched on dev.

### 5.3 R5 exit gate (recorded in the ARCH ledger by S5)

1. M1 = 0 on a sel Session with `'*.ll'` at O0/O2, the 40 harnessed milestones and the five CLI harnesses; X1's failure set
   equals exactly the methods G2 and G3 rewrite.
2. Every native outcome equals the legacy run's, except `.deviation`s decided under OQ7, each with item and clause.
3. No coverage-row gap; every remaining row is a decided D2 (e) row in STATUS and PLAN, approved as one list (PD27).
4. Fail-closed audit counts equal legacy's, or lower where a deviation is decided; M5 = 0.
5. O2 corpus: every kernel B/A within variance against A = `49cb2b5d` (PLAN10 P1, P4); O0 times and per-function O2 metrics
   recorded.
6. LLVM verifier green; MIR `.ll` deterministic and stable across R5's restructuring commits.
7. Legacy `.ll` unchanged by every R5 unit except declared behavior changes (C7's reason row).
8. M9 = 186 (183 at 0 B, 3 at their bound).
9. Zero collector and MIR records over the library and the 40 harnessed milestones under sel; the rejected milestones 39,
   41, 42 and 43 keep their recorded first diagnostic.
10. Compile-time record: collection (stage 3 share) + layout + codegen against the legacy EmissionLowering + LlvmIrWriting
    (PLAN10-P3), and the check-time cost of collection.
11. Records: ARCH R5 done with area sizes, hotspots and duplication candidates; exceptions and Transitional declarations
    reviewed with the owner; every R5 group review passed; the end-to-end codegen review passed.

### 5.4 R6-gen exit gate (new; R4-M35 left none)

1. Session green with `'*.ll'` at O0/O2, all milestone harnesses and the CLI harnesses, on MIR codegen by default.
2. Diagnostic snapshot differences classified (OQ5's records now at check); the 20 instance-failure methods assert
   check-time records; `stage-baselines.json` re-baselined with reasons.
3. O2 corpus within variance; audit counts unchanged or lower; warm allocation tests pass.
4. Zero-reference rows for the Emission types over src, tests and `src/Benchmark` (`LegacyRuntimeHelpers` excluded until
   R8); M8's Emission rows at 0.
5. No `[Transitional("R6-gen")]` declaration remains; `GenerationFailed_Kd` replaced; `AnalyzeInstance` deleted (OQ6 (e)) or
   R4 has exited (OQ6 (d)).
6. PLAN10-P3 records: "BuildMir + collection + codegen" against the old Analyze + Emission, and G2's default check and warm LSP
   Analyze measurement.
7. OQ17's NativeAOT run; records and the R6-gen final review.

### 5.5 R6-check exit gate (new)

1. MIR decides acceptance; Session green with milestones and fixtures; diagnostic snapshot differences all classified
   (a)–(e) with clause; `VerifiedSpecExampleTest` MIR-only; M12's OldRule, spec-case and pair-case rows at 0.
2. Zero-reference rows for `OwnershipAnalysis`, `OwnershipBody`, `OwnershipModel`, `ControlFlowAnalysis` over src, tests
   and `src/Benchmark` (and the forwarding properties if OQ12 chooses R6-check); Ownership ratchets at 0.
3. No `[Transitional("R6")]` or `("R6-check")` declaration remains, except owner-approved R7 postponements recorded in their
   commit.
4. `KIMI_PIPELINE`, `CompilerPipeline`, `-Pipeline` and the `[mir]` mark removed.
5. MIR problems published through `MirReport`; TransferRequired in Binding; effect checks only A4's.
6. ARCH:135 and :149 hold for Mir/Build, or the owner amended both (OQ14).
7. PLAN10-P3 record "BuildMir + collection" (stage 4 redefined, H9) against the deleted analyses; Bind/Analyze warm zero
   allocation holds.
8. Records (STATUS, CODEMAP, DIAGNOSTICS, ARCH ledger and freeze end, area sizes, hotspots, duplication candidates, the
   owner review of exceptions and Transitional declarations) and the R6-check final review.

### 5.6 Evidence cadence (OQ9)

| Evidence | Cost (H) | When |
| --- | --- | --- |
| bp comparison | seconds after each Session | every P, F, K and Q unit touching legacy-observable code |
| Unit run of the family classes under sel and default | about twice a Unit run | every C unit |
| Sel Session with the pending check | ≈ 3 min | the end of every R5 working session, beside the default Session |
| Full cross-pipeline sweep: `'*.ll'` × 2 + compare + 40 milestones × 2 | 25–40 min on a dedicated machine (R0 swept 6,252 fixtures in ≈ 8.7 min wall) | at each family group review, X1, G3 |
| O2 corpus A/B | ≈ 10 min per kernel set on a quiet machine (other lanes pause) | each family review touching a kernel; X1; G3 |

---

## 6. Volume and difficulty

### 6.1 Totals (H)

| Stage | Units | Compiler lines (+/−) | Tests (+/−) |
| --- | ---: | --- | --- |
| R5 (S, E, P, F, K, C, X) | 103 (+1 conditional) | ≈ +9,300 / −3,150: codegen ≈ +4,200, collector ≈ +1,100, promoted code ≈ +3,450 moved in against −3,150 moved out, facts ≈ +550; net ≈ +6,150; tooling ≈ +900 | ≈ +4,500 / −300 |
| Early R6 prerequisites (Q) | 20 (4 R4 asks, 3 probes or readings) | ≈ +650 / −550 | ≈ +500 |
| R6-gen (G) | 12 | ≈ −18,300 (Emission ≈ −17,300 after the promotions, with the toolchain kept until R9; instance machinery −200; Binding −400; scaffolding −400) | ≈ −4,500 |
| R6-check (H) | 18 (+1 conditional) | ≈ −21,000 net (≈ −21,300 removed: Analysis ≈ −16,200, effect checks −2,700, Benchmark −1,200, Binding −700, switch −300; ≈ +300 added for TransferRequired, publication and A4 row checks) | ≈ −3,300 / +700 |
| **Total** | **153 (+2 conditional)** | **net ≈ −33,000** | **net ≈ −2,400** |

The count is high because units are small: about two thirds are difficulty 1–2 moves, deletions, rows or records, and group
reviews keep review cost per family rather than per unit.

### 6.2 Difficulty distribution

| Stage | 1 | 2 | 3 | 4 | 5 |
| --- | ---: | ---: | ---: | ---: | ---: |
| R5 | 20 | 47 | 36 | 0 | 0 |
| Early R6 | 5 | 7 | 8 | 0 | 0 |
| R6-gen | 4 | 5 | 1 | 2 | 0 |
| R6-check | 4 | 8 | 4 | 2 | 0 |
| **Total** | **33** | **67** | **49** | **4** | **0** |

Every deep-dive unit rated 4 was split, spiked, decided or deferred, except the switch units (G2, G3, H1, H5), whose risk is
inherent and is handled by rehearsal (X1, H3b), rollback and a final review.

### 6.3 Full-process designs

Three double-design rounds: the R5 package S2–S4 (one design review over three chapters), the R6-check prerequisite package
Q8 and the R6-check design H0; plus final reviews at the R6-gen and R6-check exits. A conditional fourth if OQ14 keeps ARCH:149
and chooses a vocabulary. Read literally, the inputs implied about 15.

### 6.4 Parts that are too large or too hard, ranked, with responses

| Rank | Part | Why | Response | Owner action |
| ---: | --- | --- | --- | --- |
| 1 | Starting cost of R5 | A5P §9.1 requires two measured codegen designs; every cross-phase unit pays the full process (about 15 double designs); PRE5 §2 still gates R5 code | One package (S2–S4) through the full process, then DEC Q6 forms 1 and 2; the deep dives as first designs; the A spike waived; three decision packets | OQ2, OQ3, OQ4 |
| 2 | `AnalyzeInstance` between R6-gen and R6-check (OD10) | As written OD10 is not implementable cheaply: "check-only" is wrong (check never runs it) and its only callers are deleted at R6-gen; a bridge would be a difficulty-4 transitional structure; R6-gen then also waits for A5 step 1 | Close the two holes it covers in the old definition run (Q1–Q3, about 20 lines), supersede the clause (Q4, before G0), delete it with its callers (G7) | OQ6 |
| 3 | Instance collection before acceptance (OD4) | A new canonical component and a phase move that changes what `check` reports; a Library with an unbuildable generic instance fails check; check cost rises on every edit; G128 makes one finite program the old path accepts unbuildable | K0–K13c; K0 measures cost and reproduces G128; PD30 defers G128 to R7 with an (e) row under R4-M51's code | OQ5 |
| 4 | The default pipeline between G2 and R6-check | Every Analyze, LSP included, runs the shadow MIR driver and the collector beside the old analyses for R4's remainder; a valid program can hit a PD12 refusal (A1/A2 problem, arena skip) that the old path accepts | PD12's typed refusal (never an internal error); G2 measures time, allocations and retained memory; X1 rehearses G2+G3 exactly | OQ5 |
| 5 | Code generation volume | ≈ 4,200 new lines in 30 slices; R4-style stall risk | Promotions first (PD2), coverage rows and per-group pending files move per slice, family groups, the escape valve PD27 | OQ9 |
| 6 | Drop correctness | Exactly-once release across Keep/Flag/Open per view, destructors that already drop their fields, fail-open audits hiding double frees | E2 first; C4s's expected outcomes as tests; C4a–C4c by verdict | — |
| 7 | Requirement witness by Subst | Six resolution cases plus the OCC-X refusal must match the call-keyed path, or native code runs the wrong body | F5a (extraction, bp proves identity) then F5b (twin test over the corpus) | — |
| 8 | Evidence machinery | ≈ 450 IR-assertion rows, 241 fail-open audits, 186 warm-allocation sites, ≈ 6,000 fixtures × 2 generators; a verification-policy change | Native behavior only; E1 limited to what E2 cannot fix; positive assertions per family; EmitFixture built-ins kept (PD8); sweeps only at group reviews | OQ9, OQ16 |
| 9 | Contention with R4 | R4 holds `MirBuilder`, `MirValidator`, `MirTypes` and the goldens and edits the driver next; `TyCtxt` and `Compilation.Pipeline.cs` are outside the protocol; after G3, R4's MIR units change default code | No R5 edit of the driver; W1 at max(MIR-lane merge, S5); PD20 hold lines; ask A-10 | OQ8 |
| 10 | R6-check's unowned prerequisites | The 13 ControlFlowAnalysis codes, the `OwnershipFailure` enum and TransferRequired had no owner or design; TransferRequired is soundness-critical (MIR accepts a bare move of a non-Copy local and the ledger cannot see it) | Q8 early; lint group Q9–Q12; Q7; Q13–Q14 early; H1 merged with the flip (PD29) | OQ10 |
| 11 | The two switches | R6-gen makes MIR mandatory under the old acceptance; R6-check changes the producer of every Ownership record, and the old effect checks would duplicate A4's records | X1 and H3b rehearsals; rollback; the flip and the effect deletion in one commit (PD29); H0 off the post-R4 path | OQ11 |
| 12 | Deletion order | The old Emission and Analysis are deleted while the solution (including `src/Benchmark`) must build after every unit | Deletions follow the call graph (the instance machinery goes in G7 with its only callers, after G4 removed the selector; H9 before H10; H5 deletes `CollectCaseDestructions`); Benchmark readers move with their producers | — |
| 13 | Old effect checks | ≈ −2,700 lines that also implement `preserves results` and delegation; need A5-F6 and A5P-S2 | Removed in H5 after R4's exit | A5P-S2, via A-7 |
| 14 | Forwarding-property reads and synthetic Koto | ≈ 1,000 Binding reads in 119 files with no speed gain; synthetic Koto needs R7's syntax-free selection; ARCH:163 depends on them | Defer to R7 | OQ12, OQ13 |
| 15 | Mir/Build syntax-class decisions | 80 sites under two ARCH rules (:135, :149); 63 are child casts or operator identities | Amend both rules; fix the 17 real ones in W1 | OQ14 |
| 16 | Formatting family | 25 Body kinds, ErasedWriter, interpolation estimates, the 1,024-byte stack rule, O2 and allocation counts | Split C9a–C9c over verbatim `LegacyRuntimeHelpers` rows | — |
| 17 | A SETTLED conflict | PLAN10 §11 rewrites "Instance-evaluated generic defaults" at R6; OD9's non-adoption says it stays | Clarify before H15 | OQ15 |

### 6.5 Calendar risks

1. Owner turnaround on packet 1 (S5 and all R5 code wait for it).
2. R4's remainder, the likely long pole; A5 step 1 also gates R6-gen (through Q2); A5-F6 needs A5P-S2.
3. W1 waits for the R4 MIR lane's group review; `claude/a5-1b-mir`'s merge gates K7 and C4a; G103-E9 gates G0.
4. Machine time: full sweeps and O2 A/B runs need a dedicated quiet machine at each family review.
5. Group reviews hold up to eight units on a branch; a slow review stalls a family.

---

## 7. Findings to record on adoption

Facts found while planning that need a PLAN issue or a correction of a record (confirmed by reading; (H) where noted):

| Finding | Where | Proposed owner |
| --- | --- | --- |
| The MIR builder drops the tuple-operator mode: a tuple comparison becomes a plain call and relation, so MIR codegen would give IEEE-wrong results for NaN and ±0, and A1–A5 see a different shape from the published plan | `MirBuilder.Calls.cs:14-36, 118-124`; legacy `LlvmModuleWriter.BuiltinComparisons.cs:106-124` | F7 (W1) |
| MIR accepts a bare move of a non-Copy local (A1 checks only static Move Paths); TransferRequired is reported only by the old analysis and passed through the ledger uncompared, so the gap is invisible until R6-check | `MirInit.cs:150-175`; R4-M4, R4-M17 | H1 (with the flip) |
| A body A1 skips at the arena limit has no A2 rows (`Group == -1`) and its `OwnershipStorageLimit_Kd` stays unpublished, while the old path's differently shaped arena may accept it | `MirInit.cs:55-59`, `MirAnalysis.cs:79-82`, R4-M15a, R4-M6a | PD12, OQ5 |
| The pre-R5 O2 baseline evidence cited by PLAN §3 does not exist in the main checkout or any worktree | `artifacts/benchmarks/` | E6 |
| `NativeAllocationAudit` fails open: an unrewritten `call ptr @HeapAlloc(` or a missing transfer anchor is skipped, so 60 zero-expect audits would pass vacuously | `tests/xUnitTest/NativeAllocationAudit.cs` | E2 |
| `Binding.closureContexts` is never cleared, so its linear scan grows across passes and (H) retains earlier passes' syntax | `Binding.ClosureInstances.cs`, `Binding.cs:198` | K2 |
| The R0 disposition sends `Binding.ClosureInstances` and `RequirementInstances` to "delete R6", but both have definition-time Binding callers | `artifacts/verify/reduction-R0/disposition.md` | Q8 (re-disposition to R7) |
| A5P §9.1 calls `AnalyzeInstance` a "check-only step"; it never runs in `check` | A5P line 877, `Project.cs:502-515` | Q4 |
| PLAN §3 expects R6 to recover the R2a Bind cost by deleting the forwarding properties; they already forward to `HirTables`, so (H) no recovery should be expected | PLAN §3 "Carried forward" | Q8 record |
| PLAN G128's deep finite Subst, accepted by Binding and the old path, becomes unbuildable under MIR generation (R4-M51 already makes it `Unsupported_Kd` in A4/A5) | PLAN G128, R4-M51 | PD30 |
| `HoverBuilder.cs` is at its token limit (H) and `MirBuilder.Expansions.cs` near it | the files | K2, any builder change |
