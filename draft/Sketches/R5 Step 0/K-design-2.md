# R5 step 0, chapter K, design 2: instance key, collection before acceptance, R6-gen switch contract

Status: second independent design of chapter K (plan S2), for the S2–S4 design review and the owner's one look (OQ4, R5-M1).
Written without reading any other design of this chapter (deep dives or `K-design-1`); no `audit.md` existed on `origin/dev`
or `claude/r5-s1` when this was written.

Base: `origin/dev` at `041ced79`. Code citations are `path:line` at that commit; `C/` = `src/Kimi/Compiler/`, `B/` =
`src/Kimi/Compiler/Backend/` (new), `T/` = `tests/xUnitTest/Tests/`. "Plan" is the R5 and R6 Implementation Plan; PD, OQ and
unit names are its. R5-M1–R5-M13 are the owner's answers of 2026-10-18 as the R5 records unit writes them. "sel" is
`KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir` (PD4). (H) marks an estimate or an inference not checked by running code.

---

## 0. Decisions in one screen

| # | Decision | Section |
| --- | --- | --- |
| K-D1 | Instance key `Instantiation(MirDefKey, Subst? Erased)`; `TyCtxt.Erase` maps every Origin to `BoundOrigin.Static` and keeps arities, so an erased Subst is still a well-formed Subst; null when no static argument remains. Proofs run on one representative unerased Subst per item (erase late) | 2.1 |
| K-D2 | One Backend component, `InstanceCollector`, publishes one table, `CheckedSet`: items (body instances, type-driven items, shims) with a closed `MonoKind`, edges in MIR location order with a runtime bit, located records, and the Generated column. Codegen never composes a Subst or resolves a callee: it reads each call site's target from the table | 2.2 |
| K-D3 | One pass per body per program version builds the body's template (edges, closed capability sites, open capability sites, by-value Types); instances only replay templates | 2.2, 2.4 |
| K-D4 | Roots: every body of the compilation's source modules whose definition `TyCtxt.Instantiates` says is closed. The bundled library's own checked set is verified by a test that passes the library as a root module, not recollected in every compilation | 2.3 |
| K-D5 | One `Admit` decides all four limits in order: `Apply`/`Compose` null (PD30, `Unsupported_Kd`), depth 128, growing key 16, per-definition set 1,024 (`GenerationResourceLimit_Kd`); legacy's values, now located and hashed | 2.5 |
| K-D6 | `B/Capability.cs` is the one table of what the backend cannot generate: i128 `/` `%`, i128↔float, and the instance-stage gaps (G128 storage nesting, OCC-X witness, generic-container static); A3b and the instance stage read it; X2 adds the decided D2 rows | 2.6 |
| K-D7 | One location rule for every instance-stage record: the cause's site when it lies in a source module, else the nearest source-module site up the admission chain (SPEC 23.1.1 "including an instantiation", mirroring SPEC 22.5.1) | 2.8 |
| K-D8 | Generated is a mark pass at emit over runtime edges from the output's roots; nothing outside it reaches codegen (a request outside it is `InternalInvariant_Kd`) | 2.9 |
| K-D9 | Collector records join `MirRecords` in the ControlFlow partition (PD7, R5-M4): under sel from K4b, on every pipeline from G2, folded into `MirReport` at H3 | 3 |
| K-D10 | R6-gen switch: G2 (MIR, driver and collector in every Analyze; records at check), G2b and G3 (MIR codegen by default) are one review group with one rollback rule; deletions follow the call graph G4 → G5/G6 → G7 → G8 → G9 → G10 | 4 |

