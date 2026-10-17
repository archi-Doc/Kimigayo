# R5 step 0, chapter K (S2): package (round 2)

Status: the chapter's synthesis of design 1 (`deep-dives/q3-instances-od4.md`, "D1") and design 2 (`K-design-2.md`, "D2"),
redone against the planning session's decisions (`S2-S4-decisions.md`: X1–X25, the chapter K open points P1–P8 and the adopted
non-blocking findings), which govern. It goes to the round-2 design review and then once to the owner (OQ4, DECISIONS R5-M1).
Working record, not authority.

Base: code at `041ced79` (`C/` = `src/Kimi/Compiler/`, `B/` = `src/Kimi/Compiler/Backend/` (new), `T/` = `tests/xUnitTest/Tests/`);
DECISIONS and PLAN from `origin/dev`. Plan, PD, OQ and unit names are the R5 and R6 Implementation Plan's; "sel" is
`KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir` (PD4). (H) marks a claim not checked by running code. "K0" is the probe evidence
`artifacts/verify/r5-k0/README.md` (base `147c4616`, counts only, no timing).

Round 2 changes: X1 (no `BaseDepth`), X2 (`TypeOf`), X3 (glue recursive per Type, direct parts in SPEC 16.3.2 order), X4 (legacy
rows declare their callees and needs), X5 (no target for virtual calls), X7 (selector in K1), X8 (`TyCtxt.Instances.cs`), X9/X14
(property-witness shim item), X10 (bridge memo), X11 (A-11), X13 (`View(item)`), X15 (target column on the intrinsic rows), X16
(`Witness` with the Contract), X17 (`Compilation.Generation.cs`), X18 (`Layout.Of` owns the C-layout rule), X19 (limits on Body
items), X20 (`Erase` only interns), X21 (no destruction-nesting row), X22 (generic static key to the owner), and the adopted
non-blocking items naming K.

