# R5 step 0, chapter K (S2): package

Status: the chapter's synthesis of design 1 (`deep-dives/q3-instances-od4.md`, "D1") and design 2 (`K-design-2.md`, "D2"),
for the planning session's decision, the S2–S4 design review and the owner's one look (OQ4, DECISIONS R5-M1). Working record,
not authority.

Base: code at `041ced79` (this branch's code equals it; `C/` = `src/Kimi/Compiler/`, `B/` = `src/Kimi/Compiler/Backend/` (new),
`T/` = `tests/xUnitTest/Tests/`); DECISIONS and PLAN read from `origin/dev` (`a8c2ec93`, R5-M1–R5-M13). Plan, PD, OQ and unit
names are the R5 and R6 Implementation Plan's; "sel" is `KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir` (PD4). (H) marks a claim not
checked by running code. K0's probe (`artifacts/verify/r5-k0/README.md`, `probe-counts.json`, base `147c4616`, counts only, no
timing) is used where cited as "K0". Planning-session input received while writing (S1's second reading) is applied: K6 widened to
compiler-function callees whose row calls a user witness, and the prepared-storage invariant (§4.1, with a scope K0 forces).

---

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| KD1 | Key `Instantiation(MirDefKey Def, Subst? Erased)`; `Erased = Instantiates(Def.Decl) ? Erase(s) : null`, so roots and edges agree by construction | 4.1 |
| KD2 | `Erase` maps every Origin (and a reference's null Origin) to `BoundOrigin.Static` and keeps arities (D2); an erased Subst or closure context is null exactly when its binder does not instantiate (new rule, replacing both designs' null rules) | 4.1 |
| KD3 | Erase late: one representative unerased Subst per item; every proof runs on it; codegen and layout Types are `Erase(Apply(t, representative))`; erased values are keys and layout inputs only | 4.1 |
| KD4 | Every Type a generic edge gives an admitted item has storage prepared in the current pass (`Apply`'s guarantee; `Erase` runs the same preparation on what it interns); consumers read stored Types through `Instance`, and an incomplete storage (a missing stored Type) of an admitted item is `InternalInvariant_Kd`; the preparation walk's depth bound is PD30's row at a generic edge and no failure for a nongeneric root's own Types (K0) | 4.1 |
| KD5 | Closure contexts as `Subst` (A5P-C1) as both designs; the legacy bridge is memoized per closure Type, not per Subst (D1) | 4.2 |
| KD6 | One Backend component `InstanceCollector` publishing one table `CheckedSet` of mono items (`MonoKind`, D2) with a view index per body item, one target per MIR site, the instance-type function and the Generated column | 4.3 |
| KD7 | Roots: closed bodies of the source modules; the bundled library is verified once by a test that roots it (D2), not recollected per compilation | 4.4 |
| KD8 | One `Admit` decides PD30's `Apply` null (capability row) and the limits 128/16/1,024 (`GenerationResourceLimit_Kd`), located by one rule | 4.6, 4.9 |
| KD9 | One capability table (R4-M8), created by K4a with its first row; rows: i128 `/` `%`, i128↔float, and the instance-stage gaps storage nesting (G128), OCC-X, generic static storage key, destruction nesting (the walk's Deep, with R4's code) | 4.7 |
| KD10 | Location rule: the cause's site in a source module, else the nearest source-module site out of Inlined scopes and up the admission chain (D2, refined) | 4.9 |
| KD11 | Collector records in ControlFlow with R3-1's MIR records (R5-M4): under sel from K4b, on every pipeline from G2, into `MirReport` at H3 | 5 |
| KD12 | R6-gen switch contract S1–S8 (D2 §4) with G2b in the switch group | 6 |
| KD13 | The `KIMI_CODEGEN` selector lands in K1 (its first consumer, the collector's run condition), not C0a | 9 |

---

## 1. Required meaning

D2 §1's table holds unchanged (SPEC 3.3.1, 8.13.3, 8.15.3, 16.3, 22.4.3–22.4.4, 22.5.1, 23.1.1–23.1.2, 23.2.2; IMPL 21.1, 21.3.1–21.3.3).
The clauses that decide this chapter's disputes:

| Clause | Text (short) | Decides |
| --- | --- | --- |
| SPEC 8.15.3 ¶2 | Checks cover every instantiation a checked nongeneric body requires, reachable or not, generated or not | Roots, check-only edges |
| SPEC 23.2.2 | A Completed result reports every Language and Proof error **of its unit**, including its checked set's concrete instances | Library rooted by a test (KD7) |
| SPEC 23.1.1 | Unsupported at the concrete use, "including an instantiation"; codes and facts implementation-defined | Location rule (KD10) |
| SPEC 23.1.2 | Grouping of one failure found under several instantiations is implementation-defined | One record per (code, subject) |
| IMPL 21.3.2 | Limits are Resource "at the affected declaration, call or token" and never stop independent checks | `Admit` drops one edge, continues |
| IMPL 21.3.3 | i128 `/`, `%`, i128↔float Unsupported for every instantiation of the checked set; direct literal conversions supported | Capability rows; K9a's literal test |
| SPEC 3.3.1, 8.13.3 | Identity excludes Origins only; `obj/D` ≠ `rc/D` | `Erase` keeps Semantics, maps Origins only |

What the representation lacks today: no erasure (keys are CallPlans compared by `SameModuloOpenOrigins` in linear scans); closure
contexts are synthetic CallPlans in a never-cleared list (`C/Binding/Binding.ClosureInstances.cs:9, 54-71`); "needs instantiation"
is a syntax property (`FunctionKoto.cs:87-89`); instance findings exist only after acceptance (five collectors and
`AnalyzeInstance`, D2 §1 "Current state"). What it wrongly allows: an accepted program can fail `build` on an instance; Binding's
eager C-layout check rejects written instances behind borrows (`Binding.Types.cs:801-804` → `Binding.Attributes.cs:384-403`, no
by-value test; (H) not run).

---

## 2. Decision table

| Topic | D1 | D2 | Chosen | Reason (facts at `041ced79`) |
| --- | --- | --- | --- | --- |
| Key shape | `Instantiation(MirDefKey, Subst)` | `Instantiation(MirDefKey, Subst?)` | D2's shape, null by `Instantiates(Def.Decl)` | One rule for roots and edges (next row) |
| When the erased Subst is null | never; "a nongeneric callee erases to `Identity(binder)`" | when no Type, length, `Self` or `Declaring` slot is bound | **new:** exactly when the binder does not instantiate (F3's view) | `CallPlan.DeclaringType` is documented as set for every nominal member (`CallPlan.cs:82-83`), so D2's slot rule gives a nongeneric struct method's call a non-null key while its root has null (H); D1's `Identity` match fails for a specialization `Implementation` selects, whose call Subst targets the generic (H). Tying both to `Instantiates` makes the agreement structural; closures follow because F3 defines a Closure's instantiation by its enclosing definition and `SubstOfClosure`'s Target is that definition (`TyCtxt.cs:208-221`) |
| Erased Origin form | `Origin` null, `OriginArguments` empty, Subst Origins/Inputs empty | every Origin → `BoundOrigin.Static` (`Binding.Origins.Model.cs:125`), arities kept | **D2**, plus a reference's null Origin → `Static` | (1) Layout and DropGlue read the stored Types of the erased instance (`Binding.Storage.cs:25`); substitution leaves an unmatched slot as the open parameter (`Binding.Storage.cs:334-345`), so D1's empty spans put open Origin parameters into erased instances' Fields; D2's keep them closed. (2) A null Origin already means "the analysis infers it" (`TyCtxt.cs:191`); a second meaning of null is avoidable. (3) `OriginArguments` holds non-null entries (`BindingModel.cs:469`). (4) `Static` adds no vocabulary |
| Erase late / representative | yes | yes | agree | Proofs read Origins (OJ3; `ConformanceWitness`, `ContractType` in `InstantiateStorageType`, `Binding.Structs.cs:23-35`) |
| Proofs on erased values | prohibited | prohibited | agree | F design 1 (`q6` line 71) suggests Copy/Sealed on erased T: interface point §7.2 |
| Prepared storage | — | — | **KD4** (planning input), scoped by K0 | `TyCtxt.Apply` prepares (`TyCtxt.cs:235` → `Binding.Structs.cs:24-36`, stamp `AdtDef.cs:178`, per pass `Binding.Structs.cs:63-69`); erased Types are interned by `Erase`, not `Apply`, so `Erase` must prepare; the stamp is per pass, so a memo hit re-checks it (a version compare, 0 B). K0: the walk's depth bound also refuses nongeneric Types that build today, so the bound cannot be an internal fault |
| G128 (`Apply` null) | Resource; conditional U-KG if the old build accepts | PD30's `Unsupported_Kd` | **PD30**; U-KG not needed | K0 Q1: old `check` accepts every depth, the old **build** already rejects at the bound (unlocated: `GenerationResourceLimit_Kd` for `Option^32<ref/i32>`, `GenerationFailed_Kd` for `Array^65<i32>`); library and milestones peak at storage depth 7 (Type 8), so DEC O3 does not apply; the move changes check acceptance, not buildability |
| Closure contexts (K2) | C1: `ClosureContext : Subst?`, scan deleted, Hover `SubstIdentity`, `ClosureCall` bridge | same | agree | — |
| Bridge memo | per closure Type | per Subst | **D1** | The old dedup keys a context by closure function plus enclosing instance (`Binding.ClosureInstances.cs:54-71`), and old Emission looks bodies up by that CallPlan (`BodyLowering.Closures.cs:84`); two closures of one instance share one Subst, so a per-Subst memo would hand both one CallPlan (legacy wrong code; F-design-2 §3.11's `ClosureCall(Subst)` has the same flaw) |
| Collector placement | Backend, after `MirAnalysis.Run` in `BuildMir` | same, independent of the analyses' results | agree | Driver may not decide acceptance (`MirAnalysis.cs:11`), PD6 |
| Item representation | prose (instances, glue, descriptors, shims) | one table, `MonoKind`, CSR targets | **D2** + view index + one target per site | C asks for the view index (C-design-2 §2.4); one target per site gives codegen a lookup, not a resolution |
| Per-body templates | yes | yes, A3b in the same scan | D2 | One walk per body serves edges, both capability passes and by-value uses |
| Roots | own modules; library by edges only (planning decision) | own modules; library verified by a test rooting it (D-B) | **D2** | Same per-compilation records; SPEC 23.2.2 speaks of "its unit", and the library's checked set depends on library source and target only; the test runs in every Session (KimiLibraryShapeTest) |
| Edges | table §2.3 | 12 forms + 7 item rows | D2 + K6 widening + pure item Subst | Planning input (S1 second reading); plan K5 already says "pure item Subst" |
| Limits | 16/128/1,024 + storage depth as Resource | same values; order; `Apply` null → Unsupported | **D2** | PD30 fixes `Apply` null as R4-M51's `Unsupported_Kd`; K0 margins: milestones reach chain 4, same template twice on a chain, 7 entries per template; Generic 128 reaches 256 old entries per template (128 erased), so the set limit binds at about 1,024 item Types with erased keys |
| Destruction walk's Deep in a glue item | — | `GenerationResourceLimit_Kd` | **`Unsupported_Kd`** capability row | R4 already reports this cause as Unsupported: A4's `Limit` is "Unsupported_Kd" (`MirEffects.cs:30`, Deep at `MirEffects.Solve.cs:531`); one code per cause (PD30's reasoning) |
| Inline-depth bounds 32/64/64 | — | open issue | **not unified in R5**; K7 counts hits | Three measures: the walk counts sequence nesting (`MirTypes.Destruction.cs:76, 101-104`), `Apply` counts Type-expression depth (`Binding.Structs.cs:55`, G128, R7), Layout counts inline nesting (`AggregateLayout.cs:47`). One constant would equate different concepts; G91's per-query cut (R4, before K7) removes the walk's false Deep on recursive types; a real hit is an (e) row |
| Capability table | one function, two passes | one table `B/Capability.cs`, rows incl. instance-stage gaps (D-C) | **D2** + Deep row; created by K4a | ARCH "Closed vocabularies" allows Unsupported only from Binding, MIR construction or the capability check; K4a's `Apply` null record is its first row (R4-M2a: the first consumer creates) |
| Generic static storage key | — | capability row (K7) | **D2** | `MirStatic` carries no container Type arguments (C-design-2 RK7), so the collector cannot form the StaticInit key; C7's D2 coverage rows keep only codegen gaps (§7.1) |
| Written C-layout instance | one owner of the rule, shared with Binding | collector reports at by-value uses; Binding's eager report deleted at G2b; sel overlap declared | **D2** | Deleting Binding's check before G2 regresses the default pipeline (it would fail at legacy generation); a suppression rule is a symptom fix; G2 and G2b merge together, so no dev state has the overlap on the default pipeline. K0 Q4: the inferred form (`make(())`, `P.init(1, ())`) passes `check` on both pipelines and fails the build unlocated (`GenerationFailed_Kd`, `AggregateLayout.cs:336` via `GenericStoragePlan.cs:413-416`), even from an unused function; only the written form is reported (Binding, at `P<()>`) |
| Location of records | requesting call (`body.Subject`) | source-module rule (K-D7) | **D2**, plus stepping out of Inlined scopes (H) | A bundled-library site is not where the user can act; SPEC 22.5.1 uses the same pattern for Aborts |
| Partition and stages | ControlFlow; default at R6-gen (recommended) | ControlFlow; sel from K4b, all from G2, `MirReport` at H3 | **D2** | R5-M4 decided; DIAGNOSTICS "Bounded excerpts" wording corrected (both designs agree on substance) |
| Generated | mark pass at emit | same; Library output = every root item | **D2** | `public` is not an export (IMPL 21.3.1); legacy generates every nongeneric body |
| R6-gen switch | publication stages only | S1–S8 | **D2** + G2b + `PrepareTypeStorage` note | R5-M4, R5-M5, PD28 |
| Requirement witness | W1 resolver + `TyCtxt.Witness` | F5b | chapter F (PD16) | K6 consumes `Body`/`Builtin`/`Refused`/`Limit` |
| Collector selector | — | in `BuildMir`, `[Transitional("R6-gen")]` | **K1 introduces it** | K1 must run "under sel" but C0a (G:C-frame) merges after G:K-core; K4b cannot depend on C0a (§9) |

---

## 3. Design chain

Required meaning (§1) → the checked set must exist before acceptance, keyed without Origins, with proofs on unerased values →
owners: TyCtxt (erasure, `Instantiates`), the Backend collector (closure, limits, capability, layout obligations, Generated),
Core (publication) → representations: erased interned Types and Substs, `Instantiation`/`MonoKey`/`CheckedSet`, capability rows →
unnecessary: the five collectors and `AnalyzeInstance` (G7), `SameModuloOpenOrigins` (G9), the closure-context list and scan (K2),
Binding's eager C-layout report (G2b), Binding's i128 gates (H7), generation-time instance publication (G2).

---

## 4. Components and contracts

### 4.1 TyCtxt instance facts: `Erase` (K3), `Instantiates` (F3), prepared storage

File: one new partial `C/Binding/TyCtxt.Instances.cs` (concern: instance identity and per-instance facts), created by F3 with
`Instantiates`, extended by K3 (`Erase`) and K10b (`CLayoutObligation`); no `TyCtxt.cs` hold (open point P2).

| Item | Contract |
| --- | --- |
| Responsibility | The Origin-erased form of a Type and of a Subst (SPEC 3.3.1, 8.13.3): the one identity rule for instance keys, descriptors, glue and layouts |
| Rule | A `ref`/`uniq`/`objref`/`objuniq` Type's `Origin` (written or null) → `Static`; every `OriginArguments` entry → `Static`; a Subst's `Origins` and `Inputs` entries → `Static`; a closure context → `Erase(context)`; Semantics, arities, lengths, symbols, `Self`, `Declaring` kept. `Erase(Subst s)` is null when `!Instantiates(s.Target)` |
| Key | `Instantiation(def, s)` = `(def, Instantiates(def.Decl) ? Erase(s) : null)`; top-level statements (`Decl` null) key null |
| Invariants | Idempotent; interned (reference equality); `Erase(Apply(t, s1)) == Erase(Apply(t, s2))` for Substs differing in Origins only; the stored Types of an erased instance equal the erasure of the representative instance's (the property Layout relies on; K3 tests it over the library and milestones, (H)); every Type it returns has prepared storage in the current pass |
| Prohibitions | No proof (Witness, Copy, Sealed, Applies, Retains, `MirCases.Views`) on an erased value; no second Origin sentinel; never rewrites an interned value |
| Cost | Two reference-keyed memos beside the interning tables, never cleared like them (`TyCtxt.cs:13-16`); warm: memo hit + prepared-stamp compare, 0 B |

`Instantiates(BindingSymbol)` is chapter F's (F3). K needs it exact: true iff the body depends on a non-Origin slot of itself or an
enclosing declaration, a Contract default body's `Self` included (H: whether a Contract's `Self` is a `GenericSlot` is unverified),
false for a specialization. K3 asserts, for every admitted item, `Erased == null` iff `!Instantiates(Def.Decl)`.

Prepared-storage invariant (KD4): every Type a generic edge gives an admitted item (its representative's Types, erased keys,
`TypeOf` results) has its struct and enum instances prepared in the current pass. `Apply` already guarantees it for non-identity
Substs; `Erase` runs the existing preparation on the Types it returns (so G9's deletion of `PrepareTypeStorage`,
`Binding.Structs.cs:17`, waits for its last caller, by G9's own grep rule). `Layout.Of`, DropGlue and codegen read stored Types
through `Instance` (current per pass, `Binding.Storage.cs:25-40`); a missing stored Type of an admitted item is
`InternalInvariant_Kd`. Scope (K0 Q1): the preparation walk refuses storage depth 65 (`Binding.Structs.cs:55`), and a nongeneric
root's own Types are never prepared by `Apply` (the identity returns the Type, `TyCtxt.cs:235-236`); a nongeneric
`Option^32<ref/i32>` local (depth 65) builds today. So the bound is not an internal fault: at a generic edge it is PD30's
storage-nesting row (as an `Apply` null), and for a root's own Types it records nothing (the layout obligation, K10a, decides them
by `Layout.Of`). Open point P8.

### 4.2 Closure contexts as `Subst` (K2; schema shared with chapter F)

As D1 §1.3–1.4 and D2 §2.1, unchanged except the bridge: `[Transitional("R6-gen")] Binding.ClosureCall(BoundType closure)`,
memoized per closure Type for the pass (one CallPlan per closure function and enclosing instance, as the old scan made), for the two
legacy readers (`GenericStoragePlan.cs:312`, `BodyLowering.Closures.cs:84`); G7 deletes it. The context Subst's Target is the
enclosing function (`Binding.ClosureInstances.cs:70`); `ReturnType` drops (a function of closure and Subst, H). bp on the closure
families; a regression test with two closures of equal result Type in one generic instance (the per-Subst failure mode).
Hover's `SubstIdentity` goes in a new `HoverBuilder` partial.

### 4.3 `InstanceCollector` and `CheckedSet` (K1, K4a; entry file `B/Instances/InstanceCollector.cs`)

D2 §2.2's contract with these changes: inputs add the root-module count (an input range, not a mode), `Capability`, F3's view;
the output adds the view index and the instance-type function; invariants add "one target per MIR site" and KD4.

| Item | Contract |
| --- | --- |
| Responsibility | The checked set (SPEC 8.15.3, IMPL 21.3.1): every closed body instance, type-driven item and shim a root requires through runtime or check-only edges, its instance-stage records (limits, capability, layout) and, at emit, the Generated column; the one resolver of callees for codegen |
| Inputs | A built, validated `MirProgram`; root-module count; `TyCtxt` (`Compose`, `Apply`, `Erase`, `Instantiates`, `Implementation`, `Witness`, `Comparison`, `Copy`, `Sealed`, `Applies`, `SubstOfClosure`, `SubstOfItem`, `CLayoutObligation`); `AdtDef`; `Capability`; `Layout.Of` typed limits (P7c) |
| Output | `CheckedSet` per program version (below) |
| Invariants | Deterministic (roots in `MirProgram.Bodies` order, FIFO worklist, targets in template order); one item per key; representatives closed; repeated run of a version is a no-op; validator findings → empty set; one target per site; KD4 |
| Prohibitions | No syntax, HIR or Plan reads; no edit of the `MirAnalysis` driver; no acceptance decision; no new `IntrinsicId`/`CompilerFunctionKind`/`BindingFailure` (PD21); no proof on erased values; no record for a failure only a Binding error causes (an Opaque operand, an Error Type in a Subst, an unresolved callee: derived, R4-M10's pattern) |
| Run | `Compilation.BuildMir()` after `MirAnalysis.Run`, independent of its results: under sel until G2 (the selector K1 adds), always from G2 |

```
readonly record struct Instantiation(MirDefKey Def, Subst? Erased);              // PD5
enum MonoKind : byte { Body, DropGlue, VTable, ObjectDescriptor, ClosureErasure, ItemAdapter, Builtin,
                       [Transitional("R8")] LegacyRow }
readonly record struct MonoKey(MonoKind Kind, Instantiation Body, BoundType? Type, CompilerFunctionKind Function);
struct MonoItem   { MonoKey Key; Subst? Representative; BoundType? RepresentativeType; int BodyOrdinal; byte View;
                    int Parent; ushort Depth; SourceRef Site; int FirstTarget, TargetCount; }
struct MonoTarget { int Item; MirLocation At; bool Runtime; }                      // CSR per item, MIR location order
```

`CheckedSet` API for codegen (chapter C §2.4): `Items` (admission order), `Targets(item)`, `View(item)` (the single bit of
`MirCases.Views(body, representative)`; more than one bit is `InternalInvariant_Kd`), `TypeOf(item, t)` = `Erase(Apply(case Type of t
under the item's view, representative))` (the one instance-type function; the order of case selection and `Apply` is fixed by K4a's
tests, H), `Find(MonoKey)` (a lookup of an item by key, e.g. a child's glue for an Open drop row; a miss is `InternalInvariant_Kd`),
`Records`, `Mark(roots)` and `IsGenerated(item)`. Names (`__kimi_f<N>`) are ModuleCodegen's, in Generated order.

`LegacyRow` hosts the targets a Body-disposition row needs (glue, witnesses, storage callees) so each site keeps one target; it is
removed with `LegacyRuntimeHelpers` at R8. Memory and time as D2 §2.2 (≈ 0.6 MB per compilation at 3,000 items, H; 0 B warm).

### 4.4 Roots (K1)

D2 §2.3 unchanged: every `MirBody` with `Source.Module < rootModules` whose key is closed (`Decl` null or `!Instantiates(Decl)`):
Fn, Getter, Setter, Test, Closure of a closed definition, StaticInit of a nongeneric static, DefaultTemplate of a closed function.
A root's representative is `Identity(binder)`, which `Apply` returns unchanged (K0), so `TypeOf` is `Erase(t)` there. Bodies of
overrides no instance applies are roots (checked) and not generated (R4-M28). The bundled library is not a root in user
compilations; a `KimiLibraryShapeTest` row runs the collector with every module as a root module and asserts zero records (plan
§5.3 item 9). Sound because a library root's closure depends on library source and the target only, and SPEC 23.2.2 makes the
library's own errors its unit's; library instances that user code requests are collected by edges and reported by KD10's rule. A user
Library project's modules are source modules, so its nongeneric bodies are roots of its own compilation and its instance-stage
records fail its `check` from G2 (R5-M4; IMPL 21.3.3 "every instantiation of the checked set"); a generic body no root
instantiates stays checked symbolically (SPEC 8.15.3).

### 4.5 Edges

Per item, each template site's Subst is composed with the representative (`TyCtxt.Compose`, `TyCtxt.cs:242`) and admitted (§4.6).
Each site has a runtime bit from one `MirReach` walk without CheckTarget edges.

| # | MIR form | Target | Unit |
| --- | --- | --- | --- |
| 1 | Call, Fn callee with a body | `Body` of `Implementation(sym, closed, 0)`, or its getter, setter or default template by `MirProgram.Callee` | K1 (closed), K4a |
| 2 | Call, Fn callee without a body | Shim disposition → `Builtin`; Body disposition → `LegacyRow` whose targets its needs row lists (rows added by the family unit: C8d element glue, C8e Dictionary-storage callees through F2c); `#LibraryImport` → none | K6, C8*, C9* |
| 2w | Call, compiler-function callee whose row calls a user witness: `TextWriter` (intrinsic `ErasedWriter`), `WriterWrite`, `TextToString`, `TextTryFormat` (legacy `Binding.HasFormattingCallback`, `GenericStoragePlan.cs:254`) | the `Body` of `Witness(row's requirement, closed)` (the user's `Utf8Format`/`BufferWriter` witness): the site's target for the intrinsic, a target of the site's `LegacyRow` item for a Body-disposition row; `Refused`/`Limit` → capability rows; the requirement per kind is one needs-table row | **K6** (widened; codegen stays C9b) |
| 3 | Call, Requirement callee | `Witness(requirement, closed)` (F5b): `Body` → row 1; `Builtin` → shim item; `Refused` → OCC-X row; `Limit` → storage-nesting row | K6 |
| 4 | Call, Virtual callee | none at the call; the receiver's `VTable` item holds the entries; codegen loads the slot by F4's `VTableDef` | K8 |
| 5 | Call, Value callee | Closure Type → `Body` under `SubstOfClosure`; ordinary function item → `Body` under the item Subst (`SubstOfItem`, made pure in K5: the same Subst `FunctionItemContext` builds, which passes no Origins, `Binding.FunctionItems.cs:108`, without its cache and storage mutation, `:82, :115`); requirement item → `Witness` (K6); Function Type → none | K5, K6 |
| 6 | Call, `staticEnsure` | StaticInit `Body` through `MirProgram.StaticInit` (F10); a static of a generic container → generic-static-key row until P42 | K7 |
| 7 | `MakeObj`/`MakeRc`/`MakeArc`/`Clone`/`TypeTest` | `ObjectDescriptor(erased payload)`; its `VTable` hangs from the descriptor | K8 |
| 8 | `DestroyValues<T>` (producer-less until R8, PD21) | `DropGlue(T)` when `MayNeedDrop` | K7 |
| 9 | Drop terminator | `DropGlue(TypeOf(place Type))` for every Drop (A2 verdicts ignored for the checked set) | K7 |
| 10 | `Aggregate(Closure)` | the Closure's `Body` | K5 |
| 11 | `Const(FnItem)` | as row 5 | K5 |
| 12 | `Cast(Erasure)` | `ClosureErasure`/`ItemAdapter` item (its targets: the body, the environment's glue) | K5 |

Item rows (targets added when an item is admitted, from its representative Type): D2 §2.4's seven rows unchanged (destructor-only
glue for a drop-declaring struct, R4-M6b/PD18; parts by the one destruction walk; Sealed payload glue; descriptor → glue and VTable;
VTable → `Applies` on the representative, inapplicable overrides checked only; erasure shims; `Builtin` leaves by `Comparison`
(F6) or `Witness`). A walk `Deep` → destruction-nesting row. A type-driven item is admitted only for a Type whose layout verdict
(§4.8) is OK: a layout failure is the one record for that Type (derived rule inside the collector).

### 4.6 `Admit` (K4a)

D2 §2.5 unchanged: (1) `Compose`/`Apply` null → storage-nesting capability row (`Unsupported_Kd`, PD30); (2) depth > 128;
(3) 16 ancestors of the same kind and definition with other keys (growing key); (4) 1,024 items of one definition — (2)–(4)
`GenerationResourceLimit_Kd`. The edge is dropped, everything else continues; chain walks only for new keys; constants without the
legacy thread-static override (`GenericStoragePlan.cs:21-23`); recursive defaults are ordinary `Body` edges.

### 4.7 Capability (`B/Capability.cs`; K4a creates, K9a/K9b extend)

| Item | Contract |
| --- | --- |
| Responsibility | The one table of operations and instances the backend does not generate on windows-x64 (IMPL 21.3.3, SPEC 23.1.1), read by A3b and the instance stage; the "capability check" of ARCH "Closed vocabularies" |
| Rows (R5) | Operation rows (K9a): i128/u128 `Div`, `Rem` (compound forms build as the same op), `Cast(Numeric)` i128/u128↔f32/f64 except direct literal conversions (IMPL 21.3.3). Instance rows: storage nesting past Binding's bound (K4a; G128, R7), OCC-X refusal (K6), generic static storage key (K7; P42), destruction nesting past the walk's bound (K7). At X2 the decided D2 coverage rows (PD11, PD27) |
| A3b | In the body scan, every body (unused and check-only blocks included), operations whose Types are closed; Opaque skipped, so Binding's gates (`Binding.Expressions.cs:1385`, `Binding.Conversions.cs:796`), which fail the node (`Binding.cs:565-584`) so MIR builds an Opaque without a record (`MirBuilder.cs:409`), never duplicate |
| Instance stage | Template sites whose Types depend on slots, per item through `TypeOf` |
| Prohibitions | No `Supports*`; `Capability.Of(...)` returns the row or none; no row copied elsewhere (Emission's at G7, the old analysis's at H10, Binding's two gates at H7) |
| Records | `Unsupported_Kd`, once per subject (§4.9) |

### 4.8 Layout obligations (K10a, K10b)

D2 §2.7 unchanged: by-value uses per template (locals except DropFlag, `Aggregate` Types, `RawAllocate`/`Stride`/`DestroyValues`
Type arguments), per item `TypeOf`, one verdict per erased Type per version: `Layout.Of` typed limits → `GenerationResourceLimit_Kd`
(K10a); F's `CLayoutObligation` view sharing Binding's zero-stride rule → `InvalidCLayout_Kd` (Language) at the use (K10b; the
catalog gains whatever ControlFlow pairing `DiagnosticContractTest` requires, H). Binding's eager report stays until G2b. Under sel
between K10b and G2b a written by-value instance may report at both the written syntax and the use (H; K10b's reproducer decides);
declared in K10b's DEC entry with removal at G2b. K10b's record is new check-time behavior: K0 shows the inferred programs pass
`check` today and never build, so the change rejects at check what the build already refused (SPEC 8.15.3), with valid twins
(`make(7)`, `P.init(1, 7)`) that build. Likewise K10a moves the inline-layout depth limit (a nongeneric `Option^33<ref/i32>` local,
rejected only by the old build, K0) to check.

### 4.9 Records and the location rule

| Record | Code | Producer |
| --- | --- | --- |
| A3b, instance-stage capability, instance rows | `Unsupported_Kd` | Capability via the collector |
| Depth, growing key, set size, layout limits | `GenerationResourceLimit_Kd` (`DiagnosticCode.tinyhand:774`) | `Admit`, layout pass |
| C-layout zero-sized Field of an inferred instance | `InvalidCLayout_Kd` | layout pass (K10b) |

Location rule (one function, the collector's): start at the cause's site (operation, by-value use, requesting site); if it lies in an
Inlined scope of a template outside the root modules, use the omitting call (H: the scope records it); if it lies in a root module
(`Module < rootModules`, as `Compilation.SyntaxOf` distinguishes, `Compilation.Pipeline.cs:139`), that is the subject; else walk the
admission chain (each item's first-admission `Site`, then its parent's) to the first root-module site. One record per (code,
subject). A user generic body reports once at its own operation; a library instance reports at the user's request; the library test
(all modules roots) reports in the library. Location changes against legacy are diagnostics, not native deviations.

### 4.10 Generated (K11)

D2 §2.9 unchanged: `Mark(roots)` at emit after acceptance and `Tests.Discover`, runtime targets only; roots: an Application's
startup item (F2a), a test build's selected `#Test` items (P6), a Library output's every root item; statics through `staticEnsure`
targets. A codegen request outside Generated is `InternalInvariant_Kd`. K12 moves the 23 library-reach `Bodies` reads.

---

## 5. Publication per stage (R5-M4, PD7, PD12)

D2 §3's table holds, with "from K4" read as "from K4b":

| Records | R5 sel | R5 `mir` only / legacy | G2 → H3 (every pipeline) | H3 → H5 | H5 on |
| --- | --- | --- | --- | --- | --- |
| MIR construction, validator (R3-1) | check, ControlFlow, decide | mir as today / none | check, decide | in `MirReport` | MIR decides |
| Collector records (§4.9) | check, decide, from K4b | collector not run (PD4) | check, decide | in `MirReport`; H0 names one owner of `Apply` null (PD30) | as left |
| PD12 refusals | generation, Emission (C4a) | — | generation, Emission (default from G3) | generation | removed |
| Coverage rows (PD11) | generation, Emission, until X2 | — | — | — | — |

DIAGNOSTICS at K4b: the instance-refusal paragraph (§4.1 "Generation retains a refused generic instance…") gets its sel note; the
"Bounded excerpts" sentence "Resource and output limits do not change acceptance" is reworded to say rendering and output limits
(a Resource Error rejects at check, as `OwnershipStorageLimit_Kd` does); "Virtual descriptor dependencies share
`GenerationResourceLimit_Kd`" is restated for the collector. G2 replaces the generation-time paragraph for every pipeline.

---

## 6. R6-gen switch contract (G0 records it; D2 §4)

| # | Clause (D2 §4, edits in bold) |
| --- | --- |
| S1 | From G2 every `Analyze` on every pipeline runs `BuildMir` (build, validate, the whole `MirAnalysis.Run`, unpublished) and the collector; the sel branch in `BuildMir` goes |
| S2 | G2 publishes construction records, validator findings and collector records at check, with entries superseding R3-1, R4-M1 (for these records) and OD10's split; a Library with an unbuildable generic instance fails `check`; the 20 instance-failure methods assert check-time records; `stage-baselines.json` and `MilestoneSourcesTest` re-baselined with reasons; STATUS gets PD30's G128 (e) row, **worded as K0 found: `check` accepted the deep finite Subst, the old build already rejected it unlocated (`GenerationResourceLimit_Kd` or `GenerationFailed_Kd`); PLAN G128's "the old path accepts" is corrected the same way** |
| S3 | PD12's gate wherever MIR codegen runs, typed Emission-partition refusal until H5; `InternalInvariant_Kd` only for a runtime `Group == -1` in a completed body |
| S4 | G2 measures default `check` and warm LSP Analyze time, allocations and retained memory (Generic shape 1/32/64/128) **with the warm-Analyze measurement K4b adds (none exists, K0 Q3)**; mir Allocation tests run on the default pipeline at 0 B |
| S5 | G1 splits `Compilation.Pipeline.cs` (behavior-preserving); **G2, G2b and G3** are verified and pushed separately and merge into dev together after G:R6-gen-switch passes |
| S6 | Rollback: a regression in a switch unit is reverted on its branch; `KIMI_CODEGEN=legacy` selectable until G4; G4 only after G3's full sweep on dev |
| S7 | Deletions follow the call graph: G4 → G5, G6 → G7 (lowering, plans, five collectors, `AnalyzeInstance` and callers, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `Binding.ClosureCall`, Benchmark readers) → G8 → G9 (Emission-only Binding members incl. `SameModuloOpenOrigins`, each after its grep; **`PrepareTypeStorage` stays, `Erase` calls it**) → G10 |
| S8 | Prerequisites: X3, R5-M4, R5-M5 with Q4, A-6, A-8, the R4 MIR lane merged; else OQ6's fallback (d) |

---

## 7. Interfaces

### 7.1 With chapter C (S4)

| K provides | C needs (C-design-2 §2.4) | Status |
| --- | --- | --- |
| Per body item: `BodyOrdinal`, `Representative`, `View` | body ordinal, representative, view index, symbol ordinal | match; the symbol ordinal is ModuleCodegen's (Generated order) |
| `Targets(item)`: one per site (Call, Drop, Aggregate(Closure), Const(FnItem), Cast(Erasure)) in location order with a runtime bit | `(Block, Target)` per runtime Call, Drop glue included, binary search | match (superset; codegen reads runtime rows) |
| no target for Virtual, intrinsic or `#LibraryImport` calls | "a virtual slot" listed among targets | **mismatch**: codegen reads the slot from F4's `VTableDef`, intrinsics from MIR |
| `TypeOf(item, t)` | `instanceType(T) = Erase(Apply(MirCases.Type(T), subst))`, collector-owned | match |
| `Find(MonoKey)`, Generated bits, `Mark` | Generated set; outside → `InternalInvariant_Kd` | match |
| Collector records through `MirRecords` (gate step 1) | `Generates` counts them from K4 | match ("from K4b") |
| Generic static storage key as a capability row (K7) | C7 lists "generic storage key" among its D2 coverage rows | **mismatch**: one owner; C7 keeps mutable, aggregate and drop-needing statics only |
| `LegacyRow` items carry needs; C's ModuleCodegen request tables still write the rows | rows as module requests | compatible; C names and writes rows, K resolves their callees |
| Selector introduced by K1 | C0a introduces `KIMI_CODEGEN` | **mismatch** (open point P1) |
| K needs `Layout.Of` typed limits (P7c) for K10a, and codegen to honor Generated and never re-check | — | — |

### 7.2 With chapter F (S3)

| K needs | F provides (F-design-2) | Status |
| --- | --- | --- |
| `Instantiates` exact, Contract default bodies included; in `TyCtxt.Instances.cs` | F3 in `TyCtxt.Instances.cs` (D2 put it in `TyCtxt.Erasure.cs`) | match on F's file; K3 adds `Erase` there (P2) |
| `Witness` with `Body`, `Builtin`, `Refused`, `Limit` | F5b's `RequirementTarget` | match |
| `Applies`/`VTableDef` on the representative | F4, `declaring` the representative | match |
| `Comparison` leaves for `Builtin` items | F6 | match |
| `CLayoutObligation` view (K10b's unit) | F §3.7 | match |
| Copy/Sealed only on representatives | F design 1 (`q6` line 71) suggests erased T | **flag** for F's package |
| K2 schema with the bridge memoized per closure Type | F §3.11 `ClosureCall(Subst)` | **mismatch**: per closure Type (§4.2) |
| A pure `SubstOfItem` (K5, ordinary items) and requirement items through `Witness` (K6) | not listed among F's removed mutations | **flag**: F may list `FunctionItemContext`'s cache among them |
| F2a startup symbol, P6 test symbols (K11); F2c storage symbols (C8e's needs rows); F10 `StaticInit` (K7) | as planned | match |

---

## 8. What becomes unnecessary; size and speed

D2 §8 holds: K2 −40 (+40); G2b −30; G7 ≈ −2,600 of instance machinery; G9 `SameModuloOpenOrigins`, `IsInapplicableVirtualBody`,
`ImplementationContext`, `IsVerifiedSpecialization`; H7 both Binding i128 gates (division `Binding.Expressions.cs:1382-1386` and
conversion `Binding.Conversions.cs:785-797`, ≈ −20); ratchets 11 → 0, 21 → 0, `Ownership.Bodies` 183 → 160. Optional R4 open issue:
`MirTypes.Agree` as `Erase(a) == Erase(b)` (≈ −30, held file). New code ≈ +1,250 (D2's estimate; plan +1,100), tests ≈ +1,000 (H).
Speed: one scan per body per version, per item one template replay with memoized `Compose`/`Erase` and one hashed probe per site;
legacy's linear scans and per-instance ownership dataflow go; codegen resolves no callee. Measured at K4b and G2.

---

## 9. Unit mapping and review groups

| Unit | Change against the plan | Review | Hold |
| --- | --- | --- | --- |
| K0 | Done (counts; timing waits for a quiet machine, README Q3). Not covered, so K7 counts them over the library, milestones and fixtures: destruction-walk Deep hits on closed instances | — | no |
| F3 | Creates `TyCtxt.Instances.cs` (chapter F decides; P2) | D+R (S3) | adv (one line, `Binding.ClosureInstances.cs:38`) |
| K1 | Adds the `KIMI_CODEGEN` selector (read once, valid only with `KIMI_PIPELINE=mir`, identity mark, `PipelineSwitchTest` throwing combinations) as the collector's run condition; DEC component entry and ARCH row "Instance collection" | G:K-core | adv |
| K2 | Bridge memoized per closure Type; two-closure regression | D+R (S2) | adv |
| K3 | `Erase` in `TyCtxt.Instances.cs`: Static form, null by `Instantiates`, preparation (KD4), the commutation and stored-Type properties | G:K-core | no |
| K4a | **New** (split): composition, representatives, `Admit`, records in `CheckedSet` unpublished; creates `Capability` with the storage-nesting row (DEC capability entry, ARCH row); Origin-only variants → one item (Generic shape: 1,408 items where the old path made 1,664, K0); finite recursion reuse; G128 twins on both sides of the bound (`pick<Array^64<i32>>` valid / `Array^65` record, which the inline-layout limit does not also hit; `Option^31`/`Option^32<ref/i32>`); 2×/4× counters; warm 0 B | G:K-core | no |
| K4b | **New** (split): `MirRecords` adds collector records under sel; DEC OD4 entry (with D-B, the location rule, "from K4b" for R5-M4); ARCH pipeline correction (D-A); DIAGNOSTICS (§5); `CompilerPipeline.md` stage 3; check cost against K0's baseline commands (stages 2+3+4 of the Generic rows) and a new warm-Analyze measurement in `src/Benchmark` (K0: none exists), which G2 reuses; zero records over the library test and the 40 milestones under sel | G:K-core | adv |
| K5 | `SubstOfItem` made pure for ordinary items | G:K-edges | **adv** (`TyCtxt.cs`) |
| K6 | **Widened:** requirement callees and compiler-function callees whose row calls a user witness (row 2w); requirement items | G:call | no |
| K7 | Adds the generic-static-key and destruction-nesting rows | G:K-edges | no |
| K8 | as planned | G:object | no |
| K9a | Operation rows and A3b (table exists since K4a); literal-conversion test | **G:K-capability** (new) | no |
| K9b | Instance-stage pass; the location rule for capability | G:K-capability | no |
| K10a | Adds the derived rule (no type-driven item for a Type whose layout failed) | G:K-edges | no |
| K10b | sel overlap declared, removal at G2b; K0's `make(())`, `P.init(1, ())` and the unused-function case as reproducers with their valid twins | D+R (S2) | no (F's partial) |
| K11 | Library output = every root item | G:C-frame | no |
| K12 | as planned | R | no |
| K13a | as planned | G:K-capability | no |
| K13b, K13c | as planned | G:K-edges, G:object | no |
| C0a | Loses the selector to K1; keeps the generation entry, gate, `kimi test` path, `-Codegen`, `summary.json` | G:C-frame | adv |
| G0 | Records §6 | D+R (S2) | no |
| G2b | **New:** Binding's eager C-layout instance report and its replay queue deleted (`Binding.Attributes.cs:375-403`, `Binding.Types.cs:801-804`, `Binding.cs:455-456`); a written borrow-only use becomes valid ((a), SPEC 8.15.3) | G:R6-gen-switch | no |
| H7 | Also deletes the conversion gate (`Binding.Conversions.cs:785-797`) | G:check-del | no |

Prerequisite changes: K4b after K4a and K0; K7 after G91 (R4's per-query cut, A-11) besides A-9 and F10; K9a after K4a (not K4).
Groups: **G:K-core** K1, K3, K4a, K4b; **G:K-edges** K5, K7, K10a, K13b; **G:K-capability** K9a, K9b, K13a (after G:K-core,
independent of G:K-edges); G:call F5b, K6, C3a, C3b; G:object F4, K8, K13c, C6a–C6c; G:C-frame E10, E15, C0a, C0b1, C0b2, K11;
**G:R6-gen-switch** G2, G2b, G3; G:R6-gen-del G4–G10. Outside groups: K2, K10b, G0 (D+R S2); K12, F10, G1 (R); K0 (—).
Reasons: K4's split separates the collector-local mechanism from the first acceptance-visible commit; the capability table is its
own canonical component (R4-M8) and G:K-edges held three concepts.

---

## 10. Departures from owner-approved texts (for the owner's summary)

| ID | Departure | From | Reason |
| --- | --- | --- | --- |
| D-A | A3b runs in the Backend collector's body scan before acceptance, not as a Mir P7 pass | ARCH "Target pipeline" P7; PLAN10 §5.1, §5.5 | Mir analyses may not read the Backend's table; the driver is R4's (PD6); one walk serves both stages |
| D-C | Instance-stage `Unsupported_Kd` causes beyond operators (storage nesting, OCC-X, generic static key, destruction nesting) are capability rows | PLAN10 §5.7 (operator, intrinsic and layout capabilities only) | ARCH allows Unsupported only from Binding, MIR construction or the capability check; one code per cause (PD30, R4-M51, A4's Limit) |
| — | R5-M4's "collector records publish from K4" is met by K4b | R5-M4 | K4's split; same stage |

Not departures: D-B (library rooted by a test) gives the records SPEC requires per unit (§4.4); the erased form (`Static`) fills
a form PLAN10 §5.7 left open; PD3 and PD11 are chapter C's highlights; pulling `ClosureContext` as `Subst` forward follows A5P §9.1.

## 11. Owner items

- Approve the package (OQ4) with D-A and D-C.
- New representations, approved with the package (no ARCH vocabulary entry): `MonoKind` (8 members, `LegacyRow` `[Transitional("R8")]`,
  a Backend enum outside ARCH's closed-vocabulary table) and the capability rows.
- No new diagnostic code; `GenerationResourceLimit_Kd` becomes located; `InvalidCLayout_Kd` gains a ControlFlow producer.
- Information: from G2 a Library with an unbuildable generic instance fails `check` (R5-M4); at G2b written borrow-only C-layout
  instances become valid (an (a) correction).

## 12. Asks to the R4 stream

| ID | Ask | Needed by |
| --- | --- | --- |
| A-9 | (unchanged) `MayNeedDrop` signature change merged | K7, C4a |
| A-11 (new) | G91's per-query cut in `MirTypes.Destruction` lands before K7 (already planned in A5 step 1 before 1b-5): without it a recursive type through a sequence (`struct Tree` with `Array<Tree>`) reaches depth 32 and every such glue item would report the destruction-nesting row | K7 |
| open issue | `MirTypes.Agree` may become `Erase(a) == Erase(b)` once K3 lands (R4's held file; optional) | — |
| H0 | (unchanged, PD30) one owner of an `Apply` null when `MirReport` absorbs collector records | H3 |

## 13. Risks

| Risk | Response |
| --- | --- |
| Cost of collection on every Analyze from G2 (LSP included); K0 has counts, no timing, and no warm-Analyze benchmark exists | K0's quiet-machine commands as baseline; K4b adds the warm-Analyze measurement; G2 records it; warm 0 B asserted; owner accepted (R5-M4) |
| G128 in a library or milestone program | Closed by K0: peak storage depth 7 of 64, so DEC O3 does not apply; the move rejects at check what the old build already refused |
| An Origin-reading proof distinguishes Origin variants of one key, or the stored Types of an erased instance differ from the erased representative's (Layout would disagree with codegen) | K3's property tests over library and milestones; a finding is a SPEC gap report, never a collector rule |
| Set limit 1,024 or chain 16 reached by a valid program | K0 margins: milestones 7 per template, chain 4; erased keys halve the busiest template's entries (256 to 128 at Generic 128); legacy values kept |
| The walk's Deep on a closed instance (nesting > 32 through destructible parts, or sequences) | K7 counts (K0 did not); G91 first (A-11); a real hit is an (e) row with a PLAN issue on bounds |
| Enforcing preparation on every named Type would make a valid nongeneric deep local Internal (K0: `Option^32<ref/i32>` local builds) | KD4's scope (§4.1); P8 |
| Legacy wrong code from the closure bridge | memo per closure Type; two-closure regression; bp on closure families |
| Written C-layout instance reported twice under sel (K10b → G2b) | declared, sel-only, removal at G2b; G2 and G2b merge together |
| First-chain location moves between edits for a library instance requested from several user sites | deterministic per version; SPEC 23.1.2 grouping |
| `Erase`'s memo outlives a pass while preparation is per pass | prepared-stamp compare on every return (KD4) |
| K10a and K10b reject at check programs that `check` accepts today (inline-layout depth, inferred C-layout) | both never built (K0 Q1, Q4); STATUS and snapshot classification name them as check-time moves of build failures |

## 14. Open points for the planning session

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| P1 | Where does the `KIMI_CODEGEN` selector land? | (a) K1, as the collector's run condition; (b) C0a joins G:K-core; (c) K4b moves to G:C-frame | (a): the first consumer creates it (R4-M2a); (b) and (c) tangle the groups' merge order |
| P2 | Which file holds `Instantiates`, `Erase` and `CLayoutObligation`? | (a) one `TyCtxt.Instances.cs` (F's name); (b) `TyCtxt.Erasure.cs` for `Erase` beside F's file | (a): one concern, and `Erase`'s null rule calls `Instantiates`; settle with chapter F's package |
| P3 | Who owns the generic static storage key gap? | (a) collector capability row from K7; (b) C7's D2 coverage row | (a): the collector cannot form the key; codegen never sees such a program |
| P4 | Unify the inline-depth bounds 32/64/64? | (a) not in R5, K7 counts, G91 first; (b) one constant for the walk and Layout (R4 ask); (c) one constant for all three | (a): three different measures; (b) only if K7's count or a fixture shows a hit |
| P5 | Is the library-root test (D-B) an acceptable reading of SPEC 8.15.3? | (a) test-rooted; (b) root it in every compilation; (c) edges only, no test | (a): identical records, no per-Analyze cost; escalate to the owner only if the design review doubts it |
| P6 | Location of instance-stage records | (a) source-module rule with the Inlined step-out; (b) always the requesting call | (a) |
| P7 | Destruction-walk Deep in a glue item: which code? | (a) `Unsupported_Kd` row (R4's code); (b) `GenerationResourceLimit_Kd` (D2) | (a): one code per cause |
| P8 | Scope of the prepared-storage invariant (planning input) given K0's nongeneric `Option^32<ref/i32>` | (a) admitted items only; the depth bound is PD30's row at generic edges and nothing for a root's own Types; (b) every named Type, the bound Internal; (c) every named Type, the bound a storage-nesting row everywhere | (a): (b) faults a valid program; (c) rejects at check a program that builds today |
