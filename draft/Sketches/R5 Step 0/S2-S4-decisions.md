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

## Design review round 1 (lenses K, F, C and cross-cutting; all BLOCK) — decisions

Each package is redone by its synthesizer against these decisions; the same lenses then review round 2. Two rounds on one family
of findings go to the owner (AGENTS).

| # | Finding (lens) | Decision |
| --- | --- | --- |
| X13 | The item's view has two owners (C, x, K) | The collector publishes `View(item)` (K §4.3); codegen selects it with `MirCases.Select`; C's own `MirCases.Views` call and C §2 row 6 are withdrawn. C's inputs add `CheckedSet.Find` (an Open drop row needs a child's glue) |
| X14 | Property-witness shims cannot be keyed or written (x, F, K) | Implemented in R5, not deferred: `MonoKind` gains a property-witness member whose key carries the Field symbol, the Self Type and the witness kind (K fixes the exact `MonoKey` shape); K6 creates the item from `TyCtxt.Witness`'s Storage kind; a new unit C3c in G:call writes the shim, prerequisites K6, P7b and C4a (Set needs DropGlue), fixtures `Counted`/`Own` |
| X15 | Target-bearing intrinsics stated inconsistently (x, K) | One whitelist column on the intrinsic row table names the intrinsics whose sites carry a collector target (`ErasedWriter`, `StaticEnsure`, `MakeObj`/`MakeRc`/`MakeArc`/`Clone`/`TypeTest`, `DestroyValues`, as K's rows); every other intrinsic has none. C9b reads K6's target (prerequisite K6) and never calls `TyCtxt.Witness` |
| X16 | K6/F5b interface (F, K) | K6 calls `TyCtxt.Witness(requirement, contract, subst)` with `MirCallee.Contract` (formatting rows pass their Contract); kinds Body, Builtin, Storage, Refused, Limit; K's rows are updated |
| X17 | `Core/Compilation.Pipeline.cs` changed without a hold, fourth R5 change (x) | C0a puts the generation entry and the gate in a new R5-owned partial `Core/Compilation.Generation.cs` (contract entry); C4a's gate step 2 and K11's mark change that partial only. K1 and K4b keep their adv hold lines on `Compilation.Pipeline.cs`; G1's split stays at R6-gen |
| X18 | Nested C-layout obligations missed; G2b would regress (K) | `Layout.Of` returns OK, Limit or CLayout(instance), one owner of the zero-sized rule at any depth; K10a/K10b map CLayout to `InvalidCLayout_Kd` at the by-value use and Limit to the Resource record; any other failure is `InternalInvariant_Kd`. The probes `let x: Option<P<()>> = .None` and inferred `f<T>` with `Option<P<T>>` become tests |
| X19 | `Admit` limits reject valid programs through recursive glue (K) | The growing-key limit and the per-definition set limit apply to Body items only (unbounded growth always passes through bodies, SPEC 8.15.3); the depth limit applies to every item |
| X20 | `Erase` contract vs prepared storage (K) | `Erase` only interns; a null stored Type at a reader is `InternalInvariant_Kd`; `PrepareTypeStorage` is deleted at G9 as the plan says (audit row restored); P8's scope is moot |
| X21 | Destruction-nesting row is dead under X3 (K, x) | Dropped: KD9's destruction-nesting row, P7, D-C's "destruction nesting", K7's Deep count and A-11 as a K7 prerequisite (A-11 is satisfied anyway: G91 merged at `5ae8ebb7`) |
| X22 | Generic static storage key: the old path accepts and builds an immutable scalar in a generic container's group (`Cache<i64>.Statistics.count`), so the K7 row would reject it at `check` from G2 (K) | Recommendation to the owner: keep OQ17(b) (generic storage keys belong to P42) and defer the form under PD27 as a D2 (e) row at G2 if no library, milestone or corpus program uses it (K7 confirms by search), recorded in STATUS and PLAN and approved in the PD27 list at R5's exit. This corrects R5-M13(b)'s premise ("the old analysis accepts only immutable nongeneric static scalars"): it also accepts immutable static scalars in generic containers' groups. Alternative for the owner: `MirStatic` carries the container's Type arguments before G2 (P42 needs it anyway) |
| X23 | DropGlue lacks concrete closure environments (C) | A row for closure environments: remaining captures are destroyed in reverse initialization order (SPEC 16.4); an inline environment of 8 bytes or less (PD19) gets the environment word's address |
| X24 | F5a's core would change `Witnesses` (F) | The abstract-Self forwarding rule (`Binding.RequirementInstances.cs:56-66`, SPEC 8.15 forwarding) stays in the materializer; `Witnesses` maps the new kinds (Property, FixedArrayEntry, IntegerPosition, Refused) to `false` as today; `MirEffectsTest` joins F5a's checks |
| X25 | Unlisted departures (C, x, F) | The owner summary adds: PLAN10 §5.8 (`Storage.moveBytes` allows overlap) against C8a's "no overlap" comment until R8; PD15's refinement and `ComparisonPlan(CallPlan)` deleted at H11 instead of R7 (F §8); F6's new signature, order and group G:compare; the legacy comparison twin suspends A5P §7's one-producer rule for comparison leaves until A-12 or R7 (A4 and codegen use different resolvers; the difference costs precision, never wrong code) |

Adopted non-blocking findings (each package fixes its text): stale text against X1–X12 (BaseDepth, the startup module column,
the selector in C0a, glue order = SPEC 16.3.2); `Layout.Of`'s base-prefix invariant (verified for both layout modes); C10b and
E12 prerequisites name C7b; Open drop rows are emitted in SPEC 16.3.2 order through DropGlue's per-level function; the DropGlue
ABI `(ptr, ptr location, i64)` is stated; the O2 parity list notes the Ryu rows spliced verbatim and `memmove` without
`noalias`; a folded StaticInit body is not Generated; the deviation form's signature is listed under plan wording; `Layout.Of`'s
Origin guard stays in the old pool for bp; F7 records a located `Unsupported_Kd` when `Comparison` returns null; F7's goldens pin
`(nan,1.0)<(nan,2.0)` false, `(-0.0,1)<(0.0,2)` true, `(1.0,nan)>(1.0,2.0)` false and the generic `T=f64` case true; F6 creates
the Self-bearing `Synthesized` factory in `TyCtxt.cs` (adv hold) rather than splitting factories by hold; F2a states
`StartupResult.Function`'s removal stage; F3's nested-function change to legacy generation is classified as a D5 miscompile fix
after its reproducer (fact 7), or shown absent; F7 builds on F6's pushed branch inside G:compare; the "Contract default body's
Self" clause is dropped (SPEC 8.4.2, 8.6); A-11/A-12 are renumbered in F; `Erase` also erases a Slice's Origin; a null-key
item's representative is `Identity(Decl)` (a specialization's Subst targets its generic; top-level statements have none); the
closure bridge memo key is reconciled with the old dedup (closure function, Subst); Library-output Generated excludes R4-M28's
inapplicable overrides; the location-rule function lands in K4a and nested Inlined scopes report at the outermost call; a View
with zero bits is `InternalInvariant_Kd`; P5's DEC entry states its preconditions (no library Condition, one target) and the
library test runs the whole collector; the package states whether A3b scans library bodies in every compilation; `build` under
sel between K1 and C0b1 refuses with a typed record; §5.3 item 1 names G2, G2b and G3; K3 needs F3; every "K4" prerequisite names
K4a or K4b; K6 needs P1d's rows.