Plan changes proposed (section 6): K4 split into K4a/K4b; a new group G:K-capability (K9a, K9b, K13a) out of G:K-edges; a new
unit G2b (Binding's eager C-layout instance report deleted once the collector runs on every pipeline); F3 into
`TyCtxt.Erasure.cs` (no `TyCtxt.cs` hold); H7 also deletes Binding's i128↔float gate.

---

## 1. Required meaning

| Rule | Text (short) | Forms it governs here |
| --- | --- | --- |
| SPEC 3.3.1, 8.13.3 | Runtime identity and the specialization selection key exclude Origins only; `obj/D` and `rc/D` differ; Semantics layers distinguish | Instance key, descriptor token (PD17) |
| SPEC 8.13.3 | Once arguments determine a key, the matching specialization runs, also after generic substitution | Fn edges select through `TyCtxt.Implementation` |
| SPEC 8.15.3 | Layout obligations (C-layout zero-sized Fields, alignment and size) are checked at by-value uses for every instantiation a checked nongeneric body requires, directly or transitively, reachable or not, generated or not; failure is a Language error at the use; a limit is Resource; polymorphic recursion is valid and may hit a Resource limit | Roots, check-only edges, layout pass, `Admit` |
| SPEC 23.1.1 | Unsupported at the concrete use, "including an instantiation"; Resource never rules a program invalid | Capability records, location rule |
| SPEC 23.1.2 | Each independent failure is reported; grouping and derived records are implementation-defined | Merge per (code, subject); derived-drop rule |
| SPEC 23.2.2 | A Completed result reports every Language and Proof error of the checked set's concrete instances; an accepted program never fails `build` with a Language or Proof error; whether `check` reports Unsupported is implementation-defined, but covers the same checked set | Collection before acceptance (OD4), publication at check from G2 (R5-M4) |
| SPEC 16.3.2–16.3.3 | Destruction order; a declaring struct's `drop` then Fields then base; handles release; Sealed payloads | DropGlue edges (PD18, R4-M6b) |
| SPEC 22.4.3–22.4.4 | Per-slot initialization on first access; static storage keys erase Origins only | `staticEnsure` edges; generic-container statics stay a capability gap until P42 (R5-M13 b) |
| SPEC 22.5.1 | A failure inside a Kimi operation reports the invoking user expression | Model for the location rule (K-D7) |
| IMPL 21.3.1 | Monomorphize per closed instantiation the output requires; checks cover the whole checked set | Generated ⊆ checked set |
| IMPL 21.3.2 | Instantiation growth, context depth and analysis storage are implementation-defined Resource limits at the affected declaration, call or token, never stopping independent checks | `Admit`, located records |
| IMPL 21.3.3 | i128 `/`, `%` and conversions to or from f32/f64 are Unsupported at the concrete use for every instantiation of the checked set, unexecuted branches included | Capability table, A3b, instance stage |
| IMPL 21.1.1, 21.1.3 | Layout rules, C mode, zero-sized values, the 8-byte inline closure environment | Layout pass (with S4's `Layout.Of`) |

Current state the design replaces (all at generation, after acceptance):

- Every nongeneric body in `Ownership.Bodies` is a root and is generated (`C/Emission/LlvmEmitter.cs:221-237`); instances come from
  five collectors totalling 2,054 lines: `GenericStoragePlan*` (607 + 88 + 196), `ObjectGenerationPlan` (346), `VirtualGenerationPlan`
  (398), `DefaultGenerationPlan` (172), `CompilerFunctionAdapters` (247); each generic instance is re-analysed by `AnalyzeInstance`
  (`C/Analysis/OwnershipAnalysis.Instances.cs:49-94`, called from `LlvmEmitter.cs:429`, `LlvmEmitter.Defaults.cs:15`).
- Keys are CallPlan references deduplicated by a linear scan with `SameModuloOpenOrigins` (`GenericStoragePlan.cs:434-443`,
  `DefaultGenerationPlan.cs:76-78`); closure contexts by a never-cleared linear scan that compares Origins
  (`C/Binding/Binding.ClosureInstances.cs:9, 54-71`).
- Limits: growing key 16 over the ancestor chain, depth 128, 1,024 entries per template (`GenericStoragePlan.cs:17-19, 363-379,
  396-400, 446-452`); storage depth 64 returns null (`C/Binding/Binding.Structs.cs:54-57`), an Internal issue in `AnalyzeInstance`.
  Failures are published in the Emission partition at the project file, not at a call (`LlvmEmitter.cs:67-79`).
- Inferred C-layout zero-sized instances fail only in Emission (`C/Emission/AggregateLayout.cs:336-340`); written ones are
  `InvalidCLayout_Kd` from Binding at the generic syntax (`C/Binding/Binding.Attributes.cs:384-403`, `Binding.Types.cs:801-804`).
- A Library `build` never generates (`src/Kimi/SolutionAndProject/Project.cs:212-213`); `kimi test` turns a generation failure into
  a string without a diagnostic (`src/Kimi/Unit/Command/TestCommand.cs:131-135`).

---

## 2. Components

### 2.1 Instance key (K2, K3, F3)

**`TyCtxt.Erase(Type)` and `TyCtxt.Erase(Subst)`** — `C/Binding/TyCtxt.Erasure.cs` (new partial, PD20), owner Binding (TyCtxt).

| Item | Contract |
| --- | --- |
| Responsibility | The Origin-erased form of a Type and of a substitution (SPEC 3.3.1, 8.13.3): the one identity rule for instances, descriptors and layouts |
| Rule | Every Origin, in a Type's `Origin` and `OriginArguments` at any depth, in a closure context and in a Subst's `Origins` and `Inputs`, becomes `BoundOrigin.Static` (`C/Binding/Binding.Origins.Model.cs:125`); arities, Semantics, lengths, symbols, `Self` and `Declaring` are kept. `Erase(Subst)` is null when the Subst binds no Type, length, `Self` or `Declaring` slot (an Origin-only Subst is the nongeneric case) |
| Invariants | Idempotent; `Erase(Apply(t, s1)) == Erase(Apply(t, s2))` for Subst differing in Origins only (reference equality, interned); an erased Subst is well formed, so `Apply(t, Erase(s))` is defined (K3 tests that it equals `Erase(Apply(t, s))`, (H)) |
| Prohibitions | No proof (Witness, Copy, Sealed, Applies, Retains) runs on an erased Subst or Type: they read Origins (PLAN10 OJ3). No second Origin sentinel |
| Cost | Two reference-keyed memos beside the interning tables, never cleared like them (`C/Binding/TyCtxt.cs:13-16`): one entry per distinct Type or Subst erased; a warm rebuild only hits them, 0 B |

Why `static` and not a new "erased" Origin: no new vocabulary, `OriginArguments` cannot hold null, and erased values are keys and
layout inputs only (the prohibition above), so the Origin's meaning is never read.

**Representative** (erase late, PD5): each item keeps the first unerased closed Subst (or Type) that admitted it, in the
deterministic worklist order. Witness, Copy, Sealed and Applies run on it. Codegen derives an instance's Types as
`Erase(Apply(t, representative))`. Origin-only variants of one key reach one item; SPEC 3.3.1 makes proofs that could tell them apart
irrelevant to generated code (risk R3).

**Closure contexts as Subst (K2, A5P-C1)** — design for its D+R under S2:

- `BoundType.ClosureContext` becomes `Subst?` (`C/Binding/BindingModel.cs:517`); `TyCtxt.Intern(..., closureContext: Subst?)`
  (`C/Binding/TyCtxt.cs:83`); `SubstOfClosure` returns `closure.ClosureContext ?? Identity(binder)` (`TyCtxt.cs:208-221`).
- `CloseClosureTypes` interns the context with the existing private `Intern(target, self, declaring, types, lengths, origins,
  inputs)` (`TyCtxt.cs:292`), so interning replaces the list and its scan (`Binding.ClosureInstances.cs:9, 54-71`); the CallPlan's
  `ReturnType` field is dropped because `Apply(signature result, context)` determines it. `ClosureSignature` becomes
  `Apply(closure.Signature, context)` (`Binding.ClosureInstances.cs:11-13`).
- The 26 `ClosureContext` references in Binding, `C/Mir/MirTypes.cs:83` and `src/Kimi/Checking/Hover/HoverBuilder.cs:601` keep
  reference identity; Hover's `CallIdentity(context)` becomes a `SubstIdentity` in a new `HoverBuilder` partial.
- `Binding.ClosureCall(type)` `[Transitional("R6-gen")]` materializes the old CallPlan for the two legacy readers
  (`C/Emission/GenericStoragePlan.cs:312`, `C/Emission/BodyLowering.Closures.cs:84`), memoized per Subst for the pass so legacy warm
  generation stays 0 B; G7 deletes it with its readers. bp on the closure families proves legacy `.ll` unchanged.

**`TyCtxt.Instantiates(BindingSymbol)` (F3)** — whether a definition's body exists per closed instantiation: own generic slots,
generic slots of its container, or (a Closure) an enclosing definition that instantiates; a specialization never does (SPEC
8.13.1). Read from the declaration table (`Schema.GenericSlots`, `Scope`), replacing the syntax-only
`FunctionKoto.RequiresInstantiation` (`C/Parsing/Koto/Declarations/FunctionKoto.cs:87-89`, 4 references) with one rule; placed in
`TyCtxt.Erasure.cs` beside `Erase` (one concern: instance identity), so F3 needs no `TyCtxt.cs` hold.

### 2.2 `InstanceCollector` and `CheckedSet` (K1, K4a)

Files: `B/Instances/Instantiation.cs` (key and item rows), `B/Instances/CheckedSet.cs` (the published table),
`B/Instances/InstanceCollector.cs` (contract entry), `B/Instances/InstanceCollector.Templates.cs` (the body scan),
`B/Instances/InstanceCollector.Items.cs` (type-driven items and shims). Owner phase: Backend, before acceptance.

| Item | Contract |
| --- | --- |
| Responsibility | The checked set of SPEC 8.15.3 and IMPL 21.3.1: every closed body instance, type-driven item and shim that a root requires through runtime or check-only edges, with the edges, the instance-stage records (limits, capability, layout) and, at emit, the Generated column |
| Inputs | A built and validated `MirProgram` (bodies, `Callee`), the root modules (a count of source modules), `TyCtxt` (Compose, Apply, Erase, Implementation, Witness, Copy, Sealed, Applies, SubstOfClosure, SubstOfItem, the C-layout view), `AdtDef`, `Capability`, `Layout.Of` (S4, P7) |
| Output | `CheckedSet` for the program version: `Items` (ordinal = admission order), per item its targets in MIR location order, `Records` (located `Problem`s, one per code and subject), `Generated` bits (K11) |
| Invariants | Deterministic: roots in `MirProgram.Bodies` order (`C/Mir/Build/MirBuilder.cs:52-60`), a FIFO worklist, edges in template order; one item per key; every admitted item's representative is closed; a repeated run of the same version is a no-op (as `C/Mir/Analysis/MirAnalysis.cs:47-50`); a program with validator findings yields an empty set (its `InternalInvariant_Kd` already rejects) |
| Prohibitions | No syntax, HIR or Plan reads (ARCH dependency rules); no edit of the `MirAnalysis` driver (PD6); no acceptance decision of its own (it records; `Compilation` decides); no new `IntrinsicId`, `CompilerFunctionKind` or `BindingFailure` (PD21); no proof on erased values; no record for a failure that only a Binding error causes (an edge whose Subst holds an Error Type, or a callee Binding did not resolve, is dropped as derived, R4-M10) |
| Run | `Compilation.BuildMir()` runs it after `MirAnalysis.Run` (`C/Core/Compilation.Pipeline.cs:145`) and independent of the analyses' results: under sel until G2, always from G2 |

Representation (one row type per concept):

```
readonly record struct Instantiation(MirDefKey Def, Subst? Erased);          // the body-instance key (PD5)
enum MonoKind : byte { Body, DropGlue, VTable, ObjectDescriptor, ClosureErasure, ItemAdapter, Builtin, LegacyRow }
readonly record struct MonoKey(MonoKind Kind, Instantiation Body, BoundType? Type, CompilerFunctionKind Builtin);
struct MonoItem { MonoKey Key; Subst? Representative; BoundType? RepresentativeType; int BodyOrdinal /* -1 */;
                  int Parent; ushort Depth; SourceRef Site; int FirstTarget, TargetCount; }
struct Target { int Item; MirLocation At; bool Runtime; }                    // CSR, in MIR location order per body
```

- `MonoKind` is a Backend enum, not an ARCH closed vocabulary: it lists PLAN10 §5.7's MonoItems with PD3's shims as Backend items.
  `Body` covers every `MirBodyKind` (Fn, Closure, Getter, Setter, StaticInit, DefaultTemplate as recursive defaults, Test);
  `LegacyRow` is a compiler function with a Body disposition and no Kimigayo body yet (`C/Binding/KimiLibraryKinds.cs:170-205`),
  served by `LegacyRuntimeHelpers` until R8; `Builtin` the four Shim kinds (`KimiLibraryKinds.cs:199`). Entry and TestEntry are
  not items: they are written at emit around Generated roots (S4).
- Codegen reads a call's callee as `Target` at its `MirLocation` with a forward cursor; a miss is `InternalInvariant_Kd`. Callee
  resolution (`Implementation`, `Witness`, closure and item contexts) therefore has one owner.
- Memory (H): an item ≈ 72 B, a target 16 B, a lookup entry ≈ 48 B; 3,000 items and 15,000 targets ≈ 0.6 MB retained per
  compilation. Arrays, the lookup dictionary and templates are pooled per compilation and reset by counts; a warm rebuild of the
  same program allocates nothing (K1 and K4a `Purpose=Allocation` tests). `Apply` and `Compose` are 0 B warm (interning hits), as
  A4's warm tests already rely on through `MirCases.Views` (`C/Mir/MirCases.cs:93`) (H).
- Time: one scan per body per version (O(statements)), then per item O(its template): `Compose` + `Erase` (memo) + one hashed
  lookup per edge. Legacy's quadratic scans disappear.

### 2.3 Roots

| Root | Rule |
| --- | --- |
| Bodies of source modules | Every `MirBody` with `Source.Module < SourceModules.Length` (the library is the next module, `Compilation.Pipeline.cs:139-140`) whose `Key.Decl` is null (top-level statements) or `!TyCtxt.Instantiates(Key.Decl)`: Fn, Getter, Setter, Test, Closure of a closed definition, StaticInit of a nongeneric static (SPEC 22.4.3: every initializer is checked), DefaultTemplate of a closed function (the analyses check it once; it is generated only when a recursive default calls it) |
| The bundled library | Not a root in user compilations: its checked set is closed and edit-independent, so a `KimiLibraryShapeTest` row runs the collector with every module as a root module and asserts zero records (the plan's "zero collector records over the library", §5.3 item 9). Records equal what rooting it per compilation would give, at no per-Analyze cost |
| Tests | In a Test unit, `#Test` bodies are ordinary closed roots; Generated decides what is written (2.9) |

The root module count is an input range, not a mode: user compilations pass `SourceModules.Length`; the library test passes all.

### 2.4 Edges per MIR form (K1, K4a, K5–K8)

Each body's template lists its edge sites once per version, in location order, with the site's runtime bit from one runtime walk of
`MirReach` (`C/Mir/MirReach.cs:48`, without CheckTarget edges); check-only sites stay in the checked set and are never Generated.
Per item the inner Subst is composed with the item's representative (`TyCtxt.Compose`, `C/Binding/TyCtxt.cs:242-273`; identity for
a closed body) and admitted (2.5).

| # | MIR form | Edge | Unit |
| --- | --- | --- | --- |
| 1 | Call, Fn callee, callee with a body | `Body` item of `TyCtxt.Implementation(symbol, closed, 0)` (the specialization a closed key selects, `C/Binding/Binding.Specializations.cs:226-245`), its getter, setter or default template by `MirProgram.Callee` (`C/Mir/MirProgram.cs:58-59`) | K1 (closed), K4a (generic) |
| 2 | Call, Fn callee without a body | Shim disposition → `Builtin(kind, erased Subst)`; Body disposition → `LegacyRow(kind, erased Subst)` with the type-driven needs its row declares (element glue, comparison or format witnesses; one needs table in `InstanceCollector.Items.cs`, a row per family added by C8a–C9c); a `#LibraryImport` → no item (an external symbol) | K6, C8*, C9* |
| 3 | Call, Requirement callee | `TyCtxt.Witness(requirement, closed)` (F5b) → row 1 or row 2's `Builtin`; the OCC-X refusal → capability gap | K6 |
| 4 | Call, Virtual callee | No edge at the call: the receiver's `VTable` item (row 7) holds the entries | K8 |
| 5 | Call, Value callee | Operand Type after `Apply`: Closure → its `Body` under `SubstOfClosure` (closed by `Apply`, after K2); function item → `Body` of the item's function under `SubstOfItem` and `Implementation`; Function Type value → no edge (row 12 made its adapter) | K5 |
| 6 | Call, `staticEnsure` | The StaticInit `Body` through `MirProgram.StaticInit(body, operand)` (F10; A4's inline copy `C/Mir/Analysis/MirEffects.cs:159-160` is R4's); a static that instantiates (a generic container's) → capability gap until P42; the edge does not depend on C7's slot states (SPEC 22.4.3's four, R5-M13 b) | K7 |
| 7 | Call, `MakeObj`/`MakeRc`/`MakeArc`/`Clone`/`TypeTest` | `ObjectDescriptor(erased payload Type)` and, when it has virtual slots, `VTable(erased Type)` | K8 |
| 8 | Call, `DestroyValues<T>` | `DropGlue(T)` when `MirTypes.MayNeedDrop(T)` | K7 |
| 9 | Drop terminator | `DropGlue(Erase(Apply(place Type, representative)))` (a pair slot holds the whole Type, so `Apply` closes the case) for every Drop, A2's verdicts ignored for the checked set (Generated reads only runtime sites; codegen reads the verdicts) | K7 |
| 10 | Rvalue `Aggregate(Closure)` | The Closure's `Body` under its closed context | K5 |
| 11 | Operand `Const(FnItem)` | The item's `Body` (as row 5) | K5 |
| 12 | Rvalue `Cast(Erasure)` | `ClosureErasure(erased closure Type)` or `ItemAdapter(erased item Type)` | K5 |

Type-driven items and shims add their own targets when admitted, from the representative Type:

| Item | Targets |
| --- | --- |
| `DropGlue(S)`, S declares a `drop` | The destructor `Body` (`AdtDef.DestructorSymbol`, `C/Binding/AdtDef.cs:132`) under `TyCtxt.Synthesized(destructor, S, [])`; nothing else: the destructor body drops Fields and base (R4-M6b, PD18) |
| `DropGlue(T)`, other T | One target per part the one destruction walk reports (`MirTypes.Destruction`, `C/Mir/MirTypes.Destruction.cs:68`): a declaring struct's glue, a handle's glue, Array and Dictionary element glue; a part past the walk's depth bound → `GenerationResourceLimit_Kd` (risk R6) |
| `DropGlue(obj/rc/arc P)` | `DropGlue(P)` when `TyCtxt.Sealed(null, P)` on the representative; else none (the descriptor's finalizer runs the actual Type's glue) |
| `ObjectDescriptor(T)` | `DropGlue(T)` (its destroy entry); its `VTable(T)` |
| `VTable(T)` | Per slot of `VTableDef` (F4) the `Body` of the implementation `TyCtxt.Applies` proves on the representative; an override no instance applies stays a checked definition, never an item (R4-M28) |
| `ClosureErasure(C)` / `ItemAdapter(I)` | The closure's or item's `Body`; `DropGlue` of the environment |
| `Builtin(kind, s)` | Leaf witnesses by `TyCtxt.Comparison` (F6) or `Witness` (format) |

Twelve MIR edge forms (metric M6) plus seven item rows; a new form adds a row (AGENTS "Under a canonical table a new form adds a
row").

### 2.5 `Admit`: the four limits in one place (K4a)

`Admit(parent, site, key, representative)` returns the item ordinal or records one failure; the edge is then dropped and every
other edge and item continues (IMPL 21.3.2: independent checks go on).

| Order | Condition | Record | Basis |
| --- | --- | --- | --- |
| 1 | `Compose` or `Apply` returned null (Binding's storage depth 64, G128) | `Unsupported_Kd`, capability gap "storage nesting" | PD30, R4-M51's code for the same cause; R7 removes the bound |
| 2 | `parent.Depth + 1 > 128` | `GenerationResourceLimit_Kd` | Legacy `ContextDepthLimit` |
| 3 | 16 ancestors on the admission chain have the same kind and definition (a body's `MirDefKey`, a type-driven item's nominal symbol), each with another key, since equal keys hit the memo | `GenerationResourceLimit_Kd` | Legacy `GrowingKeyLimit`; SPEC 8.15.3's polymorphic recursion |
| 4 | The definition (as in 3) already has 1,024 items | `GenerationResourceLimit_Kd` | Legacy per-template `SubstitutionSetLimit` |

- The record's subject follows the location rule (2.8) from `site`; merged per (code, subject), so a growing recursion reports once
  at its recursive call. Legacy reported at the project file (`LlvmEmitter.cs:77`): a located record is an improvement, (d).
- Chain walks happen only for new keys (≤ 128 steps); a memo hit costs one lookup. Recursive default templates (legacy
  `DefaultGenerationPlan.cs:94-99`: 1,024 per declaration, depth 16) become ordinary `Body` edges under the same `Admit`.
- Limits are constants (no thread-static override as `GenericStoragePlan.cs:21-23`); the set-limit test generates 1,025 instances.

### 2.6 Capability table, A3b and the instance stage (K9a, K9b)

`B/Capability.cs` (contract entry), canonical component "Capability" (ARCH row, DEC capability entry implementing R4-M8).

| Item | Contract |
| --- | --- |
| Responsibility | The one table of operations and instances the backend does not generate on windows-x64 (IMPL 21.3.3, SPEC 23.1.1), read at the definition stage (A3b) and the instance stage |
| Rows (R5) | i128/u128 `Div` and `Rem` (compound forms build as the same `BinaryOp`); `Cast(Numeric)` between i128/u128 and f32/f64; instance-stage gaps: storage nesting past Binding's bound (G128, R7), the OCC-X witness refusal (PLAN10 §16-2), a static that instantiates (P42, R5-M13 b); at X2 the decided D2 coverage rows (PD11, PD27) |
| A3b | Over every body (unused bodies and check-only blocks included), each operation whose operand and target Types are closed: a row → `Unsupported_Kd` at the operation, once per location. Opaque values are skipped, so Binding's gate (`C/Binding/Binding.Expressions.cs:1381-1386`, `Binding.Conversions.cs:785-797`), which makes the node an Error and the MIR an Opaque, never duplicates; H7 deletes both gates (the plan's H7 names only the division gate: correction) |
| Instance stage | The template lists operations whose Types depend on the body's slots; per item, `Erase(Apply(type, representative))` against the rows; `Unsupported_Kd` per the location rule, once per subject |
| Prohibitions | No `Supports*` boolean (ARCH "Capability gates"): `Capability.Of(...)` returns the row or none; no second copy of a row elsewhere (the legacy i128 sites go with their owners: Emission's at G7, the old analysis's at H10, Binding's gates at H7) |

A3b runs inside the collector's body scan (one walk per body yields edges, closed capability sites, open capability sites and by-value
Types), so it stays in the Backend with its table. This moves ARCH's P7 "definition-stage capability check" into the Backend's
pre-acceptance pass (departure D-A, section 5.2).

### 2.7 Layout obligations (K10a, K10b)

- By-value uses in the template: every local's Type (Return, Arg, Var, Temp; not DropFlag), each `Aggregate`'s Type, and the Type
  argument of `RawAllocate`, `Stride` and `DestroyValues`; one entry per distinct Type per body, with its first site.
- Per item: `Erase(Apply(t, representative))`; one verdict per erased Type per version (a memo), from two owners:
  - `Layout.Of` (S4, P7c): typed limit results (size, count, depth) → `GenerationResourceLimit_Kd` (Resource, IMPL 21.3.2);
  - Binding's C-layout rule published as a `TyCtxt` view (K10b; the logic of `Binding.CheckCLayoutInstance`,
    `C/Binding/Binding.Attributes.cs:384-403`) → `InvalidCLayout_Kd` (Language, SPEC 8.15.3), whose catalog entry gains the
    ControlFlow requirement.
- Subjects follow the location rule from the use's site, one record per (code, subject).
- Binding's eager report at written generic syntax stays on the default pipeline until G2 (the collector does not run there before
  G2). It also fires behind borrows, which SPEC 8.15.3 exempts (H: `BindConstructedType` checks every written C-layout instance).
  G2b deletes it when the collector runs on every pipeline: one reporter, at the uses SPEC names. Under sel between K10b and G2b a
  written by-value instance may report at both sites (H; K10b's reproducer decides): a declared sel-only overlap with its removal
  at G2b, recorded in K10b's DEC entry.

### 2.8 Records and the location rule

| Record | Code | Category | Producer |
| --- | --- | --- | --- |
| A3b, instance-stage capability, capability gaps (G128, OCC-X, generic static) | `Unsupported_Kd` | Unsupported | Capability via the collector |
| Depth, growing key, set size, layout limits, destruction depth | `GenerationResourceLimit_Kd` (`src/Kimi/Diagnostics/DiagnosticCode.tinyhand:774`) | Resource | `Admit`, layout pass |
| C-layout zero-sized Field of an inferred instance | `InvalidCLayout_Kd` | Language | layout pass (K10b) |

Location rule (K-D7): a record's subject is the site of its cause (the operation, the by-value use, the requesting call) when that
site lies in a source module; otherwise the requesting site of the nearest ancestor on the admission chain that lies in a source
module. A user generic body therefore reports at its own operation, once for all its instances; a bundled-library instance reports at
the user's instantiating call. This follows SPEC 23.1.1 ("at the concrete use, including an instantiation") and SPEC 22.5.1's rule for
Abort locations; codes and locations are otherwise implementation-defined (SPEC 23.1.1). K13a–c move the test expectations to these
subjects through E12, with the legacy values kept until G2. Location changes are diagnostics, not native deviations (R5-M6 covers
native outcomes only).

### 2.9 Generated at emit (K11)

- `CheckedSet.Mark(roots)` at emit, after acceptance and after `Tests.Discover` (`Project.cs:510-514`): a breadth-first walk over
  runtime targets only, a pooled bit array; codegen writes exactly the marked items.
- Output roots: an Application, the startup body (F2a's symbol column: `main`'s item or the top-level statements' item); a Test
  build, the selected `#Test` items (P6's case order); a Library output, every root item of 2.3 (legacy generates every nongeneric
  body for every output, `LlvmEmitter.cs:221-237`; a `public` declaration is not an export, IMPL 21.3.1, so nothing narrower is
  observable). Static initializers enter through `staticEnsure` targets.
- An Application therefore no longer writes nongeneric functions nothing reaches (information, PD9).
- Invariant: a codegen request for an unmarked item is `InternalInvariant_Kd` (K11's negative test). K12 moves the 23 library-reach
  `Ownership.Bodies` reads to Generated (ratchet 183 → 160).

---

## 3. Partitions and publication per stage (R5-M4, PD7, PD12)

| Records | R5, sel | R5, `KIMI_PIPELINE=mir` only / legacy | G2 → H3, every pipeline | H3 → H5 | H5 on |
| --- | --- | --- | --- | --- | --- |
| MIR construction records and validator findings (R3-1) | check, ControlFlow, decide | mir: as today; legacy: none | check, ControlFlow, decide | folded into `MirReport` (R4-M3a unit a) | MIR decides |
| Collector records (2.8) | check, ControlFlow, decide (from K4b) | not run (PD4) | check, ControlFlow, decide | folded into `MirReport`; H0 names the single owner of a G128 `Apply` null (PD30) | as left |
| A1–A5 problems | unpublished (R4-M1) | unpublished | unpublished | published by H3 (A4 effect problems from H5, PD29) | decide |
| PD12 refusal (A1/A2 problem, arena skip) | generation, Emission partition, the problem's own code at its subject (from C4a) | — | generation, Emission (wherever MIR codegen runs: default from G3) | generation | removed (rejects at check) |
| Coverage-row `Unsupported_Kd` (PD11) | generation, Emission, sel only | — | — (X2 moved the rows into the capability table) | — | — |
| Codegen contract failures | `InternalInvariant_Kd`, generation, Emission | — | same | same | same |

- Publication is the existing `MirRecords` path (`C/Core/Compilation.Pipeline.cs:163-180`), reported under
  `DiagnosticRequirement.ControlFlow` by `ReportAnalysis` (`:98-109`) and counted by `Analysis` and `Generates` (`:34-42`,
  `:154-159`); acceptance already reads that count (`src/Kimi/SolutionAndProject/Project.cs:510`). No generation-time path
  for instance findings remains.
- `DiagnosticPartition.Emission` keeps PD12 refusals, coverage rows and codegen faults until S4's successor decides its name (G9).
- DIAGNOSTICS changes: :56 (instance refusal published at generation) is replaced at K4b for sel and at G2 for every pipeline;
  :292 ("Resource and output limits do not change acceptance") is corrected at K4b: a Resource Error rejects at check as
  `OwnershipStorageLimit_Kd` already does; only rendering and output limits do not; :306 is restated for the collector.
- LSP sees collector records from G2: the check service runs `Analyze` and `ReportAnalysis` and finalizes Input..Ownership,
  ControlFlow included (`src/Kimi/Checking/CheckService.cs:61`).

---

## 4. The R6-gen switch contract (G0 applies it; G:R6-gen-switch, G:R6-gen-del)

| # | Clause |
| --- | --- |
| S1 | **Semantics from G2.** Every `Analyze` on every pipeline runs `BuildMir`: publish, build, validate, `MirAnalysis.Run` (A3, A1, A2, A4, A5 once landed; unpublished) and the collector. `KIMI_PIPELINE=legacy` then means "old acceptance with MIR and collector records"; `mir` adds only the difference ledger and the `[mir]` identity until H3. The PD4 branch in `BuildMir` (collector only under sel) is deleted in G2 |
| S2 | **Publication at check.** G2 publishes construction records, validator findings and collector records on the default pipeline (R5-M4), with DEC entries superseding R3-1 (records only under mir), R4-M1 (for these records) and OD10's split; `check` reports instance-stage `Unsupported_Kd` and Resource records for the checked set, so a Library with an unbuildable generic instance fails `check`; the 20 instance-failure methods assert check-time records; `stage-baselines.json` and `MilestoneSourcesTest` re-baselined with reasons; STATUS gets PD30's G128 (e) row |
| S3 | **PD12's gate.** MIR codegen runs only on accepted programs (`Compilation.Generates`) and refuses a runtime-reached body with an A1/A2 problem or an arena skip with a typed Emission-partition record at the problem's subject until H5; `InternalInvariant_Kd` only for a runtime `Group == -1` in a body A1 and A2 completed. C4a's DEC entry covers sel; G0's extends it to the default pipeline from G3 |
| S4 | **Measurement.** G2 records default `check` and warm LSP Analyze time, allocations and retained memory with the full shadow driver and collector (Generic shape 1/32/64/128, K4b's harness); the mir-pipeline Allocation tests run on the default pipeline and keep 0 B warm. No threshold beyond the existing allocation assertions: the owner accepted the cost (R5-M4) |
| S5 | **Order and merge.** G1 splits `Compilation.Pipeline.cs` (behavior-preserving) into the switch and entry points, the records (`MirRecords`, `Problems`, `ReportAnalysis`) and the generation entry (`WriteIr`, `Validate`, `Generates`, the gate). G2, G2b and G3 are verified and pushed separately and merge into dev together after G:R6-gen-switch passes |
| S6 | **Rollback.** A regression in G2's or G3's Session or sweep is reverted on its branch, never patched on dev (plan §5.2). After the merge, `KIMI_CODEGEN=legacy` stays selectable until G4; G4 starts only after G3's Session and full sweep (`'*.ll'` O0/O2, all milestones, snapshot, O2 corpus A/B) passed on dev with no open codegen regression. After G4 the rollback is a revert of the switch group |
| S7 | **Deletion order = call graph.** A unit deletes a component only after every caller is gone, and the solution (`src/Benchmark` included) builds after each unit: G4 (selector, legacy branch, pending ledger, deviation form) → G5, G6 (tests reading `TryPrepare`, injected plans, pairing) → G7 (lowering, plans, the five collectors, `AnalyzeInstance` with its callers `LlvmEmitter.cs:429` and `LlvmEmitter.Defaults.cs:15`, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `Binding.ClosureCall`, the four Benchmark `.Emission.` readers and the split-IR check) → G8 (`EmissionModule` and writers) → G9 (Binding members only Emission read, `SameModuloOpenOrigins` among them, after a grep over `C/Analysis` and `C/Mir`; `GenerationFailed_Kd` replaced) → G10 (zero-reference rows) |
| S8 | **Prerequisites.** X3 (R5 exit gate), R5-M4, R5-M5 with Q4 landed (A5 step 1 through Q2), A-6 (G103-E9a/E9b), A-8 (jump part), the R4 MIR lane merged; otherwise OQ6's fallback (d) |

---

## 5. Positions on the plan's decisions

### 5.1 PDs

| PD | Position | Detail |
| --- | --- | --- |
| PD4 | Agree, refine | The collector-run condition (sel) lives in `Compilation.BuildMir`, the pipeline switch's file, `[Transitional("R6-gen")]`; G2 deletes the branch, G4 the selector |
| PD5 | Agree, refine | K-D1: `static` keeps arities and well-formedness; null for no static argument; codegen Types are `Erase(Apply(t, representative))`; the erased Subst is a key only |
| PD6 | Agree, refine | After `MirAnalysis.Run`, independent of its results; empty set on validator findings; roots as an input range (2.3); A3b inside the collector's body scan; K4b's ARCH correction lists A3b in the Backend's pre-acceptance pass (D-A) |
| PD7 | Agree, refine | DIAGNOSTICS :292 corrected at K4b (section 3) |
| PD12 | Agree (S2's part) | Stage table in section 3; gate wherever MIR codegen runs; refusal path removed at H5 |
| PD17 | Agree | `ObjectDescriptor` and `VTable` keyed by the erased Type; the token is the item's ordinal among Generated descriptors |
| PD18 | Agree | A declaring struct's glue has one target, its destructor (2.4) |
| PD21 | Agree | `MonoKind` and the capability rows are Backend representations, not ARCH vocabularies; both are new representations approved with the package (section 9, Q3) |
| PD26 | Agree | Collection is timed inside stage 3 (`MirPublishBuildValidate`, `src/Benchmark/CompilerPipeline.md:14-19`) |
| PD27 | Agree | The escape valve's D2 rows become capability rows at X2 |
| PD28 | Agree, refine | S6's G4 condition added |
| PD30 | Agree, refine | The collector's `Apply` null is a capability gap (2.6) at the location rule's subject, which for a source-module body is the call A4/A5 also name |

### 5.2 Departures from owner-approved texts

| ID | Departure | From | Reason | Cost |
| --- | --- | --- | --- | --- |
| D-A | A3b runs in the Backend's collector scan, before acceptance, not as a P7 Mir analysis | ARCH "Target pipeline" P7, PLAN10 §5.1/§5.5 | The table is the Backend's (ARCH: Mir analyses may not read the Backend); the driver is R4's (PD6); one body walk serves both stages | ARCH pipeline row text at K4b |
| D-B | The bundled library is not a root in user compilations; a test roots it | A literal reading of SPEC 8.15.3 per compilation | Its checked set is edit-independent; the test proves zero records, so results are identical | One `KimiLibraryShapeTest` row |
| D-C | Instance-stage `Unsupported_Kd` causes beyond i128 (G128, OCC-X, generic statics) are capability rows | PLAN10 §5.7 lists only operator, intrinsic and layout capabilities | ARCH:45-47 allows Unsupported only from Binding, MIR construction or the capability check; the collector's Unsupported must be one of them | Three rows |
| D-D | Erasure keeps Origin arity with `static` | PLAN10 §5.7 "Origin を消去した置換" (no form given) | A well-formed Subst, no new Origin kind | None |
| D-E | PD3 (no `MirBodyKind.Shim`), as the plan states | PLAN10 §5.4, A5P §9.1 | `MonoKind` holds the shims; no MIR consumer needs them as bodies | None here (S4 carries the codegen side) |

---

## 6. Unit mapping and review groups

### 6.1 Units

| Unit | Part of this design | Change to the plan |
| --- | --- | --- |
| K0 | Probe: G128 in library/milestones; instance counts, max depth, max same-definition chain and max per-definition set over library, milestones and Generic shape 1–128; Destruction-walk depth hits; old collection and check time; one inferred C-layout instance | Adds the chain, set and walk-depth counts (they set the headroom of 2.5) |
| F3 | `TyCtxt.Instantiates` (2.1) | File `TyCtxt.Erasure.cs`; hold "no" |
| K1 | Skeleton: key, `CheckedSet`, templates, roots, rows 1 and 10–11 for closed callees, runtime bits, no records | — |
| K2 | Closure contexts as Subst (2.1), D+R under S2 | — |
| K3 | `TyCtxt.Erase` (2.1) | Adds the `Apply(t, Erase(s))` property test |
| K4a | Generic edges (composition, representative), `Admit` with its four limits, records in `CheckedSet` (not yet published), Origin-only variants → one item, finite recursion reuse, G128 reproducer, 2×/4× work counters, warm 0 B | **New** (split of K4): local to the collector |
| K4b | `MirRecords` adds collector records under sel (ControlFlow); DEC OD4 entry and ARCH pipeline correction (P10/P11 split, D-A); DIAGNOSTICS :56, :292, :306; `CompilerPipeline.md` stage 3; check cost measured (stage 3, Generic shape 1/32/64/128, warm LSP Analyze); zero records over the library (2.3) and the 40 harnessed milestones under sel | **New** (split of K4): the first commit whose collection decides acceptance |
| K5 | Rows 5, 10–12; `ClosureErasure`, `ItemAdapter` items | — |
| K6 | Row 3; `Builtin` items | — |
| K7 | Rows 6, 8, 9; `DropGlue` items; generic-static gap | — |
| K8 | Row 7; `ObjectDescriptor`, `VTable` items | — |
| K9a | `Capability`, A3b | Group G:K-capability |
| K9b | Instance-stage capability; the location rule (2.8) for capability | Group G:K-capability |
| K10a | Layout limits of items | — |
| K10b | C-layout view and records; the sel overlap noted (2.7), D+R under S2 | — |
| K11 | Generated (2.9) | — |
| K12 | Library-reach reads → Generated | — |
| K13a | Capability expectations through E12 | Group G:K-capability |
| K13b, K13c | Limit expectations through E12 | — |
| F10 | `MirProgram.StaticInit` (W2) | — |
| C0a (K part) | The one generation entry calls `Generates` (counting collector records) and, from K11, `Mark`; `PipelineSwitchTest` adds sel's collector cases | — |
| G0 | Writes section 4 into DEC, D+R under S2 | — |
| G1 | Split per S5 | — |
| G2 | S1, S2, S4 | — |
| G2b | Binding's eager C-layout instance report and its replay queue deleted (`Binding.Attributes.cs:375-389`, `Binding.Types.cs:801-804`, `Binding.cs:455-456`); the collector owns the obligation; a written borrow-only use becomes valid ((a), SPEC 8.15.3) | **New**, in G:R6-gen-switch, after G2 |
| G3, G4 | S3, S6 | — |
| G7 | S7's instance machinery | — |

### 6.2 Review groups (DEC Q6 form 2 under the approved package)

| Group | Units | Merge order |
| --- | --- | --- |
| G:K-core | K1, K3, K4a, K4b | First; C0a and later C units stack on K1's branch until it passes |
| G:K-edges | K5, K7, K10a, K13b | After G:K-core |
| G:K-capability (new) | K9a, K9b, K13a | After G:K-core; independent of G:K-edges |
| G:C-frame | E10, E15, C0a, C0b1, C0b2, K11 | After G:K-core |
| G:C-scalar | C1a, C1b | — |
| G:C-aggregate | C2a, C2b | — |
| G:call | F5b, K6, C3a, C3b | — |
| G:C-drop | C4s, C4a, C4b, C4c | After G:K-edges (K7) |
| G:closure | C5a, C5b | After G:K-edges (K5) |
| G:object | F4, K8, K13c, C6a, C6b, C6c | — |
| G:library-1 | C8a, C8b, C8c | — |
| G:library-2 | F6, C8d, C8e, C8f | — |
| G:format | C9a, C9b, C9c | — |
| G:test | C10a, C10b | — |
| G:R6-gen-switch | G2, G2b, G3 | One merge (S5) |
| G:R6-gen-del | G4, G5, G6, G7, G8, G9, G10 | In S7's order |

Outside groups: K2, K10b, G0 D+R (S2); F3 D+R (S3); K12, F10, G1 R; K0 none; G11 final review.

Reasons for the changes: G:K-edges held seven units mixing three concepts (edges, the capability table, layout); the capability table
is its own canonical component with its own DEC entry (R4-M8), so it is reviewed alone and holds fewer units on a branch. K4's split
separates the collector-local mechanism from the cross-phase acceptance change, so each commit has one reason and K4b's acceptance
change is measured alone.

---

## 7. Alternatives considered

| Alternative | Rejected because |
| --- | --- |
| Fix inside the owning components: keep the legacy collectors and `AnalyzeInstance`, key them by Subst, report their failures earlier | They run after acceptance (SPEC 23.2.2: an accepted program must not fail with a Language error from an instance), read CallPlans and syntax (ARCH Backend row), and need the old ownership IR; moving them earlier means running Emission planning at check |
| The collector as a MIR analysis step in the driver | The driver is R4's shared file and its contract forbids deciding acceptance (`MirAnalysis.cs:11`); monomorphization is the Backend's (ARCH P11) |
| Erase early (proofs on erased Types) | Proofs read Origins (PLAN10 OJ3); an erased `static` would invent premises |
| No erasure; equivalence by `SameModuloOpenOrigins` | Not hashable; legacy's quadratic scans; 8 Emission callers kept alive |
| A separate `InstanceKey` type instead of an erased Subst | A second representation of a substitution; the erased Subst is one already interned value |
| Per-kind tables (instances, glue, vtables, descriptors, shims) | Five tables with five lookups and five Generated walks for one concept "item the output may need" |
| Collection at generation with Emission-partition records (PLAN10 §7) | Contradicts OD4 and SPEC 23.2.2; owner chose PD7 (R5-M4) |
| Root the bundled library in every compilation | Same records, recomputed on every Analyze (LSP included) |
| Generated = every nongeneric body (legacy) | Writes unreachable code; PLAN10 §5.6.6 and IMPL 21.3.1 ask for the output's closure |
| Limits spread over the worklist, the table and the edge code | Three places deciding Resource limits; PLAN10 §5.7 asks for one |
| A Deep part through per-level component glue edges instead of the destruction walk | A second walk of destruction (ARCH "Type facts in MIR": one walk); the walk's bound becomes a Resource record (R6) |
| Report every instance-stage record at the requesting call | A user generic body would report once per instantiating call instead of at its own operation; SPEC 22.5.1 already uses the source-module rule |
| Incremental collection per edit | No measured need; full recollection is pooled and 0 B warm; K4b and G2 measure |

---

## 8. Deletions, size and speed

| Stage | Deleted | Lines (approx.) |
| --- | --- | --- |
| K2 | `closureContexts` list, its scan and the CallPlan synthesis | −40 (+40 for the Subst form and the bridge) |
| G2b | Binding's eager C-layout instance report and replay queue | −30 |
| G7 | The five collectors (2,054), the instance parts of `LlvmEmitter` and `LlvmEmitter.Defaults` (≈ 150), `OwnershipAnalysis.Instances` (315) with `FailedInstance`, `InstanceStorageLimit`, `ClearInstances` and `OwnershipModel`'s instance half, `Binding.ClosureCall` | ≈ −2,600 of G7's −13,900 |
| G9 | `SameModuloOpenOrigins`, `IsInapplicableVirtualBody`, `ImplementationContext`, `IsVerifiedSpecialization`, `PrepareTypeStorage` | part of −400 |
| H7 | Binding's i128 gates (division and conversion) | ≈ −20 (H) |
| ratchets | `(Failed\|Failure\|Analyze)Instance` 11 → 0, `.Emission.(FailureIsResourceLimit\|ReportFailure)` 21 → 0, `Ownership.Bodies` 183 → 160 (K12) (`T/ArchitectureRulesTest.cs:28, 32, 33`) | — |

New code (H): F3 +15, K1 +200, K3 +100, K4a +150, K4b +80, K5 +90, K6 +50, K7 +120, K8 +120, K9a +100, K9b +50, K10a +40,
K10b +60, K11 +70 ≈ +1,250 (plan: ≈ +1,100), tests ≈ +1,000.

Speed (H): check gains one body scan per version and per item one template replay with memoized `Compose`/`Erase`; K0 and K4b
measure it, G2 records it on every Analyze. Generation loses `AnalyzeInstance`'s per-instance ownership dataflow and the legacy
linear scans; the collector's output is reused by codegen, which resolves no callee.

---

## 9. Risks, open questions, owner items

| ID | Risk or question | Response |
| --- | --- | --- |
| R1 | Collection on every Analyze from G2 (LSP included) costs time | Measured at K0, K4b, G2; warm 0 B asserted; owner accepted (R5-M4) |
| R2 | G128: a library or milestone program with a deep finite Subst | K0 checks; DEC O3 applies only then (PD30) |
| R3 | A Binding proof on closed Types that reads Origins (OJ3, Owned) could tell Origin variants of one key apart | SPEC 3.3.1/8.13.3 make it irrelevant to code; K4a's Origin-only test; a finding is a SPEC gap to report, not a collector rule |
| R4 | Set limit 1,024 per definition is reached by a valid generic-heavy program | K0's counts; the limit is legacy's |
| R5 | Templates replayed per item could be slow for large libraries of tiny generic bodies | K4b's 2×/4× counters and Generic shape 1–128 |
| R6 | Three inline-depth bounds disagree: destruction walk 32 (`MirTypes.Destruction.cs`), storage instantiation 64 (`Binding.Structs.cs:54-57`), layout 64 (`AggregateLayout.cs:47`) | Open issue for PLAN: one bound per concern or one shared constant, decided with R4 (the walk is R4's) and P7c |
| R7 | Under sel, a written by-value C-layout instance may report twice until G2b | Declared, sel-only, removal at G2b |
| Q1 | Is the location rule (2.8) right, or should all instance-stage records go to the requesting call? | Design review |
| Q2 | Is D-B (library verified by test, not rooted) an acceptable reading of SPEC 8.15.3? | Design review; owner if the review doubts it |
| Q3 | Are `MonoKind` and the capability rows (D-C) acceptable as new Backend representations? | Owner, with the package (OQ4) |
| Q4 | Should H7 (Binding's i128 gate) move to G2b, since A3b runs on every pipeline from G2? | Not proposed: the old analysis still decides until H5 and may report i128 itself (`C/Analysis/OwnershipAnalysis.cs:1454-1459`) (H); revisit at H0 |
| Q5 | Library output Generated = every root item (legacy behavior) or public roots only? | Every root item, since `public` exports nothing (IMPL 21.3.1); review may narrow it |

Owner items: approve the package (OQ4) with D-A to D-E listed; nothing else in this chapter needs a separate owner decision.