---

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| KD1 | Key `Instantiation(MirDefKey Def, Subst? Erased)` with `Erased = Instantiates(Def.Decl) ? Erase(s) : null`: roots and edges agree by construction (departs from PD5's wording, §10) | 4.1 |
| KD2 | `Erase` maps every Origin (a reference's or Slice's null Origin included) to `BoundOrigin.Static` and keeps arities; it only interns (X20); an erased Subst or closure context is null exactly when its binder does not instantiate | 4.1 |
| KD3 | Erase late: one representative unerased Subst per item (`Identity(Decl)` for a null-key item); every proof runs on it; erased values are keys and Layout inputs only; codegen's Types come from `TypeOf` (X2) | 4.1 |
| KD4 | Closure contexts as `Subst` (A5P-C1); the legacy bridge is memoized per closure Type (X10) | 4.2 |
| KD5 | One Backend component `InstanceCollector` publishing `CheckedSet`: mono items of nine `MonoKind`s, the one `View(item)` (X13), one target per site, `TypeOf`, `Find`, records and the Generated column | 4.3 |
| KD6 | Roots: closed bodies of the root modules; the bundled library is rooted once by a test (P5) under stated preconditions | 4.4 |
| KD7 | Edges per MIR form; target-bearing intrinsics named by one column of the intrinsic row table (X15); legacy rows declare their callees and needs (X4); no target for virtual calls (X5) | 4.5 |
| KD8 | `DropGlue` recursive per erased Type: its targets are its direct parts' glues in SPEC 16.3.2 order (X3) | 4.5 |
| KD9 | One `Admit`: `Apply` null → capability row (PD30); depth 128 on every item; growing key 16 and set 1,024 on Body items only (X19) | 4.6 |
| KD10 | One capability table (R4-M8), created by K4a; rows: i128 `/` `%`, i128↔float, and the instance-stage gaps storage nesting (G128), OCC-X, generic static storage key (X21 drops destruction nesting) | 4.7 |
| KD11 | Layout obligations through `Layout.Of` → OK, Limit or CLayout(instance), the one owner of the zero-sized rule at any depth (X18) | 4.8 |
| KD12 | Location rule (lands in K4a): the cause's root-module site, else the outermost omitting call of nested Inlined scopes, else up the admission chain (P6) | 4.9 |
| KD13 | Records in ControlFlow with R3-1's MIR records (R5-M4): under sel from K4b, everywhere from G2, into `MirReport` at H3 | 5 |
| KD14 | R6-gen switch contract S1–S8 with G2b in the switch group | 6 |
| KD15 | The `KIMI_CODEGEN` selector lands in K1, the collector's run condition (X7); generation entry and gate in `Core/Compilation.Generation.cs` (X17) | 9 |

---

## 1. Required meaning

D2 §1's table holds (SPEC 3.3.1, 8.13.3, 8.15.3, 16.3, 16.4, 22.4.3–22.4.4, 22.5.1, 23.1.1–23.1.2, 23.2.2; IMPL 21.1, 21.3.1–21.3.3).
The clauses that decide this chapter's disputes:

| Clause | Text (short) | Decides |
| --- | --- | --- |
| SPEC 8.15.3 | Layout obligations at by-value uses for every instantiation a checked nongeneric body requires, reachable or not, generated or not; a failure is a Language error at the use; a limit is Resource | Roots, check-only edges, KD11 |
| SPEC 23.2.2 | A Completed result reports every Language and Proof error **of its unit**, including its checked set's concrete instances | Library rooted by a test (KD6) |
| SPEC 23.1.1 | Unsupported at the concrete use, "including an instantiation" | Location rule (KD12) |
| SPEC 23.1.2 | Grouping of one failure found under several instantiations is implementation-defined | One record per (code, subject) |
| SPEC 16.3.2, 16.4 | Destruction order per level; a declaring struct's `drop` destroys its own parts; closure captures in reverse initialization order | KD8 |
| IMPL 21.3.2 | Limits are Resource "at the affected declaration, call or token" and never stop independent checks | `Admit` drops one edge |
| IMPL 21.3.3 | i128 `/`, `%`, i128↔float Unsupported for every instantiation of the checked set; direct literal conversions supported | Capability rows |
| SPEC 3.3.1, 8.13.3 | Identity excludes Origins only; `obj/D` ≠ `rc/D` | `Erase` keeps Semantics |

What the representation lacks today: no erasure (keys are CallPlans compared by `SameModuloOpenOrigins` in linear scans); closure
contexts are synthetic CallPlans in a never-cleared list (`C/Binding/Binding.ClosureInstances.cs:9, 54-71`); "needs instantiation"
is a Syntax property reading Binding (`FunctionKoto.cs:87-89`); instance findings exist only after acceptance. What it wrongly
allows: an accepted program fails `build` on an instance (K0: G128 twins, inferred C-layout instances, deep inline layouts all pass
`check` and fail the old build unlocated); Binding's eager C-layout check rejects written instances behind borrows
(`Binding.Types.cs:801-804` → `Binding.Attributes.cs:384-403`, (H) not run).

---

## 2. Decision table

| Topic | D1 | D2 | Chosen | Reason |
| --- | --- | --- | --- | --- |
| Key shape and null rule | `(MirDefKey, Subst)`; a nongeneric callee erases to `Identity` | `(MirDefKey, Subst?)`; null when no Type/length/Self/Declaring slot | KD1 | `CallPlan.DeclaringType` is set for nominal members (`CallPlan.cs:82-83`), so D2's slot rule gives a nongeneric method's call a non-null key while its root has null (H); a specialization's call Subst targets its generic. Gating by F3's `Instantiates` makes agreement structural |
| Erased Origin form | `Origin` null, spans empty | `Static`, arities kept | D2 + Slice and null Origins → `Static` | Readers take stored Types of erased instances through `Instance` (`Binding.Storage.cs:25-40`); substitution leaves an unmatched slot as the open parameter (`:334-345`), so empty spans would put open Origins in erased Fields; null already means "inferred" (`TyCtxt.cs:191`); a Slice keeps its Origin in `Origin` (`TyCtxt.cs:119-126`) |
| `Erase` and storage | — | — | X20: only interns | A null stored Type at a reader is `InternalInvariant_Kd`; `PrepareTypeStorage` goes at G9 as planned |
| Representative | first admitting Subst | same | first admitting Subst; `Identity(Decl)` for a null-key item; none for top-level statements | A specialization's Subst targets its generic, so it cannot represent the specialization's body |
| Proofs on erased values | prohibited | prohibited | agree | OJ3: proofs read Origins (`Binding.Structs.cs:23-35`) |
| Closure contexts, bridge | C1; memo per closure Type | C1; memo per Subst | C1; X10 | Two closures of one instance share one Subst; old Emission looks bodies up by the CallPlan (`BodyLowering.Closures.cs:84`) |
| Collector placement | Backend after `MirAnalysis.Run` | same | agree; selector in K1 (X7) | Driver may not decide acceptance (`MirAnalysis.cs:11`) |
| Items | prose | `MonoKind` table | D2 + `PropertyWitness` (X14) + `View` (X13) + one target per site | §4.3 |
| Roots | library by edges only | library rooted by a test | P5 with preconditions | §4.4 |
| A3b over library bodies | — | every body | root modules only | Library bodies are the library test's (P5); no per-compilation cost |
| Edges | D1 §2.3 | 12 forms + 7 item rows | D2 under X3, X4, X5, X14–X16 | §4.5 |
| DropGlue | walk-based targets | walk-based targets | X3: direct parts in SPEC 16.3.2 order | One glue per Type and level, legacy shape; no deep walk, so no Deep (X21) |
| Limits | 16/128/1,024 | same, ordered, `Apply` null → Unsupported | D2 under PD30 and X19 | K0 margins: milestones chain 4, 7 entries per template; Generic 128: 256 old entries per template, 128 erased |
| Capability | one function, two passes | one table incl. instance gaps | D2 minus destruction nesting (X21) | ARCH "Closed vocabularies" admits Unsupported only from Binding, MIR construction or the capability check |
| Generic static key | — | capability row (K7) | X6 row; X22 to the owner | §4.7 |
| Layout obligations | Backend pass sharing Binding's rule | `Layout.Of` limits + TyCtxt C-layout view | X18: `Layout.Of` → OK/Limit/CLayout | One owner of the zero-sized rule at any depth; nested uses (`Option<P<()>>`) are covered |
| Written C-layout instance | — | Binding's report deleted at G2b; sel overlap | D2 | Deleting Binding's check before G2 regresses the default pipeline; G2 and G2b merge together |
| Location | requesting call | source-module rule | P6, nested Inlined → outermost call | A library site is not where the user can act; SPEC 22.5.1's pattern |
| Partition and stages | ControlFlow | ControlFlow, sel from K4b, all from G2 | R5-M4 | — |
| Generated | mark at emit | same; Library output = every root | D2, minus R4-M28's inapplicable overrides | `public` is not an export (IMPL 21.3.1) |
| Witness | W1 resolver | F5b with `BaseDepth` | X1, X16: `Witness(requirement, contract, subst)` → Body/Builtin/Storage/Refused/Limit | Chapter F |
| Depth bounds 32/64/64 | — | open issue | P4: not unified in R5 | Three different measures (walk, `Apply` storage, inline layout); X3 removes the collector's deep walk |

---

## 3. Design chain

Required meaning (§1) → the checked set must exist before acceptance, keyed without Origins, with proofs on unerased values →
owners: TyCtxt (`Erase`, `Instantiates`), the Backend collector (closure, limits, capability, layout obligations, Generated, the one
callee resolution), `Layout` (layout verdicts), Core (publication) → representations: erased interned Types and Substs,
`Instantiation`/`MonoKey`/`CheckedSet`, capability rows, the intrinsic target column → unnecessary: the five collectors and
`AnalyzeInstance` (G7), `SameModuloOpenOrigins` and `PrepareTypeStorage` (G9), the closure-context list and scan (K2), Binding's
eager C-layout report (G2b), Binding's i128 gates (H7), generation-time instance publication (G2).

---

## 4. Components and contracts

### 4.1 TyCtxt instance facts: `Erase` (K3) and `Instantiates` (F3) in `C/Binding/TyCtxt.Instances.cs` (X8)

| Item | Contract |
| --- | --- |
| Responsibility | The Origin-erased form of a Type and of a Subst (SPEC 3.3.1, 8.13.3): the one identity rule for instance keys, descriptors, glue and layouts |
| Rule | The `Origin` of a Semantics reference (`ref`, `uniq`, `objref`, `objuniq`) or a Slice, written or null, → `Static`; every `OriginArguments` entry → `Static`; a Subst's `Origins` and `Inputs` entries → `Static`; a closure context → `Erase(context)`; Semantics, arities, lengths, symbols, `Self`, `Declaring` kept. `Erase(Subst s)` is null when `!Instantiates(s.Target)` |
| Key | `Instantiation(def, s)` = `(def, Instantiates(def.Decl) ? Erase(s) : null)`; top-level statements (`Decl` null) key null |
| Invariants | Idempotent; interned (reference equality); only interns (no storage preparation, X20); `Erase(Apply(t, s1)) == Erase(Apply(t, s2))` for Substs differing in Origins only; the stored Types of an erased instance equal the erasure of the representative instance's (the property `Layout.Of` relies on; K3 tests it over the library and milestones, (H)) |
| Prohibitions | No proof (Witness, Copy, Sealed, Applies, Retains, `MirCases.Views`) on an erased value; no second Origin sentinel; never rewrites an interned value |
| Cost | Two reference-keyed memos beside the interning tables, never cleared like them; 0 B warm |

`Instantiates` is chapter F's (F3, which K3 needs): true iff the body depends on a non-Origin slot of itself or an enclosing
declaration, false for a specialization. K3 asserts for every admitted item `Erased == null` iff `!Instantiates(Def.Decl)`.

Readers (`Layout.Of`, DropGlue, the collector's part enumeration, codegen) take stored Types through `Instance`, current per pass;
a null stored Type there is `InternalInvariant_Kd` (X20). An `Apply` null at a generic edge is PD30's storage-nesting row; a root's
own Types are never `Apply`'d (the identity returns them, `TyCtxt.cs:235-236`), so K0's nongeneric `Option^32<ref/i32>` local
(storage depth 65, builds today) meets no bound.

### 4.2 Closure contexts as `Subst` (K2; schema shared with chapter F)

As D1 §1.3–1.4 and D2 §2.1: `BoundType.ClosureContext : Subst?`, interned by `CloseClosureTypes` (the list and scan deleted),
`SubstOfClosure` reads the field, `ClosureSignature` goes through `Apply`, Hover's `SubstIdentity` in a new `HoverBuilder`
partial. The bridge `[Transitional("R6-gen")] Binding.ClosureCall(BoundType closure)` is memoized per closure Type for the pass
(X10) for the two legacy readers (`GenericStoragePlan.cs:312`, `BodyLowering.Closures.cs:84`); G7 deletes it. Reconciled with
the old dedup, which keyed by (closure function, enclosing Target, ReturnType, Declaring, Types, lengths, Origins, inputs)
(`Binding.ClosureInstances.cs:54-71`): the closure Type's symbol is the closure function and its context is that Subst, and its
other fields and the ReturnType are functions of the two (H), so "per closure Type" and "per (closure function, Subst)" select the
same CallPlans; the memo is keyed by the closure Type, as X10 says. K2 adds the two-closure regression (two closures of equal result
Type in one generic instance) and bp on the closure families.

### 4.3 `InstanceCollector` and `CheckedSet` (K1, K4a; entry file `B/Instances/InstanceCollector.cs`)

| Item | Contract |
| --- | --- |
| Responsibility | The checked set (SPEC 8.15.3, IMPL 21.3.1): every closed body instance, type-driven item and shim a root requires through runtime or check-only edges; its instance-stage records (limits, capability, layout); at emit the Generated column; the one callee resolution and the one owner of each body item's view for codegen |
| Inputs | A built, validated `MirProgram`; the root-module count (an input range, not a mode); `TyCtxt` (`Compose`, `Apply`, `Erase`, `Instantiates`, `Implementation`, `Witness`, `Comparison`, `Copy`, `Sealed`, `Applies`, `SubstOfClosure`, `SubstOfItem`); `AdtDef`; `MirTypes.MayNeedDrop`; the intrinsic row table's target column (X15); `LegacyRuntimeHelpers` row declarations (X4); `Capability`; `Layout.Of` (P7c, X18) |
| Output | `CheckedSet` per program version (below) |
| Invariants | Deterministic (roots in `MirProgram.Bodies` order, FIFO worklist, targets in template order); one item per key; representatives closed; a repeated run of a version is a no-op; validator findings → empty set; at most one target per MIR site, present exactly at the sites the edge rows name |
| Prohibitions | No syntax, HIR or Plan reads; no edit of the `MirAnalysis` driver; no acceptance decision; no new `IntrinsicId`/`CompilerFunctionKind`/`BindingFailure` (PD21); no proof on erased values; no collector-side needs table (X4); no record for a failure only a Binding error causes (an Opaque operand, an Error Type in a Subst, an unresolved callee: derived, R4-M10's pattern) |
| Run | `Compilation.BuildMir()` after `MirAnalysis.Run`, independent of its results: under sel until G2 (K1's selector), always from G2 |

```
readonly record struct Instantiation(MirDefKey Def, Subst? Erased);                       // PD5, KD1
enum MonoKind : byte { Body, DropGlue, VTable, ObjectDescriptor, ClosureErasure, ItemAdapter, Builtin, PropertyWitness,
                       [Transitional("R8")] LegacyRow }
readonly record struct MonoKey(MonoKind Kind, Instantiation Body, BindingSymbol? Symbol, BoundType? Type, byte Variant);
struct MonoItem   { MonoKey Key; Subst? Representative; BoundType? RepresentativeType; int BodyOrdinal; byte View;
                    int Parent; ushort Depth; SourceRef Site; int FirstTarget, TargetCount; }
struct MonoTarget { int Item; MirLocation At; bool Runtime; }                              // CSR per item, location order
```

| `MonoKind` | Key fields used | Representative | Targets added when admitted |
| --- | --- | --- | --- |
| Body | `Body` = (def, erased Subst or null) | Subst (`Identity(Decl)` for a null key) | its template's sites |
| DropGlue | `Type` = erased Type | the first admitting unerased Type | direct parts' glues (§4.5) |
| VTable | `Type` = erased Type | Type | per `VTableDef` slot, the `Body` `Applies` proves on the representative (F4) |
| ObjectDescriptor | `Type` = erased payload | Type | `DropGlue(payload)`, its `VTable` |
| ClosureErasure | `Type` = erased closure Type | Type | the closure's `Body`, `DropGlue` of the environment |
| ItemAdapter | `Type` = erased item Type | Type | the item's `Body` |
| Builtin | `Type` = erased Self; `Variant` = `CompilerFunctionKind` | Self Type | leaves by `Comparison` (F6) or `Witness` |
| PropertyWitness | `Symbol` = the implementing stored Field; `Type` = erased Self; `Variant` = `Access` (`PropertyWitnessKind` Copy, Borrow or Set): the key (Field, erased Self, `Access`) | Self Type | Set: `DropGlue(the Field's stored Type)` |
| LegacyRow | `Body` = ((row function, Fn), erased Subst) | Subst | the callees and needs its `LegacyRuntimeHelpers` row declares (X4) |

`CheckedSet` API for codegen (chapter C): `Items` (admission order), `Targets(item)`, `View(item)` (the one owner: the single bit of
`MirCases.Views(body, representative)`, computed at admission; zero bits or more than one is `InternalInvariant_Kd`; codegen
selects it with `MirCases.Select`, X13), `TypeOf(item, t)` = `Erase(Apply(case Type of t under View(item), representative))`
(X2; the order of case selection and `Apply` is fixed by K4a's tests, H), `Find(MonoKey)` (e.g. a child's glue for an Open drop
row; a miss is `InternalInvariant_Kd`), `Records`, `Mark(roots)`, `IsGenerated(item)`. Names (`__kimi_f<N>`) are ModuleCodegen's.
Memory ≈ 0.6 MB at 3,000 items (H; K0: Generic 128 has 1,408 Origin-blind instances); 0 B warm.

### 4.4 Roots (K1)

Every `MirBody` with `Source.Module < rootModules` whose key is closed (`Decl` null or `!Instantiates(Decl)`): Fn, Getter, Setter,
Test, Closure of a closed definition, StaticInit of a nongeneric static, DefaultTemplate of a closed function. Bodies of overrides
no instance applies are roots (checked) and not generated (R4-M28). A user Library project's modules are root modules, so its
instance-stage records fail its `check` from G2 (R5-M4; IMPL 21.3.3); a generic body no root instantiates stays symbolic.

The bundled library is not a root in user compilations (P5). A `KimiLibraryShapeTest` row runs the whole collector (roots, edges,
A3b, the instance stage, layout obligations) with every module as a root module and asserts zero records (plan §5.3 item 9). The
DEC OD4 entry states the preconditions under which this equals rooting it per compilation: no library declaration under a
compile-time Condition (SPEC 19.2) and one target profile (windows-x64); a change to either reopens P5. Library instances that user
code requests are collected by edges and reported under KD12. A3b scans root-module bodies only, for the same reason. The design
review is asked to challenge this reading of SPEC 8.15.3 and IMPL 21.3.3 (P5).

### 4.5 Edges

Each template site's Subst is composed with the representative (`TyCtxt.Compose`, `TyCtxt.cs:242`) and admitted (§4.6); each
site has a runtime bit from one `MirReach` walk without CheckTarget edges.

| # | MIR form | Target | Unit |
| --- | --- | --- | --- |
| 1 | Call, Fn callee with a body | `Body` of `Implementation(sym, closed, 0)`, or its getter, setter or default template by `MirProgram.Callee` | K1 (closed), K4a |
| 2 | Call, Fn callee without a body | Shim disposition → `Builtin`; Body disposition → `LegacyRow`, whose row declares its library callees (F2c) and type-driven needs, witness needs included (X4); `#LibraryImport` → none | K6 (witness rows, P1d), C8* |
| 3 | Call, Requirement callee | `Witness(requirement, MirCallee.Contract, closed)` (X16), acted on by the kind table below | K6 |
| 4 | Call, Virtual callee | none (X5): the receiver's `VTable` item holds the entries; codegen reads the slot from F4's `VTableDef` | K8 |
| 5 | Call, Value callee | Closure Type → `Body` under `SubstOfClosure`; ordinary function item → `Body` under `SubstOfItem`, made pure in K5 (the Subst `FunctionItemContext` builds, which passes no Origins, `Binding.FunctionItems.cs:108`, without its cache and storage mutation, `:82, :115`); requirement item → as row 3; Function Type → none | K5, K6 |
| 6 | Intrinsic call, column value `Witness` (`ErasedWriter`) | `Body` of `Witness(the writer requirement, its Contract, closed)` for the user's `BufferWriter` witness; Refused/Limit → rows | K6 |
| 7 | Intrinsic call, column value `StaticInit` (`StaticEnsure`) | StaticInit `Body` through `MirProgram.StaticInit` (F10); a static of a generic container → generic-static-key row | K7 |
| 8 | Intrinsic call, column value `Descriptor` (`MakeObj`, `MakeRc`, `MakeArc`, `Clone`, `TypeTest`) | `ObjectDescriptor(erased payload)` | K8 |
| 9 | Intrinsic call, column value `Glue` (`DestroyValues<T>`, producer-less until R8, PD21) | `DropGlue(T)` when `MayNeedDrop` | K7 |
| 10 | Drop terminator | `DropGlue(TypeOf(place Type))` for every Drop (A2's verdicts ignored for the checked set; codegen reads them) | K7 |
| 11 | `Aggregate(Closure)` | the Closure's `Body` | K5 |
| 12 | `Const(FnItem)` | as row 5 | K5 |
| 13 | `Cast(Erasure)` | `ClosureErasure`/`ItemAdapter` | K5 |

Witness results (rows 2, 3, 5 and 6). `TyCtxt.Witness(requirement, contract, subst)` returns `RequirementTarget(Kind, Target,
Subst, Access)`; `Access` is Binding's existing `PropertyWitnessKind`: `AccessorCall` for every kind except Storage, and Copy,
Borrow or Set for Storage. The table is chapter F's (`F-package.md` §4.5), copied word for word:

| Kind | Target, Subst | K6 action |
| --- | --- | --- |
| Body | A function with a MIR body: a conformance witness (inherited ones included), an accessor-call witness's executing accessor (Getter/Setter key), a fixed-array or integer-position member. Subst is its instance: declaring instance, own Types and lengths per case, translated Origins | Body item through `TyCtxt.Implementation(Target, Subst, 0)` and `MirProgram.Callee` |
| Builtin | A compiler function (comparison, format, arithmetic); Subst binds Self | Builtin shim item; its leaves come from F6 |
| Storage | A standard property witness. Target is the implementing stored Field (it may sit in a base layer, which `AdtDef.FindField` finds from Self); `Subst.Self` is the conforming Self; `Access` is Copy, Borrow or Set | `MonoKind` property-witness item keyed by (Field, erased Self, witness kind), exact `MonoKey` K's (X14); C3c writes it; Set adds a `DropGlue` target for the Field's Type |
| Refused | — | OCC-X capability row (`Unsupported_Kd` at the requesting call) |
| Limit | — | storage-nesting capability row (PD30, R4-M51's code) |

Target column (X15): one column `Target` on the Backend's intrinsic row table (one row per `IntrinsicId`, the table chapter C's
codegen dispatches by), with values `None`, `Witness`, `StaticInit`, `Descriptor`, `Glue` as rows 6–9; every other intrinsic is
`None`. K1 creates the table with this column (its template builder is the first reader); C0b1 adds the writer column; C9b reads
row 6's target and never calls `Witness`. A target at a `None` site or a missing one at another is `InternalInvariant_Kd`.

DropGlue (X3, SPEC 16.3.2, 16.4): `DropGlue(T)`'s targets are the glues of T's direct parts whose Types `MayNeedDrop`, in
destruction order: a struct that declares `drop` → its destructor `Body` only (`AdtDef.DestructorSymbol` under
`TyCtxt.Synthesized`, R4-M6b, PD18); another struct → Fields last to first, then the base; Tuple and fixed array → elements in
decreasing index; enum → each Case's payload elements; closure environment → the remaining captures in reverse initialization order
(X23); Array and Dictionary → the element glue their legacy row declares (X4); `obj`/`rc`/`arc` → `DropGlue(payload)` when
`TyCtxt.Sealed` holds on the representative, else none (the descriptor's finalizer runs the actual Type's glue); string, scalars,
references, raw pointers, Slices, InlineStorage → none. The direct-part enumeration is one function owned by the DropGlue component
(`B/DropGlue.cs`), created by K7 and read by C4a's writer, so collector targets and glue bodies cannot disagree. A type-driven item
is admitted only for a Type whose layout verdict is OK (§4.8): a layout failure is that Type's one record.

### 4.6 `Admit` (K4a)

`Admit(parent, site, key, representative)` returns the item or records one failure; the edge is dropped and everything else
continues (IMPL 21.3.2). In order: (1) `Compose`/`Apply` null → storage-nesting capability row (`Unsupported_Kd`, PD30, R4-M51);
(2) depth > 128, every item; (3) Body items only: 16 ancestors with the same definition and other keys (growing key); (4) Body
items only: the definition already has 1,024 items (X19). (2)–(4) are `GenerationResourceLimit_Kd`. Chain walks only for new keys;
constants without the legacy thread-static override (`GenericStoragePlan.cs:21-23`); recursive defaults are ordinary `Body` edges.

### 4.7 Capability (`B/Capability.cs`; K4a creates, K6, K7, K9a, K9b extend)

| Item | Contract |
| --- | --- |
| Responsibility | The one table of operations and instances the backend does not generate on windows-x64 (IMPL 21.3.3, SPEC 23.1.1), read by A3b and the instance stage; the "capability check" of ARCH "Closed vocabularies" |
| Rows (R5) | Operation rows (K9a): i128/u128 `Div`, `Rem` (compound forms build as the same op), `Cast(Numeric)` i128/u128↔f32/f64 except direct literal conversions. Instance rows: storage nesting past Binding's bound (K4a; G128, R7), OCC-X refusal (K6), generic static storage key (K7; P42 or X22's alternative). At X2 the decided D2 coverage rows (PD11, PD27) |
| A3b | In the body scan of root-module bodies (unused and check-only blocks included), operations whose Types are closed; Opaque skipped, so Binding's gates (`Binding.Expressions.cs:1385`, `Binding.Conversions.cs:796`), which fail the node (`Binding.cs:565-584`) so MIR builds an Opaque without a record (`MirBuilder.cs:409`), never duplicate |
| Instance stage | Template sites whose Types depend on slots, per item through `TypeOf` |
| Prohibitions | No `Supports*`; `Capability.Of(...)` returns the row or none; no row copied elsewhere (Emission's at G7, the old analysis's at H10, Binding's two gates at H7) |
| Records | `Unsupported_Kd`, once per subject (§4.9) |

Generic static storage key (X6, X22): `MirStatic` carries no container Type arguments, so the collector cannot key the StaticInit
body of a static in a generic container's group and reports the row. The old path accepts and builds an immutable scalar there
(`Cache<i64>.Statistics.count`), so from G2 the row rejects at `check` a program that builds today. K7 searches the library, the
milestones and the O2 corpus for the form; if none uses it, the recommendation to the owner is to keep OQ17(b) (generic storage
keys belong to P42) and defer the form under PD27 as a D2 (e) row at G2, recorded in STATUS and PLAN and approved in the PD27 list
at R5's exit; the alternative is `MirStatic` carrying the container's Type arguments before G2. This corrects R5-M13(b)'s premise:
the old analysis also accepts immutable static scalars in generic containers' groups.

### 4.8 Layout obligations (K10a, K10b; X18)

By-value uses per template (locals except DropFlag, `Aggregate` Types, `RawAllocate`/`Stride`/`DestroyValues` Type arguments),
per item `TypeOf`, one verdict per erased Type per version from `Layout.Of` (chapter C, P7c): OK; Limit (size, count or depth) →
`GenerationResourceLimit_Kd` (K10a); CLayout(instance), a C-layout instance (`AdtDef.IsCLayout`, F1) with a zero-sized Field at any
depth → `InvalidCLayout_Kd`, Language, at the use (K10b); any other failure is `InternalInvariant_Kd`. `Layout` is the one owner of
the zero-sized rule; no TyCtxt C-layout view is needed (X18 supersedes X8's third member). Binding's eager report of written
instances stays until G2b, a declared temporary second copy of the rule; under sel between K10b and G2b a written by-value instance
may report at the written syntax and at the use (H; K10b's reproducer decides), declared in K10b's DEC entry with removal at G2b.
Tests: K0's `make(())`, `P.init(1, ())` and the unused-function case with their valid twins; the nested probes
`let x: Option<P<()>> = .None` and inferred `f<T>` with `Option<P<T>>`. K10b may need the held `DiagnosticRequirement.cs` (adv).
Both moves reject at `check` what the old build already refused (K0 Q1, Q4).

### 4.9 Records and the location rule

| Record | Code | Producer |
| --- | --- | --- |
| A3b, instance-stage capability, instance rows | `Unsupported_Kd` | Capability via the collector |
| Depth, growing key, set size, layout limits | `GenerationResourceLimit_Kd` (`DiagnosticCode.tinyhand:774`) | `Admit`, layout pass |
| C-layout zero-sized Field of an instance | `InvalidCLayout_Kd` | layout pass (K10b) |

Location rule (one function, K4a): start at the cause's site (operation, by-value use, requesting site); if it lies in Inlined
scopes whose template is outside the root modules, use the omitting call of the outermost one (H: each scope records its call); if
that site lies in a root module (`Module < rootModules`, as `Compilation.SyntaxOf` distinguishes, `Compilation.Pipeline.cs:139`),
it is the subject; else walk the admission chain (each item's first-admission `Site`, then its parent's) to the first root-module
site. One record per (code, subject). A user generic body reports once at its own operation; a library instance at the user's
request; the library test (all modules roots) in the library. Location changes against legacy are diagnostics, not native
deviations.

### 4.10 Generated (K11)

`Mark(roots)` at emit, after acceptance and `Tests.Discover`, in the generation entry `Core/Compilation.Generation.cs` (X17),
over runtime targets: an Application's startup item (F2a), a test build's selected `#Test` items (P6), a Library output's every
root item except bodies of overrides `TyCtxt.Applies` refutes (R4-M28); statics enter through `staticEnsure` targets; a folded
StaticInit body is not Generated (chapter C). A codegen request outside Generated is `InternalInvariant_Kd`. K12 moves the 23
library-reach `Bodies` reads.

---

## 5. Publication per stage (R5-M4, PD7, PD12)

| Records | R5 sel | R5 `mir` only / legacy | G2 → H3 (every pipeline) | H3 → H5 | H5 on |
| --- | --- | --- | --- | --- | --- |
| MIR construction, validator (R3-1) | check, ControlFlow, decide | mir as today / none | check, decide | in `MirReport` | MIR decides |
| Collector records (§4.9) | check, decide, from K4b | collector not run (PD4) | check, decide | in `MirReport`; H0 names one owner of an `Apply` null (PD30) | as left |
| PD12 refusals | generation, Emission (C4a) | — | generation, Emission (default from G3) | generation | removed |
| Coverage rows (PD11) | generation, Emission, until X2 | — | — | — | — |

Between K1 and C0b1, `build` and `WriteIr` under sel refuse with one typed `Unsupported_Kd` at the startup body's subject (the
first root body's for a Library or test build), within PD11's sel-only exception and never a fallback to legacy codegen; C0b1's
coverage rows replace it. DIAGNOSTICS
at K4b: the generation-time instance paragraph gets its sel note; the "Bounded excerpts" sentence "Resource and output limits do
not change acceptance" is reworded to rendering and output limits (a Resource Error rejects at check, as `OwnershipStorageLimit_Kd`
does); the virtual-descriptor sentence on `GenerationResourceLimit_Kd` is restated for the collector. G2 replaces the
generation-time paragraph for every pipeline.

---

## 6. R6-gen switch contract (G0 records it; D2 §4)

| # | Clause |
| --- | --- |
| S1 | From G2 every `Analyze` on every pipeline runs `BuildMir` (build, validate, the whole `MirAnalysis.Run`, unpublished) and the collector; the sel branch in `BuildMir` goes |
| S2 | G2 publishes construction records, validator findings and collector records at check, with entries superseding R3-1, R4-M1 (for these records) and OD10's split; a Library with an unbuildable generic instance fails `check`; the 20 instance-failure methods assert check-time records; `stage-baselines.json` and `MilestoneSourcesTest` re-baselined with reasons; STATUS gets PD30's G128 (e) row worded as K0 found (`check` accepted the deep finite Subst, the old build already rejected it unlocated with `GenerationResourceLimit_Kd` or `GenerationFailed_Kd`), and PLAN G128's "the old path accepts" is corrected the same way; X22's row if the owner defers the generic static key |
| S3 | PD12's gate wherever MIR codegen runs (`Compilation.Generation.cs`), a typed Emission-partition refusal until H5; `InternalInvariant_Kd` only for a runtime `Group == -1` in a completed body |
| S4 | G2 measures default `check` and warm LSP Analyze time, allocations and retained memory (Generic shape 1/32/64/128) with the warm-Analyze measurement K4b adds (none exists, K0 Q3); mir Allocation tests run on the default pipeline at 0 B |
| S5 | G1 splits `Compilation.Pipeline.cs` (behavior-preserving); G2, G2b and G3 are verified and pushed separately and merge into dev together after G:R6-gen-switch passes |
| S6 | Rollback: a regression in a switch unit is reverted on its branch; `KIMI_CODEGEN=legacy` selectable until G4; G4 only after G3's full sweep on dev |
| S7 | Deletions follow the call graph: G4 → G5, G6 → G7 (lowering, plans, the five collectors, `AnalyzeInstance` and callers, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `Binding.ClosureCall`, Benchmark readers) → G8 → G9 (Emission-only Binding members incl. `SameModuloOpenOrigins` and `PrepareTypeStorage`, each after its grep) → G10 |
| S8 | Prerequisites: X3, R5-M4, R5-M5 with Q4, A-6, A-8, the R4 MIR lane merged; else OQ6's fallback (d) |

---

## 7. Interfaces

### 7.1 With chapter C (S4)

| K provides | C reads | Status |
| --- | --- | --- |
| Per body item `BodyOrdinal`, `Representative`, `View` (the one owner) | selects the view with `MirCases.Select`, never calls `Views` (X13) | decided |
| `Targets(item)`: one per target-bearing site in location order, runtime bit | forward cursor over runtime rows | decided |
| No target for Virtual, `None`-column intrinsic and `#LibraryImport` calls | slot from `VTableDef` (X5), intrinsics from MIR | decided |
| `TypeOf(item, t)` | every instance Type (X2) | decided |
| `Find(MonoKey)`, Generated bits, `Mark` in `Compilation.Generation.cs` | glue of a child (Open rows), generation set (X13, X17) | decided |
| Intrinsic row table with the `Target` column (K1) | C0b1 adds the writer column; C9b reads row 6's target (X15) | **check in C's round 2**: the table is created by K1, before C0b1 |
| `DropGlue` direct-part enumeration (K7, `B/DropGlue.cs`) | C4a's glue writer iterates it (X3) | **check in C's round 2** |
| `PropertyWitness` items (K6) | C3c writes the shim (X14; prerequisites K6, P7b, C4a) | decided |
| Collector records through `MirRecords` | gate step 1 counts them from K4b | decided |
| K needs `Layout.Of` → OK/Limit/CLayout (P7c, X18) and the legacy rows' declarations (P1a–P2b, X4) | — | decided |

### 7.2 With chapter F (S3)

| K needs | F provides | Status |
| --- | --- | --- |
| `Instantiates` exact, in `TyCtxt.Instances.cs` (X8) | F3 | decided |
| `Witness(requirement, contract, subst)` → Body/Builtin/Storage/Refused/Limit, no `BaseDepth`; a Self-bearing `Synthesized` for formatting rows | F5b (X1, X16) | decided |
| `Applies`/`VTableDef` on representatives; `Comparison(contract, self, operators)` leaves | F4, F6 | decided |
| No C-layout view in TyCtxt | X8 named one; X18 moves the rule to `Layout` | **F's round 2 drops it** |
| K2 schema with the bridge memoized per closure Type | X10 | decided |
| F2a startup symbol, P6 test symbols (K11); F2c storage symbols (legacy row callees); F10 `StaticInit` (K7) | as planned | decided |

---

## 8. What becomes unnecessary; size and speed

K2 −40 (+40); G2b −30; G7 ≈ −2,600 of instance machinery; G9 `SameModuloOpenOrigins`, `PrepareTypeStorage`,
`IsInapplicableVirtualBody`, `ImplementationContext`, `IsVerifiedSpecialization`; H7 both Binding i128 gates (division
`Binding.Expressions.cs:1382-1386`, conversion `Binding.Conversions.cs:785-797`, ≈ −20); ratchets 11 → 0, 21 → 0,
`Ownership.Bodies` 183 → 160. Optional R4 open issue: `MirTypes.Agree` as `Erase(a) == Erase(b)` (≈ −30, held file). New code
≈ +1,250 (H; plan +1,100), tests ≈ +1,000. Speed: one scan per body per version, per item one template replay with memoized
`Compose`/`Erase` and one hashed probe per site; legacy's linear scans and per-instance ownership dataflow go; codegen resolves no
callee. Measured at K4b and G2.

---

## 9. Unit mapping and review groups

| Unit | Change against the plan | Prerequisites | Review | Hold |
| --- | --- | --- | --- | --- |
| K0 | Done (counts; timing on a quiet machine, README Q3) | — | — | no |
| F3 | Creates `TyCtxt.Instances.cs` (X8) | S5 | D+R (S3) | adv (one line) |
| K1 | Skeleton; `KIMI_CODEGEN` selector (read once, valid only with `KIMI_PIPELINE=mir`, identity mark, `PipelineSwitchTest` throwing combinations) as the run condition; sel `build` refusal (§5); the intrinsic row table with its `Target` column; DEC component entry and ARCH row "Instance collection" | S5, F3 | G:K-core | adv (`Compilation.Pipeline.cs`) |
| K2 | Bridge per closure Type; two-closure regression | S5 | D+R (S2) | adv |
| K3 | `Erase` in `TyCtxt.Instances.cs`: Static form, null by `Instantiates`, interning only, the commutation and stored-Type properties | K2, F3 | G:K-core | no |
| K4a | **New** (split): composition, representatives, `View`, `Admit` with X19, the location-rule function, records in `CheckedSet` unpublished; creates `Capability` with the storage-nesting row (DEC capability entry, ARCH row); Origin-only variants → one item; finite recursion reuse; G128 twins on both sides of the bound (`pick<Array^64<i32>>` / `Array^65`, which the inline-layout limit does not also hit; `Option^31`/`Option^32<ref/i32>`); 2×/4× counters; warm 0 B | K1, K3 | G:K-core | no |
| K4b | **New** (split): `MirRecords` adds collector records under sel; DEC OD4 entry (D-B with P5's preconditions, the location rule, "from K4b" for R5-M4); ARCH pipeline correction (D-A); DIAGNOSTICS (§5); `CompilerPipeline.md` stage 3; check cost against K0's baseline (stages 2+3+4) and a new warm-Analyze measurement in `src/Benchmark`; zero records over the library test and the 40 milestones under sel | K4a, K0 | G:K-core | adv (`Compilation.Pipeline.cs`) |
| K5 | `SubstOfItem` made pure for ordinary items | K4a | G:K-edges | adv (`TyCtxt.cs`) |
| K6 | Rows 3, 6 and the witness needs of P1d's legacy rows; `Witness` with `MirCallee.Contract`; `PropertyWitness` items from the Storage kind; OCC-X row | K4a, F5b, P1d | G:call | no |
| K7 | Rows 7, 9, 10; DropGlue items with the direct-part enumeration (X3); generic-static-key row and the X22 search | K4a, F10, A-9 | G:K-edges | no |
| K8 | Row 8; `ObjectDescriptor`, `VTable` items | K4a, F4 | G:object | no |
| K9a | Operation rows and A3b (table exists since K4a); literal-conversion test | K4a | G:K-capability (new) | no |
| K9b | Instance-stage capability pass | K9a | G:K-capability | no |
| K10a | `Layout.Of` Limit → Resource record; derived rule for type-driven items | K4a, P7c | G:K-edges | no |
| K10b | CLayout → `InvalidCLayout_Kd` at the use; tests of §4.8; sel overlap declared | K10a, F1 | D+R (S2) | adv if `DiagnosticRequirement.cs` changes |
| K11 | Mark in `Compilation.Generation.cs`; Library output excludes inapplicable overrides | K4a, C0a, C0b1 | G:C-frame | no |
| K12, K13a–c | as planned (K13a in G:K-capability) | — | R; G:K-capability, G:K-edges, G:object | no |
| C0a | Loses the selector to K1; generation entry and gate in `Compilation.Generation.cs` (X17) | S5, K1 | G:C-frame | as chapter C's package states (X17) |
| C3a, C9b, F5b | prerequisite "K4" reads K4a; C9b also needs K6 (X15) | — | — | — |
| C3c | **New** (chapter C): the property-witness shim | K6, P7b, C4a | G:call | no |
| G0 | Records §6 | as plan | D+R (S2) | no |
| G2b | **New:** Binding's eager C-layout instance report and its replay queue deleted (`Binding.Attributes.cs:375-403`, `Binding.Types.cs:801-804`, `Binding.cs:455-456`); written borrow-only uses become valid ((a), SPEC 8.15.3) | G2 | G:R6-gen-switch | no |
| H7 | Also deletes the conversion gate (`Binding.Conversions.cs:785-797`) | H5, K9a | G:check-del | no |

Plan text changes: §5.3 item 1 names "G2, G2b and G3"; every "K4" prerequisite names K4a or K4b as above. Groups: **G:K-core** K1,
K3, K4a, K4b; **G:K-edges** K5, K7, K10a, K13b; **G:K-capability** K9a, K9b, K13a (after G:K-core, independent of G:K-edges);
G:call F5b, K6, C3a, C3b, C3c; G:object F4, K8, K13c, C6a–C6c; G:C-frame E10, E15, C0a, C0b1, C0b2, K11; **G:R6-gen-switch** G2,
G2b, G3; G:R6-gen-del G4–G10. Outside groups: K2, K10b, G0 (D+R S2); K12, F10, G1 (R); K0 (—).

---

## 10. Departures from owner-approved texts (owner's summary)

| ID | Departure | From | Reason |
| --- | --- | --- | --- |
| D-A | A3b runs in the Backend collector's body scan before acceptance, not as a Mir P7 pass | ARCH "Target pipeline" P7; PLAN10 §5.1, §5.5 | Mir analyses may not read the Backend's table; the driver is R4's (PD6); one walk serves both stages |
| D-C | Instance-stage `Unsupported_Kd` causes beyond operators (storage nesting, OCC-X, generic static key) are capability rows | PLAN10 §5.7 | ARCH admits Unsupported only from Binding, MIR construction or the capability check; one code per cause (PD30, R4-M51) |
| KD1 | The key's Subst is nullable and gated by `Instantiates` | PD5's wording `Instantiation(MirDefKey, TyCtxt.Erase(Subst))` | Roots and edges agree by construction |
| — | R5-M4's "collector records publish from K4" is met by K4b | R5-M4 | K4's split; same stage |
| X22 | R5-M13(b)'s premise is corrected: the old analysis also accepts immutable static scalars in generic containers' groups | R5-M13(b) | K0-era finding; owner chooses the deferral (recommended) or `MirStatic` Type arguments before G2 |

Not departures: D-B (P5) gives the records SPEC requires per unit under its preconditions; the erased form fills a form PLAN10
§5.7 left open; pulling `ClosureContext` as `Subst` forward follows A5P §9.1.

## 11. Owner items

- Approve the package (OQ4) with D-A, D-C and KD1's wording against PD5.
- New representations approved with the package (no ARCH vocabulary entry): the final `MonoKind` list (Body, DropGlue, VTable,
  ObjectDescriptor, ClosureErasure, ItemAdapter, Builtin, PropertyWitness, LegacyRow), the capability rows and the intrinsic
  `Target` column.
- Temporary structures: `MonoKind.LegacyRow` (R8); K10b's double report under sel (until G2b); Binding's eager C-layout check as a
  second copy of `Layout`'s rule (until G2b); the sel `build` refusal (K1 to C0b1).
- Acceptance changes: from G2, K10b's `InvalidCLayout_Kd` is a Language error at check and K10a moves the layout-depth limit to
  check (both programs never built, K0); a Library with an unbuildable generic instance fails `check` (R5-M4); at G2b written
  borrow-only C-layout instances become valid ((a)).
- X22: keep OQ17(b) and defer the generic static storage key under PD27 if K7's search finds no user (recommended), or have
  `MirStatic` carry the container's Type arguments before G2.
- K10b may need the held `DiagnosticRequirement.cs`. No new diagnostic code; `GenerationResourceLimit_Kd` becomes located.

## 12. Asks to the R4 stream

| ID | Ask | Needed by |
| --- | --- | --- |
| A-9 | (unchanged) `MayNeedDrop` signature change merged | K7, C4a |
| A-11 | G91's per-query cut: merged at `5ae8ebb7`; no longer a K7 prerequisite (X21) | — |
| open issue | `MirTypes.Agree` may become `Erase(a) == Erase(b)` once K3 lands (optional) | — |
| H0 | (unchanged, PD30) one owner of an `Apply` null when `MirReport` absorbs collector records | H3 |

## 13. Risks

| Risk | Response |
| --- | --- |
| Collection on every Analyze from G2 (LSP included); no timing yet, no warm-Analyze benchmark | K0's quiet-machine commands; K4b adds the measurement; G2 records it; warm 0 B asserted; owner accepted (R5-M4) |
| An Origin-reading proof distinguishes Origin variants of one key, or an erased instance's stored Types differ from the erased representative's (`Layout.Of` would disagree with `TypeOf`) | K3's property tests over library and milestones; a finding is a SPEC gap report, never a collector rule |
| Glue growth through a non-regular recursive Type behind heap storage passes no Body (if such Types are valid, H) | the depth limit applies to every item (X19), so collection terminates with a Resource record |
| Limits reached by a valid program | K0 margins large; legacy values kept |
| Legacy wrong code from the closure bridge | per-closure-Type memo; two-closure regression; bp |
| Written C-layout instance reported twice under sel (K10b → G2b) | declared, sel-only; G2 and G2b merge together |
| The P5 preconditions change (a library Condition, a second target) | stated in DEC OD4; reopening P5 is the response |
| The generic static key row rejects at check a program that builds today | X22's owner decision before G2; K7's search |

## 14. Open points for the planning session

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| P9 | Where does the intrinsic row table with the `Target` column live, given K1 precedes C0b1? | (a) K1 creates `B/IntrinsicRows.cs` (33 rows, `Target`), C0b1 adds the writer column; (b) the column waits for C0b1 and K6–K8 depend on it | (a): no cross-group order; one table |
| P10 | Owner of DropGlue's direct-part enumeration, needed by K7 before C4a | (a) `B/DropGlue.cs`, created by K7, read by C4a; (b) a collector-private enumeration and C's own | (a): one rule, so targets and glue bodies agree |