Owner summary additions: the temporary structures `MonoKind.LegacyRow` (R8), the out-string `WriteIr`/`Validate` adapters (C0a to
G9b) and K10b's double report under sel (until G2b); acceptance changes from G2 (K10b's `InvalidCLayout_Kd` is a Language error at
check; K10a moves the layout-depth limit to check) and at G2b (written borrow-only C-layout instances become valid); K10b may need
the held `DiagnosticRequirement.cs`; the static constant fold keeps no slot state, so P42 must narrow the fold or restore the state
for SPEC 22.4.3's shutdown rule; A-2 is required and R4 agreed (recorded at approval); X22; KD1's nullable key against PD5's
wording; the final `MonoKind` list; O2's analysis (SPEC 23.1.1 allows reuse; `MissingNativeRequirement_Kd` is an option for the
missing-supply cause); A-2 moves C4a, the drop chain and X1–X3 behind R4's unit 1c (A5 step 1), so plan §3.2's fallback (d)
loses its meaning.

## After the round-1 redo — decisions on the synthesizers' notes

| # | Note | Decision |
| --- | --- | --- |
| X26 | X8 still named a C-layout instance view while X18 makes `Layout.Of` the owner (K, C) | The view is dropped from `TyCtxt.Instances.cs` (it holds `Instantiates` and `Erase` only); `Layout.Of` is the one owner of the zero-sized C-layout rule; F1's `AdtDef.IsCLayout` feeds it; Binding keeps rejecting written forms and loses its eager instance report at G2b |
| X27 | The intrinsic row table and the direct-part enumeration are needed before their C units exist (K P9, P10) | First consumer creates (R4-M2a): K1 creates the intrinsic row table with its `Target` column (C0b1 extends it with codegen rows); K7 creates the direct-part enumeration in `Backend/DropGlue.cs` and C4a writes glue from it |
| X28 | `TyCtxt.Witness`'s result needs the witness kind for X14's key (F) | `(Kind, Target, Subst, Access)`, `Access` the existing `PropertyWitnessKind`; K keys the property-witness item on (Field, erased Self, `Access`) |
| X29 | G131: `MirValidator` accepts a Never-typed operand on every path (R4) | New unit F11 in window W1 (W1's files gain `Mir/MirValidator*.cs`): a Never temp is read only in check-only blocks; prerequisite of C0b1; an R4 reviewer signs it off (A1–A5 trust the validator) |
| X22′ | X22's options (C) | Superseded by X33 |

## Design review round 2 — decisions

Round 2: lens F PASS; lenses K, C and cross-cutting BLOCK on new findings (all round-1 findings resolved). Generic static keys
(X22, X22′) reached a second round on one family, so they go to the owner as a choice (AGENTS). The other findings are new and are
fixed once more; round 3 checks only them.

| # | Finding (lens) | Decision |
| --- | --- | --- |
| X30 | F11's rule rejects valid programs: a Never parameter, Field or payload is read on runtime paths (`func absurd(x: Never) -> i32 => x`, `.Err(let e) => e` on `Result<i32, Never>`), SPEC 3.1.3, 14.9.3 (C) | F11's rule names the actual hazard: the destination of a diverging Call (the builder's unwritten temp, `MirBuilder.Calls.cs:194`) is read only in check-only blocks; any other Never-typed place is valid. The two programs above become regressions. X29's wording is replaced by this |
| X31 | `Layout.Of` has no layout for Never (C) | `Layout.Of` states IMPL 21.1.3's rows: Never has size 0, alignment 1, stride 0, and a Type that stores Never inline is laid out as if Never were zero-sized; likewise `Loan<T>`'s row. The old pool keeps its null for bp (a STATUS limit already lists Never payloads); `let a: Option<Never> = .None` (SPEC 3.1.3) becomes a test |
| X32 | X14 put C3c in G:call with C4a as a prerequisite, so G:call and G:C-drop depend on each other (x) | C3c moves to G:C-drop (after C4a); G:call (F5b, K6, C3a, C3b) merges without waiting for R4's unit 1c |
| X33 | Generic static storage keys: the old path accepts and builds an immutable literal static in a generic container's group (`Cache<i64>.Statistics.count`); from G2 the collector would reject it (K, two rounds) | **Owner choice**, after two rounds. (a) **Recommended**: keep OQ17(b) (generic storage keys belong to P42) and defer the form under PD27 as a D2 (e) row at G2, if no library, milestone or corpus program uses it (K7 confirms by search), recorded in STATUS and PLAN and approved in the PD27 list at R5's exit. (b) A whitelist: exempt a static that is an immutable `let` of a closed constant Type whose StaticInit folds to a constant not depending on the container's Type arguments (one slot is then exact: the initializer accesses nothing, so no cycle or Abort, its moment is unobservable, and shutdown needs no drop); it needs a KD1 exception (the folded StaticInit keyed `(def, Erase(Identity))`) and K7 creating the fold predicate before C7b. (c) A representation: `MirStatic` carries the container's Type arguments before G2 (P42 needs it anyway; a MIR change in window W1). In each case R5-M13(b)'s premise is corrected: the old analysis accepts immutable static scalars in generic containers' groups too (bool and signed-literal `let`s, `StaticScalar.cs:38-58`) |

Alignment (no design change; each package fixes its text): the intrinsic row table is created by K1 at `Backend/IntrinsicRows.cs`
(C0b1 extends it); `DropGlue.Parts` is K7's function in `Backend/DropGlue.cs`, which C4a extends with glue writing; the sel `build`
refusal between K1 and C0b1 is one typed `Unsupported_Kd` per output root at that root body's subject; G1's scope shrinks (the
generation entry moved at C0a, X17); K6's prerequisites include F6 (the Builtin item's leaves and the Self-bearing factory come from
F6, so G:compare merges before G:call); W1's order starts with F11, W1 opens after R4's E13/E14 MirBuilder units land and R4 says
so, and C0b1 waits for W1 (timing risk recorded); F §4.4's core domain reads "any non-abstract Self; the materializer forwards
abstract-part Selfs before calling the core"; C3c's ABI comes from the key (Binding accepts a storage witness only with a `ref/Self`
getter or `uniq/Self` setter, a result `F` or `ref/F`, and a Set input `F`, `Binding.PropertyMatching.cs:102-117`), and K6 asserts
the agreement; stale "row 2w" references point at K's rows 2 and 6; C row 30 shows `Access`; the closure bridge memo is keyed
(closure symbol, Subst); `Layout.Of` states whether CLayout propagates through collection element layouts; the depth limit on glue
chains binds only past 64 levels (noted); a gate refusal under sel may hide independent coverage gaps until X2 (a recorded sel-only
limit); P0's `WriteExternals` takes generator-neutral inputs (resolved by P0's review). Stale rows of this file: P4's Deep count,
P7, P8 and X11's A-11 as a K7 prerequisite are superseded by X20 and X21.

Owner summary additions (round 2): X33's options; X29's addition of `Mir/MirValidator*.cs` to W1, a change of R5-M7's W1 file list,
and C0b1 waiting for W1, which puts the frame on R4's schedule; the temporary structures "sel `build` refusal" (K1 to C0b1) and
Binding's eager C-layout instance check as a second copy of the rule (until G2b); the new representations: the intrinsic rows'
`Target` column, `Layout.Of`'s OK/Limit/CLayout result and its Never rows (X31).

R4 information: under `mir`, a slot-typed static (`let zero: T = 0` in a generic group) also produces a MIR `InternalInvariant_Kd`
beside Binding's `Unsupported_Kd` (seen at `147c4616`; it should be a derived value); check on dev.
