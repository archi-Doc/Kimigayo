# Compiler design decisions

Decisions about the compiler's structure that SPEC/IMPL (language and implementation contracts) and
[SETTLED](../SETTLED.md) (rejected language changes) do not record: new representations, components and vocabulary
entries, responsibilities moved between phases, temporary structures and working-process decisions. The reduction
[plan](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md) §18 holds the
decisions of its adoption; this log holds the later ones. [COMPILER_ARCHITECTURE](COMPILER_ARCHITECTURE.md) states the
rules that result from them.

- An entry gives its id, date and decider, the decision, the options rejected and why, and the stage that removes a
  temporary structure. It is added in the commit that first implements it. A superseded entry stays and names its
  replacement.
- This log is the authority. Planning and handoff files under `artifacts/` (ignored by Git) cite entries by id instead of
  restating them; a decision recorded only there is not in force.
- Open questions belong in PLAN until decided.

## Working process

- **Q1 Quality mechanisms** (2026-10-12, owner).
  - Adopted: the size budget counts logical size, non-trivia C# tokens (Code Rules Analyzer A1, before R4); restructuring
    and behavior changes are separate commits (AGENTS, [VERIFICATION](VERIFICATION.md#behavior-preserving-commits)); design
    decisions are taken in the design step and recorded here (AGENTS); context stays small: from Code Rules Analyzer A1 each canonical
    component's entry file begins with its contract, and documents keep current rules and positions in short rows.
  - Deferred: metamorphic tests and fuzzing for shape-dependent code; consolidating tests into data-driven corpora;
    per-phase performance budgets; per-session goal metrics, the sequencing of R7 and R8 with milestone work, and lighter
    commit records.
- **Q2 Code Rules Analyzer** (2026-10-11, owner). The
  [proposal](../../draft/Proposals/2026-10-11%20Code%20Rules%20Analyzer.md) §9: rules KR1xxx–KR3xxx and the built-in unused-code
  rules as build errors over a shrinking baseline, units A0–A8 after R3 and before R4. The limits are in
  COMPILER_ARCHITECTURE (structure limits).
- **Q3 Compiler allocations** (2026-10-11, owner, AGENTS `8071549f`). An allocation in the compiler is allowed when it
  makes equivalent work faster or reduces compiler code with a reasonable increase in memory, measured and recorded.
  Supersedes plan §18 P11 and §14-8. Native allocation counts and O2 run time may not regress.

## R2b (2026-10-11, main session, as recommended)

- **R2b-D1** `BoundType` stays the Type representation until R7; `TyCtxt` owns interning and `Subst` converts.
- **R2b-D2** The HIR publisher runs lazily, at most once per Binding pass. R7 writes the columns inline and deletes it.
- **R2b-D3** The declaration-table views (`ImplTable`, `VTableDef`, `UniversalRelations`, `EffectBoundTable`,
  `AvailableBounds`) are built by their first reader (R4, R5).
- **R2b-D5** `BoundMatch` and `BoundClosure` keep their names (the plan's PatternPlan and ClosureDef) until R6.
- **R2b-D7** `HirAdjust` = `HirAcquire` + `HirAdaptation`: `@move` and `@copy` are operand Adjusts, `@ref`, `@uniq` and
  `@raw` are `Op = Borrow`, `@follow` is `Deref`, and numeric conversions are `Op = Convert`.
- **R2b-D9** The HIR snapshot is a separate `.hir.txt` per case (`KIMI_SEMANTIC_HIR=1`).
- D4, D6, D8, D10 and D11 set R2b's budget, published views and unit order; their results are in the stage ledger and
  PLAN_HISTORY.

## R3 (2026-10-11, main session as recommended; R3-9 and R3-10 by the owner)

- **R3-1** MIR records (construction `Unsupported_Kd`, validator `InternalInvariant_Kd`) are published only under the
  `mir` pipeline, in the ControlFlow partition.
- **R3-2** The R3-exit `-Pipeline mir` Session is a measurement, classified into `artifacts/verify/mir-diff/r3.tsv`; the
  gate is the plan's R3 gate.
- **R3-3** Tests read analysis and generation through the `Compilation` entry points. `TryPrepare(out var module)` reads
  wait for R5, and `Ownership.Bodies` and control-flow structure reads for the R4 dump goldens, under ratchets.
- **R3-4** R3 budget: Mir 6,600, Binding 350 lines over its R2b-exit size, Core 1,820, the old paths unchanged (D5).
- **R3-5** A built-in form without an intrinsic row gets an interim row mapping it to an existing compiler function or
  legacy helper, each with its R8 disposition.
- **R3-6** `MirDefKey` is keyed by `BindingSymbol` until R7's declaration table.
- **R3-7** `Problem(Code, SourceRef Subject)` stays minimal until R6.
- **R3-8** Default expansion stops at 65,536 MIR statements per expanded body with the Resource code
  `DefaultExpansionLimit_Kd` (IMPL §21.3.5, plan §16-10).
- **R3-9** (owner) An Array's or Dictionary's `capacity` is the temporary `IntrinsicId.Capacity`
  (`Call(Intrinsic(Capacity), [&handle])`). R5 reads the field under the current layout rules; R8 removes it with the
  representation structs. Rejected: deferring to R5 (the same choice later, plus an R3 gate exception); pulling R8's
  representation structs forward.
- **R3-10** (owner) An owning interpolation uses the temporary intrinsics `FormatBuffer` (named `FormatCapacity` until
  U16), `FormatHint` and `FormatFinish`. `FormatBuffer` creates the private buffer, where R5 chooses stack or heap;
  `FormatFinish` consumes the adapter. The estimates take the literal byte count and a TypeToken of the values' Types and
  are computed per instance in R5; the failure check reads each write's own Result. Rejected: internal library
  declarations with three more `CompilerFunctionKind` members (grows the vocabulary R8 shrinks); deferring with an R3
  gate exception. R8 removes all three; with R3-9 the `IntrinsicId` limit returns from 34 to 30.

## R4 (accepted 2026-10-11; R4 has not started)

- **R4-M1** Shadow: under `mir` the old path still decides acceptance, Problems and diagnostics. The analyses run once
  per program version after build and validation (`MirBuilder.Build`, then `MirAnalysis.Run`); MIR problems stay
  unpublished on `MirProgram` until R6 unit a; an analysis exception fails the test. Rejected: MIR deciding codes in R4
  (duplicate records and test churn twice).
- **R4-M2** Layout: `Mir/Analysis/` in namespace `Kimi.Compiler.Mir`: `MirAnalysis` (driver), `MirDataflow`,
  `MirMoveData`, `MirInit` (A1), `MirDrops` (A2), `MirLints` (A3), `MirEffects` (A4), `MirBorrows` with `.Regions`,
  `.Conflicts` and `.Origins` (A5), `MirReport` (A6). The first unit creates all of them as stubs, so parallel units never
  edit the driver.
- **R4-M3** `MirProblem(Code, Subject, Related, Requirement, Views, Fact)` with pooled fact rows; one `MirReport.Add`
  applies the derived rule (R4-M10). Core's `Problem(Code, Subject)` is unchanged until R6.
- **R4-M4** The first unit fixes the compared code set of each analysis: A1 initialization and moves; A3
  `RequireFallthrough`; A4 effect bounds and the effect-limit Unsupported; A5 Loan conflicts, default-argument access,
  Origin relations and `OwnershipStorageLimit_Kd`; every old Ownership-partition `Unsupported_Kd`. Control-flow lints,
  `TransferRequired`, `OwnershipCaseLimit`, old Internal records and capability Unsupported records pass through
  uncompared.
- **R4-M5** Compilations with an incomplete Binding are compared in their own ledger section. A new-only row there is (d)
  only if A6 shows no dependence on an Opaque site's roots; otherwise it is (b).
- **R4-M6** A2 writes a side table and does not rewrite MIR: a verdict per Drop and view class (Keep, Elide, Flag, Open)
  and the flag set and clear points; R5 code generation reads it. Rejected: rewriting MIR in place (verdicts differ per
  view, and insertions break ranges).
- **R4-M7** A3's missing fact is `MirBody.MustNotComplete(block, SourceRef)`, written by the builder and range-checked by
  the validator; it is not a vocabulary member. Rejected: inferring it from Unreachable (SPEC 14.9.2 forbids pruning).
- **R4-M8** The definition-stage capability check (A3b) moves to R5 with the single capability table; the R4 ledger
  excludes capability records.
- **R4-M9** Binding views for R4, each added to `PublishedBindingTypes` with its reason: `TyCtxt.Relate` (the single
  variance walk), `OriginAtom.Of`, `UniversalRelations.For`, `OriginClauses.Of`, `EffectBoundTable.Of`,
  `ImplTable.Witnesses`, `VTableDef.Of` and `TyCtxt.CallableBounds`. Rejected: Mir re-deriving Origins from syntax or
  scopes.
- **R4-M10** A6: a problem rooted at an Opaque site is derived and dropped; an effect caused by one is Unknown with that
  cause, and obligations on it are derived. A problem inside an Inlined scope belongs to the template's own check,
  except Loan conflicts with the caller and effect contributions. A finding rooted at a PreparedInput local maps to
  `DefaultArgument*_Kd`.
- **R4-M11** A1, A4's local facts and A5 run per `MirCases` view; A6 merges by (code, subject, Reason) with the view mask
  as the case fact. Views are never collapsed.
- **R4-M12** Library bodies are analyzed in every compilation. The library and the milestones produce no problems,
  except an (a) row whose library fix lands in the same unit.
- **R4-M13** Tests migrate per method kind (body presence, operations, cleanup verdicts, Loans, verification state, R5/R6
  residue). Ratchets: control-flow structure reads at most 1, `Ownership.Bodies` reads at most 110.
- **R4-M15** `OwnershipStorageLimit_Kd` stays the one Resource code, decided by the byte size of A5's Loan × point arena.
- **R4-M16** The ledger's classification table is tracked (`tests/xUnitTest/TestData/mir-diff/classification.tsv`); raw
  rows, reproducers and ledgers are evidence under `artifacts/verify/mir-diff/`.
- **Owner decisions, only when triggered:** O1 a MIR vocabulary member or `IntrinsicId`; O2 a total cap above 142,000;
  O3 an acceptance change outside plan Appendix B (C1–C6 and X1–X3 with a SPEC clause; T1–T4 belong to R7), any rejection
  of a milestone or library program, or a row reopening a SETTLED entry; O4 a general SPEC rule left Unsupported at the R4
  exit; O5 the unclassified count failing to fall in two sessions in a row (plan §18 P9).
