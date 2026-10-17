# R5 step 0: planning-session decisions on the S2–S4 package

The planning session's decisions on the open points of `K-package.md`, `F-package.md` and `C-package.md` and on their
mismatches (2026-10-18). With these, the three packages are the design sent to the S2–S4 design review and then once to the
owner (OQ4, DECISIONS R5-M1). Where a package and this file disagree, this file governs.

## Cross-chapter reconciliation

| # | Topic | Decision | Reason |
| --- | --- | --- | --- |
| X1 | Receiver projection of a requirement target (C O5, F 6) | No `BaseDepth`: `TyCtxt.Witness` returns `(Kind, Target, Subst)`; codegen projects nothing. `Layout` keeps "the complete base occupies a prefix" (`AggregateLayout.cs:382-383`) as a stated invariant of `Layout.Of`, tested in P7b | F fact 10; one rule in Layout instead of a depth on every target |
| X2 | Instance-type function (C O4) | The collector publishes `TypeOf(item, t)`; codegen never composes `Erase(Apply(…))` | One rule (K §7.1 already provides it) |
| X3 | DropGlue item shape (C O3) | Recursive per erased Type: a glue's targets are its direct parts' glues in SPEC 16.3.2 order; K's glue edge rows follow C | SPEC order per level; legacy shape; one glue per Type |
| X4 | Callees and needs of legacy runtime rows (C O6) | The `LegacyRuntimeHelpers` row declares its library callees and type-driven needs; the collector reads the row; no collector-side needs table | One owner per row (`[Transitional("R8")]` with the rows) |
| X5 | Virtual calls (K §7.1) | No collector target: codegen reads the slot from F4's `VTableDef` | Dispatch is a fact, not an instance edge |
| X6 | Generic static storage key (K P3) | A collector capability row (K7); C7b's D2 rows keep mutable, aggregate and drop-needing statics only | The collector cannot form the key; codegen never sees such a program |
| X7 | Selector (K P1) | `KIMI_CODEGEN` lands in K1, its first consumer (read once; valid only with `KIMI_PIPELINE=mir`); C0a keeps the generation entry, gate, `kimi test` path, `-Codegen` and `summary.json` | First consumer creates it (R4-M2a); no cross-group merge order |
| X8 | File of `Instantiates`, `Erase` and the C-layout instance view (K P2, F 3) | One partial `Binding/TyCtxt.Instances.cs` (PD20's `TyCtxt.Erasure.cs` renamed) | One concern: instance identity; `Erase`'s null rule calls `Instantiates` |
| X9 | Property witnesses (F 1) | Backend Storage shims (PLAN10 §5.7); K6 adds the item row; F5p is not adopted | F facts 4 and 12; no extra MIR bodies |
| X10 | Closure bridge memo (K §4.2, F §3.11) | Memoized per closure Type, not per Subst; K2 adds the two-closure regression | Per-Subst memo gives two closures in one instance one CallPlan (legacy wrong code) |
| X11 | R4 ask numbering | A-11 = G91's per-query cut before K7 (K); A-12 = A4's `Witnesses` comparison branch reads `TyCtxt.Comparison(operators: false)` (F's "A-11", optional); F's withdrawn D2 A-12 is not used | Unique ids |
| X12 | A-2 (C O1) | Required before C4a: `MirDropTable` publishes per-body completeness from A1's and A2's own outcome per body (name as R4 fixes it, e.g. `Incomplete(body)`); the R4 planning session agreed to land it in unit 1c, independent of G125 | `MirReport` merges by (code, subject) with no body; A5's arena code equals A1's |

## Chapter K open points

| # | Decision |
| --- | --- |
| P1 | X7 |
| P2 | X8 |
| P3 | X6 |
| P4 | The three inline depth bounds (walk 32, storage 64, layout 64) are not unified in R5; K7 counts destruction-walk Deep hits over library, milestones and fixtures; a real hit is an (e) row with a PLAN issue |
| P5 | The bundled library's checked set is rooted once by a test, not in every compilation; user modules (and a user Library project's own modules) are roots. The design review is asked to challenge this reading of SPEC 8.15.3 and IMPL 21.3.3 explicitly |
| P6 | Instance-stage records use the source-module location rule with the Inlined step-out |
| P7 | A destruction-walk Deep in a glue item is `Unsupported_Kd` (R4's code for this cause, A4's Limit), one code per cause |
| P8 | The prepared-storage invariant covers admitted items only; a root's own Types are not checked against the depth bound (K0: a nongeneric `Option^32<ref/i32>` local builds today) |

## Chapter C open points

| # | Decision |
| --- | --- |
| O1 | X12 |
| O2 | Owner item: successor code for output and publication-time native-configuration failures. Recommendation to the owner: (a) a new Input code `ArtifactPublicationFailed_Kd` (net 0 with `GenerationFailed_Kd` deleted); alternatives (b) reuse `ProjectPreparationFailed_Kd`, (c) recategorize `GenerationFailed_Kd` |
| O3 | X3 |
| O4 | X2 |
| O5 | X1 (no `BaseDepth`) |
| O6 | X4 |
| O7 | C7a's evidence is E4's report plus a throwaway hunk check; no new E4 option |
| O8 | ABI attributes are written on value calls too, through the one parameter writer; MIR-versus-legacy IR text stays information (PD9), the O2 corpus gates |
| O9 | P7a records in STATUS that the in-memory nonnull `Option<ref/T>` (IMPL 21.1.3) is not laid out (PLAN U8) |

## Chapter F open points

| # | Decision |
| --- | --- |
| 1 | X9 |
| 2 | The legacy comparison composition (private `ComparisonPlan`, `BoundComparison`, `RequirementImplementation`) stays as a recorded temporary structure beside F6 until its last reader goes: A-12 if R4 accepts it, else R7; its other readers go at H5, G7 and H11 |
| 3 | X8 |
| 4 | No startup `Module` column (the startup module is always ordinal 0); `StartupResult.Symbol` only |
| 5 | F6 is keyed by the Contract symbol: `TyCtxt.Comparison(contract, self, operators)` |
| 6 | X1 |
| 7 | F6 and F7 form review group G:compare, F6 first; an R4 reviewer signs off F7 |
| 8 | F3 re-points the legacy reader (`GenericStoragePlan.cs:68`) and deletes `FunctionKoto.RequiresInstantiation` (a Syntax property reading Binding), bp-checked |

## For the owner's summary (OQ4)

Departures from owner-approved texts, beyond PD3 and PD11 (highlighted in R5-M1): K D-A (A3b runs in the Backend collector's
body scan before acceptance, not as a Mir P7 pass) and D-C (instance-stage `Unsupported_Kd` causes beyond operators are
capability rows); C: ask A-2 required, PD23 reversed (`transferBytes` keeps `memcpy`; the library declaration says the ranges
do not overlap), `GenerationFailed_Kd`'s successor (O2), and the fact that P42's generic storage keys need `MirStatic` to carry the
container's Type arguments (R5-M13(b)'s "no representation change" holds for the slot state). New representations approved with
the package: `MonoKind`, the capability rows, the chapter-F facts of `F-package.md` §5, and the temporary structures (the F2c
Dictionary-storage view until R8, the legacy comparison composition until A-12 or R7).
