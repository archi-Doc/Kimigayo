# R5 step 0, chapter F (S3): the R5 fact schema — package

Status: synthesis of design 1 ([`deep-dives/q6-dispatch-facts.md`](deep-dives/q6-dispatch-facts.md), "D1") and design 2
([`F-design-2.md`](F-design-2.md), "D2") for the S2–S4 design review and the owner's one look (OQ4, DECISIONS R5-M1). A working
record, not authority. Date 2026-10-18.

Base: code at `origin/dev` `9dbf1393` (identical to `a8c2ec93` for every file cited); records at the same commit. `C/` =
`src/Kimi/Compiler/`, `B/` = the new Backend, plan = the R5 and R6 Implementation Plan, PD/OQ/unit names are its. (H) marks a
claim not checked by running code. "Probe" = a throwaway xUnit probe run on a detached `origin/dev` worktree (not committed;
results quoted in §2).

Already on dev and not re-planned here: P6 (`TestCase.Symbol`), Q15 (`CallPlan.Evaluated`), Q6/Q6b (StructuralCompletion and
MatchCoverage in Binding). Hold rule: DECISIONS R5-M7.

---

## 0. Decisions in one screen

1. **One schema rule** (D2 §2.1, kept): one producer per fact; tables filled in the final Binding pass, views answered after it;
   definitions named by `BindingSymbol`, syntax by `SourceRef`, never a Koto; view memos live until an edit (R4-M51's pattern),
   so warm rebuilds allocate 0 B; a view writes no diagnostic, CallPlan, HIR or synthesized Koto.
2. **Dispatch facts are views over one witness core** (D1 §3.2 and D2 §3.3 agree): F5a extracts `ResolveRequirementWitness`;
   the bind-time materializer, A4's `Witnesses` and the new `TyCtxt.Witness` call it.
3. **`TyCtxt.Witness(requirement, contract, subst)`** returns `RequirementTarget` with kinds Body, Builtin, Storage, Refused,
   Limit (D2's inputs and Refused/Limit; D1's "projection is a no-op", so no `BaseDepth`).
4. **Standard property witnesses stay Backend shims** (PLAN10 §5.7, chapter C design 2 §2.9): the view answers kind
   Storage. D2's F5p (bridges published with MIR bodies) is not adopted, so there is no departure from PLAN10.
5. **PD15 refined** (D2, verified by probe): F7 reads `CallPlan.TupleOperator` and `TyCtxt.Comparison`, not
   `ComparisonPlan(CallPlan)`, which is null for every tuple with an abstract leaf. F6 moves before F7, and the two form one
   review group, G:compare. `ComparisonPlan(CallPlan)` is deleted at H11, not kept until R7.
6. **`TyCtxt.Comparison(contract, self, operators)`**: one `bool` (ARCH structure limit), keyed by the Contract rather than a
   requirement symbol, because MIR names the synthesized built-in symbol (probe).
7. **Symbol columns**: `StartupResult.Symbol` only. The root module is always ordinal 0, so D2's `Module` column is dropped.
   `LibraryImport.Decl` replaces `Function` (D2). `[Transitional("R8")]` Dictionary-storage symbols, with no catalog row (D2).
8. **`VTableDef`** hangs off `AdtDef`, needs no sort and follows member order. Fragments merge in sorted-path parse order,
   which equals SPEC 6.2.1 except for one corner case that Field order already has (verified). `TyCtxt.Applies` is memoized.
9. **`TyCtxt.Instantiates`** replaces `FunctionKoto.RequiresInstantiation` (a Syntax property that reads Binding). Its rule is
   "it or any enclosing declaration has a Type, Semantics or length slot". It sits in one instance-facts partial shared with
   K3 (file name: open point 3).

---

## 1. Required meaning

| Rule | Governs | Facts |
| --- | --- | --- |
| SPEC 6.4.1–6.4.3; IMPL 21.1.3 | Static slot selection, most-derived implementation, an original's conditional premise per derived Type; tables implementation-defined, module-local, deterministic | `VTableDef`, `Applies` |
| SPEC 6.2.1 | Logical declaration order across fragments (UTF-8 byte order of logical names, then position) | slot order = member order |
| SPEC 8.6.1, 8.11.4, 8.13.3, 11.9.2 | A closed requirement call runs the witness its Contract reference selects; a closed key selects its specialization; a standard property witness is a bridge under its path's premises | witness core, `Witness` |
| SPEC 13.4, 13.4.1, 12.4.3 | Tuple operators compare elementwise/lexicographically, unordered float pair → all four ordering operators false; a `T` operand uses `T`'s Contract mapping in every instance | `Comparison`, F7 |
| SPEC 8.15.1; IMPL 21.3.1 | Generic bodies checked once, generated per closed instantiation; Origins not part of identity (SPEC 8.13.3, 3.3.1) | `Instantiates` |
| IMPL 21.1.2; SPEC 6.7.1 | C layout: written order; open, derived, empty rejected | `AdtDef.IsCLayout` |
| SPEC 22.4.1–22.4.2, 22.6 | Startup selection, `#LibraryImport`, C ABI | symbol columns |
| SPEC 22.5.1 | One location text per Abort site; `$abort` reports the `$abort` expression | P5, F9 |
| SPEC 17.6.3 | A failed verification shows values from the original evaluation | F8 |

---

## 2. Facts settled in the code

| # | Claim (source) | Verdict | Evidence |
| --- | --- | --- | --- |
| 1 | `ComparisonPlan(CallPlan)` is null whenever a tuple has an abstract leaf (D2 PD15, H) | **True** | Probe: `(T, f64) < (T, f64)` and `==` under `T is Comparable/Equatable` give a NULL plan; closed `(i32, f64)`, `(Key, f64)` and nested `(i32, (f64, Key))` give full trees. Cause: a leaf resolves through `RequirementImplementation` (`Binding.ComparisonPlans.cs:48-50`), whose `ResolveConformance` returns Unknown for a `Parameter` kind (`Binding.ConditionalConformances.cs:326`), and is run in the library requirement's scope (`Binding.Formatting.cs:69`) |
| 2 | The Backend cannot read its CallPlan leaves (D2) | **True for the Backend, irrelevant to F7** | Backend reads MIR, declaration tables and `TyCtxt` only (ARCH:30, :36); `Mir/Build` may read Plans (ARCH:28). So it decides C8f, not F7; claim 1 decides F7 |
| 3 | The MIR callee of a tuple comparison names the requirement (D2 F6 input) | **No**: it names the synthesized built-in | Probe: closed and abstract tuple comparisons both have `Kind = Requirement`, target `compare`/`BuiltinCompare` (`ExecutedTarget` → `CompilerRequirementTarget`, `Binding.Formatting.cs:117-140`; `Binding.Calls.cs:883-889`). F6 therefore takes the Contract |
| 4 | Standard property-witness bridges have no MIR body (D2 F5p, H) | **True** | Probe (`Counted`/`Own` sources): no body has an `IsPropertyWitness` declaration, also after the old Analysis created bridges and after a rebind. Cause: bridges are synthesized `FunctionKoto`s outside the tree (`Binding.PropertyWitnesses.cs:12-77`), the HIR walk starts at module roots (`Binding.Hir.cs:44-48`), and the builder takes only published table nodes (`MirBuilder.cs:108`) |
| 5 | `Subst.Declaring` holds the bound Contract reference of every requirement call (D2 risk 6, H) | **Holds on the corpus; code allows otherwise** | Probe over `HirColumnsTest.Corpus` + library: 1,587 requirement callees with an unbound reference; 2 with a bound reference, and both have `Subst.Declaring` reference-equal to it. Code: `Declaring = call.DeclaringType ?? BoundContract(call)` (`TyCtxt.cs:201-204, 277-278`), so a lookup-supplied declaring Type would take the slot. F5b's twin test asserts it per call |
| 6 | Merged struct fragments follow SPEC 6.2.1 (D2 H; D1 finding) | **Yes, but with an existing corner case** | Fragments merge at parse by `AddLast` (`DeclarationContainerKoto.cs:177-186`) in source order. Sources are sorted by full path, `StringComparer.Ordinal`, on disk and in LSP (`CheckInputSource.cs:46-50`, `Lsp/DiskReader.cs:97-98`; root first). UTF-16 ordinal and UTF-8 byte order differ only for names that mix supplementary characters with BMP characters ≥ U+E000. `AdtDef.Fields` uses the same members (`Binding.Storage.cs:226`), so slot order and Field order cannot diverge |
| 7 | `RequiresInstantiation` is wrong for nested declarations (D2 H) | **Not for containers; open for nested named functions** | Container schemas append the parent's slots (`Binding.Origins.cs:324-341`), so a function in a group nested in a generic struct is covered. Function schemas inherit nothing (`:283-286`). The property looks one owner up (`FunctionKoto.cs:87-89`), so a named function two levels inside a generic function may be misclassified (H). SPEC 6.1 ("an inner declaration is generic whenever an enclosing parameter exists") and SPEC 9 ("a nested function may use outer Type parameters") give the rule |
| 8 | Startup module may be constant 0 (D2 H) | **True** | `SourceModules[0]` is always `Compilation.Kotonoha` (`Compilation.cs:163`, `Compilation.Modules.cs:82`); `CheckStartup` treats indices ≥ 1 as dependencies (`Binding.Startup.cs:57-77`) |
| 9 | `VTableDef` can live on `AdtDef` without a `BindingModel.cs` edit (D2) | **True** | `BindingSymbol.Adt` exists (`BindingModel.cs:331`); `AdtDef.cs` is not on R5-M7's list |
| 10 | Base subobject is a layout prefix, so receiver projection is a no-op (D1) | **True** | "The complete base occupies a prefix" (`AggregateLayout.cs:382-383`); chapter C keeps it (C design 2 §2.5) |
| 11 | Views write no state a later pass or diagnostic sees (D2 H) | **True only after two fixes** | `VirtualApplicability` passes no failure node (`Binding.VirtualOverrides.cs:141-153`; `CheckConstraints` records only with `failureUse`, `Binding.Constraints.cs:818`). The core still writes in two places: (a) `CompilerRequirementTarget` rewrites the shared built-in symbols' `Type`/`ReceiverIndex` on every call (`Binding.Formatting.cs:137-138, 152-153`); F5a moves this to one point per pass. (b) `PropertyWitnessFunction` creates and refreshes bridges that later passes reuse (`Binding.PropertyWitnesses.cs:22-77`); the view never calls it (decision 4). What remains is interning, proof caches and `StoredType`'s storage preparation, the class `TyCtxt.Apply` already has (audit header item 4) |
| 12 | A4 needs bridge bodies (D2 F5p reason) | **No** | `EffectObligations` iterates function witnesses only (`Binding.EffectObligations.cs:26-37`); A1–A5 analyze definitions, never instance calls; a generic body's property requirement call stays a Requirement callee (probe: `count.Get`/`count.Set` with abstract Self) |

---

## 3. Decision table

| Topic | D1 | D2 | Chosen | Reason |
| --- | --- | --- | --- | --- |
| Process | One schema through FP, then Q6 form 1 per fact | Same (plan) | Same | Owner adopted (OQ4, R5-M1) |
| Common contract | Implicit | §2.1 table | D2's, minus bridge creation | One rule for every fact; R4-M51 memo pattern |
| `VTableDef` shape | Per struct symbol: `Slot`, `Count`, `Implementation`; no sort | On `AdtDef`; `Of(type)`, `Originals`, `Implementations`, `BaseCount`, `Slot`; no sort | D2's storage, keyed by struct **symbol** (D1) | A declaration table belongs on the declaration; fact 9; fact 6 removes the sort |
| `Applies` | `TyCtxt.Applies(original, declaring) → bool?` | Same + memo, representative unerased Type | D2 | PD5; warm 0 B |
| Witness core | One core with `Witnesses` | Same; refusal rule inside the core; built-in symbols fixed per pass | D2 | Fact 11(a); one refusal rule for materializer and view |
| `Witness` signature | `(requirement, Subst) → (Target, Subst)?` | `(requirement, contract, subst) → RequirementTarget(Kind, Target, Subst, BaseDepth)` | `(requirement, contract, subst)`; kinds Body, Builtin, Storage, Refused, Limit; **no `BaseDepth`** | The Contract reference decides bound references and fixed-array/integer cases (`Binding.RequirementInstances.cs:82-92, 170-197`); Refused (OCC-X) and Limit (PD30) must differ from a broken contract; fact 10 |
| Property witnesses | Inside the core (body question not raised) | F5p: bridges created at Bind with MIR bodies (PLAN10 §5.7 departure) | **Backend Storage shim** | Facts 4, 12: no analysis gains; MIR bodies need synthesized Koto in the R7-transitional HIR walk or a non-syntax builder entry (PD3's reason against Shim bodies); PLAN10 §5.7 and chapter C already name the shim; no owner departure |
| `Comparison` | `(self, equality)` over the private plan; operator mode in MIR | `(requirement, self, operators)`, own composition over the core, Requirement leaves | D2's composition, input **`contract`** | Fact 1 (abstract leaves), fact 3 (callee symbol), ARCH:124 (one `bool`); D1 lacks operator mode, which the tree shape depends on (`Binding.ComparisonPlans.cs:48`) |
| PD15 (F7 inputs) | Option C: builder expands by the published operator-mode plan | `TupleOperator` + F6 | D2 | Fact 1: no plan exists for dependent tuples, which F7 must expand too (SPEC 13.4 "every instance") |
| F6/F7 order and review | F6 after Witness; G:library-2 | F6 before F7; F6 D+R (S3) | F6 before F7; **G:compare (F6, F7)** + R4 reviewer on F7 | R4-M2a: F6 lands with its first consumer, which is now F7 |
| `ComparisonPlan(CallPlan)` stage | R6-check (option C) | H5 | **H11** (after its last readers go at G7, H5) | H11 is the plan's Binding-deletion unit |
| `IsCLayout` | `AdtDef.IsCLayout` in `ValidateLayoutFragments` | Same; written for every struct | D2 | Agreed |
| Startup column | Symbol + module ordinal | `Symbol` + `Module` | **`Symbol` only** | Fact 8: the column would be constant (AGENTS: no speculative mechanism) |
| Import column | Add a symbol column | Replace `Function` by `Decl` | D2 | One column; legacy readers dereference `Decl.Declaration` |
| Dictionary storage | Lang-item view on `KimiLibrary` | `[Transitional("R8")]` handle view, no catalog row or `KimiDeclarationId` | D2 | `KimiLibraryCatalog.cs` held; PLAN10 §5.8 shrinks that list; R8 deletes the rows |
| Location text (P5) | Core `SourceLocationTable` by `SourceRef` | Same + memo lifecycle, one table | D2 | Agreed |
| `Instantiates` | (chapter K) | Any enclosing declaration; deletes the Parsing property; `TyCtxt.Instances.cs` | D2's rule and deletion; file shared with K3 (open point 3) | Fact 7; ARCH:36 "Syntax must not read Binding" |
| Runtime identity | Erased Type, no fact | (chapter K/C) | No fact (PD17) | Agreed; outside F's units |
| Builder facts F8–F10 | Builder rules | MIR facts in W1/W2 | As plan | Agreed |
| Holds | — | Narrowed list | D2's list, F5p removed | R5-M7 |
| Asks | — | A-11 (optional), A-12 | A-11 kept, restated (§9); A-12 dropped | F5p dropped; F7's R4 reviewer is already in the plan's F7 row |

---

## 4. Components and contracts

### 4.1 Common contract of every chapter-F fact (D2 §2.1, amended)

| Clause | Rule |
| --- | --- |
| Producer | Exactly one component decides the fact; a consumer never re-derives it (ARCH "Missing facts (R3–R5)") |
| Timing | Tables are filled in the final Binding pass and views answered after it; nothing reads a fact during a pass |
| Identity | Definitions by `BindingSymbol` (`MirDefKey.Decl`, DECISIONS R3-6), syntax by `SourceRef`, Types by interned `BoundType`, substitutions by interned `Subst` |
| Mutation | After the final pass, published state is immutable. A view may intern, prepare instance storage (as `Apply` does) and fill its own memo. It writes no pass state, diagnostic, CallPlan, HIR or synthesized Koto |
| Lifecycle | Tables are refilled per pass with reused arrays. View memos are discarded by `Binding.Invalidate` through one `TyCtxt.ResetDispatch()` beside `ResetRetentions()` (`Binding.cs:199`) |
| Cost | 0 B per warm rebuild for every fact; cold cost is one entry per distinct key, bounded by the checked set |
| Failure | `Apply`'s depth bound is a typed Limit (PD30). "No answer" for a closed, accepted input is a broken Binding contract, which the reader records as `InternalInvariant_Kd` |
| D5 | Old Analysis and Emission readers stay unchanged. A legacy twin is deleted with its last reader, at the stage DECISIONS records for it |

### 4.2 `VTableDef` (F4) — `C/Binding/VTableDef.cs`

- **Responsibility.** The dispatch shape of a struct family (SPEC 6.4): for each struct that has or inherits a virtual slot, its
  slots in order and the function each slot runs.
- **Inputs.** `virtualOverrides`/`overrideEntries` (`Binding.VirtualOverrides.cs:16-19`); the struct's merged `Members` in the
  order `PrepareStorage` uses for Fields (`Binding.Storage.cs:226`).
- **Outputs.** `VTableDef.Of(BindingSymbol structure)` → table or null (no slot): `Originals[slot]`, `Implementations[slot]`
  (own override, else the base's entry, else the original), `BaseCount`; `Slot(BindingSymbol original)` → index, searched in the
  original owner's own range.
- **Invariants.** Prefix: a struct's table starts with its base's. Own slots follow member order (fact 6). A table exists only for
  a struct whose declaration and overrides resolved (R4-M18). Filled at the end of `PrepareVirtualOverrides` (`Binding.cs:484`),
  stored on `AdtDef` beside the Fields and refilled per pass with `AdtDef`'s array reuse.
- **Prohibitions.** No path or span sort, applicability, ABI, receiver adaptation or CallPlan.
- **Consumer rule** (K8, C6c; D1 §3.1). For slot i of closed `T`: the implementation's declaring layer is the first Type on
  `AdtDef.Base`'s chain from `T` whose symbol owns it; its instance is `TyCtxt.Synthesized(impl, layer, [])`. Virtual functions have
  no own generic parameters (`Binding.VirtualDeclarations.cs:30-33`). A Virtual MIR callee names the original (R4-M41), so dispatch
  loads `Slot(original)`.

### 4.3 `TyCtxt.Applies(BindingSymbol original, BoundType declaring)` (F4) — `C/Binding/TyCtxt.Dispatch.cs`

- SPEC 6.4.3: whether `original`'s slot exists for the declaring instance (the layer on the chain that declares the original).
  Over `VirtualApplicability` (`Binding.VirtualOverrides.cs:141-153`): true for Proven, false for Refuted, null otherwise. Null (or
  Error) for a closed declaring Type is a broken contract.
- Input: the representative unerased instance (PD5), never an erased key. Memo `(original, declaring)` until an edit.
- A false answer gives a null entry; the body stays a checked-only definition (R4-M28) and is never Generated (PD6).

### 4.4 Witness core (F5a) — `C/Binding/Binding.RequirementWitness.cs`

- **Responsibility.** The one rule from (requirement, Self, Contract reference, premise scope) to what the requirement runs (SPEC
  8.6.1, 8.11.4, 11.9.2; IMPL 21.3.1).
- **Signature.** `ResolveRequirementWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract, BindingScope scope)`.
  `contract` is the instance's reference, from the extracted matching half of `InstanceReference` (`Binding.RequirementInstances.cs:170-197`).
- **Result kinds** (today's cases, `Binding.RequirementInstances.cs:53-123`, `Binding.PropertyWitnesses.cs:109-150`,
  `Binding.FixedArrays.cs:84-101`, `Binding.IntegerPositions.cs`): Builtin (`CompilerRequirementTarget`), Conformance (witness,
  declaring instance, base path), Property (an accessor-call witness's accessor, or a standard witness's Field and
  `PropertyWitnessKind`), FixedArrayEntry, IntegerPosition, Refused (`ProjectWitnessCall`'s non-`ref` receiver, `:157-160`,
  OCC-X), None. A Self with an abstract part is not resolved (`:56-66`).
- **Callers.** `InstantiateRequirementCall` keeps materialization only: Origin translation, receiver projection and CallPlan
  storage. The standard bridge function is created here, for legacy readers only. `Binding.Witnesses` calls the core in its
  non-comparison branch, and `TyCtxt.Witness` calls it too. Each caller keeps its own scope: `ConstraintScope(outer)`,
  `EffectPremises(definition)` or the library root.
- **Read-only after Bind.** The built-in target symbols get their `Type`/`ReceiverIndex` once per pass, after headers (fact 11a).
- **bp**: diagnostic and semantic snapshots and legacy `.ll` unchanged.

### 4.5 `TyCtxt.Witness(BindingSymbol requirement, BindingSymbol? contract, Subst subst)` (F5b) — `TyCtxt.Dispatch.cs`

- **Input.** A MIR Requirement callee after composition: `MirCallee.Symbol`, `MirCallee.Contract` (`MirBuilder.Calls.cs:121`), a
  closed `Subst` whose `Self` is the conforming Type and whose `Declaring` holds a bound reference (fact 5). For the
  compiler-function rows that call a witness (formatting callbacks, audit header item 6), the caller builds the Subst with
  a Self-bearing `Synthesized` factory in the same partial, so one view serves both.
- **Output.** `RequirementTarget(Kind, Target, Subst)`:

  | Kind | Target / Subst | Reader (K6) |
  | --- | --- | --- |
  | Body | A function with a MIR body: a conformance witness (inherited included), an accessor-call witness's executing accessor (Getter/Setter key), a fixed-array or integer-position member. Subst = its instance (declaring instance, own Types/lengths per case, translated Origins) | Fn edge through `TyCtxt.Implementation(Target, Subst, 0)` (SPEC 8.13.3) |
  | Builtin | A compiler function (comparison, format, arithmetic); Subst binds Self | built-in shim item; C8f reads F6 |
  | Storage | A standard property witness: Target = the implementing Field, Subst binds the conforming Self, plus Binding's `PropertyWitnessKind` (Copy, Borrow, Set; existing enum) | property-witness shim item (PLAN10 §5.7; C §2.9); Set adds `DropGlue(field Type)` |
  | Refused | OCC-X | located `Unsupported_Kd` at the requesting call |
  | Limit | `Apply` null (G128) | R4-M51's code at the requesting call (PD30) |

- **Contract.** A closed, accepted Self never gets "none". The receiver's base projection is physically a no-op (fact 10), so
  no depth is returned. Memo `(requirement, contract, subst)` until an edit. The exact record layout is F5b's D+R; folding
  `PropertyWitnessKind` into Kind is allowed.
- **Twin test.** Over every requirement call K4's collector instantiates in the library and milestone bodies, the view's target
  equals `InstantiateForwardedCall`'s. The test also asserts fact 5 per bound-reference call.

### 4.6 `TyCtxt.Comparison(BindingSymbol contract, BoundType self, bool operators)` (F6) — `TyCtxt.Dispatch.cs`

- `contract` is Equatable or Comparable, read from the call's target scope owner. `operators` selects source operator
  semantics (IEEE floats) over the Contract mapping (NaN-reflexive `equals`).
- **Output.** `ComparisonNode(Type, Kind, Target, Subst, Parts)`, or null for a Limit:

  | Kind | When (Binding's predicates) | Payload |
  | --- | --- | --- |
  | Composite | `ComparisonTypes.IsComposite` (Tuple, safe `ref` layer) | `Parts` |
  | Builtin | `ComparisonTypes.IsBuiltin(type, contract)`, or `operators` and numeric (`Binding.ComparisonPlans.cs:48`) | the Type |
  | Witness | closed leaf: the core's Conformance result (library-root scope; H that no closed conditional conformance needs premises) | `Target`, declaring instance as `Subst` |
  | Requirement | leaf with an abstract part: its Contract mapping in every instance (SPEC 13.4) | `Target` = the Contract's requirement, `Subst` binds Self |

- Memo `(contract, self, operators)` until an edit; nodes allocated once per key.
- **Readers.** F7 with `operators: true`; C8f with `operators: false` (the BuiltinEquals/BuiltinCompare shims and the Dictionary
  key-equality row, today's `DictionaryComparison`). A4 only if it accepts A-11.
- **Legacy twin** (recorded in DECISIONS with its stages): the private `ComparisonPlan`, `BoundComparison` and
  `RequirementImplementation` serve `ComparisonPlan(CallPlan)` (readers `Binding.EffectBounds.cs:1029` → H5,
  `GenericStoragePlan.Comparisons.cs:26` → G7; the overload itself → H11), `TryResolveFormattingCallback` (H11) and A4's
  `Witnesses` (`Binding.RequirementInstances.cs:17-19`). The last reader keeps the twin alive until A-11 lands, or until R7 if
  A-11 is declined (open point 2).

### 4.7 Tuple-operator expansion inputs (F7, PD15 refined)

- The builder expands a comparison whose `CallPlan.Of(node).TupleOperator` is set (`CallPlan.cs:121-123`; set for a built-in
  operator on a Tuple, `Binding.Comparisons.cs:43`, `Binding.SelectedRequirements.cs:39`), closed or dependent alike. It reads
  `TyCtxt.Comparison(contract, call.ConformingType, operators: true)`:
  - Composite → projections of the operands' shared views;
  - Builtin → `BinaryOp` (IEEE by construction), or the call D1/plan F7 names for strings;
  - Witness → Fn callee (R4-M22: a closed Self with a user witness is a Fn callee);
  - Requirement → a Requirement callee formed like `MirBuilder.Calls.cs:121`;
  - then the lexicographic chain with early exit and the unordered rule.
- F7 owns the shape. A1–A5 see it, so an R4 reviewer signs off (plan F7 row). No new callee mode, `CompilerFunctionKind` or
  `IntrinsicId` (PD21).
- Builder facts after F7: `CallPlan.TupleOperator` (ARCH "Calls", to R7). `ComparisonPlan(CallPlan)` is no longer a builder fact.

### 4.8 `AdtDef.IsCLayout` (F1)

`true` exactly where `ValidateLayoutFragments` adds the struct to `cLayouts` (`Binding.Attributes.cs:472-475`), else `false`;
written for every struct each pass. `HasCLayout` (`:33-44`) stays for header binding, which runs before the fact exists;
the commit records that two answers exist, in two phases. First consumer: P7b (it deletes the attribute walk and the `Open`
re-check, `AggregateLayout.cs:257-265, 300`).

### 4.9 Symbol columns (F2a, F2b, F2c; P6 landed)

- **Rule** (one DECISIONS entry, F2a). A Backend root or extern names its definition as `MirDefKey` does. A Koto column is
  replaced where every reader can derive the Koto from the symbol; otherwise the symbol is added and the Koto column goes with
  its last syntax reader.
- **F2a.** `StartupResult.Symbol: BindingSymbol?` (explicit `main`). `Kind == Implicit` means the startup body is
  `MirDefKey(null, Fn, 0)`, because the root module is ordinal 0 (fact 8). `Function` stays: it is public, and an implicit
  startup has no symbol. Its legacy readers go at G7.
- **F2b.** `LibraryImport(BindingSymbol Decl, string Library, string Symbol, string Kind)` replaces `FunctionKoto Function`
  (`BindingModel.cs:672`); legacy readers use `(FunctionKoto)Decl.Declaration` (`EmissionArtifacts.cs:150`,
  `LlvmEmitter.cs:325-326`; 3 test lines).
- **F2c.** A `[Transitional("R8")]` symbol view of the nine `DictionaryStorage` callees the legacy rows call (`KimiLibrary.cs:37-48`;
  `LlvmEmitter.cs:191-197`; `Binding.DictionaryStorage.cs:13`). No catalog row and no `KimiDeclarationId`. ARCH's temporary
  structure list gets one row.

### 4.10 Runtime location text (P5) — `C/Core/SourceLocationTable.cs`

`Text(SourceRef)` renders SPEC 22.5.1's `path:line:column` with the existing algorithm (`C/Emission/SourceLocationTable.cs:21-67`),
resolving through `Compilation.SyntaxOf`. One table per compilation, memo keyed (document, offset), cleared when the project
directory or program version changes. Which site an Abort reports is chapter C's. The test manifest's file/line/column
(`TestCatalog.cs:256-265`) is another contract and stays separate. ARCH dependency note: the Backend reads it (OQ4).

### 4.11 `TyCtxt.Instantiates(BindingSymbol definition)` (F3)

- True when the definition is not a specialization and it, or any enclosing declaration on its scope chain, has a Type,
  Semantics or length slot (SPEC 6.1, 9, 8.15.1). Origin slots do not count: the key erases Origins.
- It covers every `MirBodyKind` key symbol: Fn, Test, Closure (enclosing chain), the Getter/Setter executing functions, the
  StaticInit property and DefaultTemplate.
- F3 re-points `Binding.ClosureInstances.cs:38` (held: one adv line) and `GenericStoragePlan.cs:68` (a D5 mechanical
  follow-up, bp-checked). It deletes `FunctionKoto.RequiresInstantiation`, which removes a Syntax→Binding read. The rule equals
  the property except for multi-level nested named functions (fact 7), which F3's tests cover in both directions.

### 4.12 Builder and program facts (F8, F9, F10)

As the plan says: `TestRecordValue` operands (F8, PD22), `$abort`'s SourceInfo (F9), `MirProgram.StaticInit(body, operand)`
(F10, W2). No Binding fact.

---

## 5. Representations and what becomes unnecessary

| New or changed representation | Owner | R7 shape |
| --- | --- | --- |
| `VTableDef` (deferred view of R2b-D3) on `AdtDef` | Binding | declaration table |
| `RequirementTarget` + local kind enum; `ComparisonNode` + local kind enum; view memos | Binding (`TyCtxt.Dispatch.cs`) | TyCtxt views |
| `Binding.ResolveRequirementWitness` | Binding | internal of R7's Contract solver |
| `AdtDef.IsCLayout`; `StartupResult.Symbol`; `LibraryImport.Decl` | Binding | declaration-table columns |
| `[Transitional("R8")]` Dictionary-storage symbol view | Binding (`KimiLibrary`) | removed at R8 |
| `SourceLocationTable` in Core | Core | `SourceRef` service |
| `TyCtxt.Instantiates` | Binding | TyCtxt view |

| Deleted or made unnecessary | Stage |
| --- | --- |
| `FunctionKoto.RequiresInstantiation` (Syntax reading Binding) | F3 |
| Per-call writes of the built-in target symbols | F5a |
| `LibraryImport.Function`; the Emission copy of `SourceLocationTable` and its second instance | F2b; P5 |
| `VirtualGenerationPlan` (path/span sort, slot rebuild, re-proof; 398 lines) | G7 |
| `ImplementationContext`, `IsInapplicableVirtualBody`, `DictionaryStorageCall` readers | G9 |
| `GenericStoragePlan.Comparisons`, `LlvmModuleWriter.BuiltinComparisons`, `BodyLowering.ContractComparisons` (≈ 360) | G7–G8 |
| `ComparisonPlan(CallPlan)` (moved from R7) | H11 |
| `InstantiateForwardedCall` and its requirement path; `TryResolveFormattingCallback` | H11 (plan) |
| Private `ComparisonPlan`, `BoundComparison`, `comparisonPlans`, `RequirementImplementation`, `requirementCalls` | A-11 or R7 |
| Post-Bind bridge creation (`CollectWitnesses`, forwarded calls) | with its legacy callers (G7, H10, H11); MIR never creates bridges |
| F2c view | R8 |

Size (H): chapter F adds ≈ +480 compiler lines and removes ≈ −80 in R5, ≈ +400 net, against the plan's "facts ≈ +550"
(F5p is dropped and D2's `Module` column is gone). Tests ≈ +520. Speed: every answer after its first instance is one memo
probe; no CallPlan is synthesized or re-set during collection or codegen; `VTableDef` costs O(slots) per pass with no sort.
Warm rebuild is 0 B, asserted by `Purpose=Allocation` rows in F4, F5b and F6 and by K1/K4's collector rows.

---

## 6. Interfaces to chapters K and C

**Provided by F:**

| Fact | Consumer unit (chapter) | Status against the other designs |
| --- | --- | --- |
| `Instantiates` (F3) | K1 roots (K-D4) | Agrees on the rule. **Mismatch: file** — K design 2 puts it in `TyCtxt.Erasure.cs` beside `Erase`, D2 in `TyCtxt.Instances.cs` (open point 3) |
| `VTableDef`, `Applies` (F4) | K8 items, C6c | Agrees |
| `Witness` (F5b) | K6 (edge row 3), C9b, K6's formatting rows | **Mismatch**: K design 2 calls `Witness(requirement, closed)` without the Contract reference and maps results only to Body or Builtin items. It needs the `contract` input and an item row for the Storage kind (a property-witness shim with a `DropGlue` target for Set). Refused and Limit map to K's capability gap and `Admit` order 1, as K already has |
| `Comparison` (F6) | K's Builtin item targets; C8f | Agrees, given the `contract` input |
| `AdtDef.IsCLayout` (F1) | P7b (C) | Agrees |
| `StartupResult.Symbol` (F2a) | C0b2 Entry, K11 roots | Agrees. Implicit startup = `MirDefKey(null, Fn, 0)` (no `Module` column) |
| `LibraryImport.Decl` (F2b) | P8 C-convention `Abi.Of` | Agrees |
| Dictionary-storage symbols (F2c) | C8e | Agrees |
| `TestCase.Symbol` (P6, landed) | C10a, K11 | Agrees |
| Location text (P5) | C1a, C2a, C3b (C input 6) | Agrees |
| F8, F9, F10 | C10a, C3b, K7 | Agrees |

**Needed by F:**

- From K: representative unerased Substs as the inputs of `Applies`, `Witness` and `Comparison` (K-D1 prohibits proofs on
  erased values, which agrees). K6 is F5b's first consumer, in G:call.
- From C: the base-prefix layout rule (fact 10) stays, otherwise `Witness` needs a projection depth. The Storage shim writes its
  projections through FunctionCodegen's primitives (PD3), finding an inherited Field with `AdtDef.FindField`.
- From the planning session: G:compare as a review group (§7).

---

## 7. Unit mapping (changes to the plan's rows)

| Unit | Change | Prereq | Review | Hold |
| --- | --- | --- | --- | --- |
| F0 | as planned | W1 | R | W1 |
| F1 | as planned; `false` written for other structs | S5 | D+R (S3) | no |
| F2a | `Symbol` only (no module ordinal) | S5 | D+R (S3) | no |
| F2b | replaces `Function` by `Decl` (+6/−4, t+10) | S5 | D+R (S3) | adv (`BindingModel.cs`) |
| F2c | `[Transitional("R8")]` view, no catalog row | S5 | D+R (S3) | no |
| F3 | instance-facts partial; enclosing-declaration rule; deletes the Parsing property (+20/−4, t+30) | S5 | D+R (S3) | adv (one line, `Binding.ClosureInstances.cs`) |
| F4 | `VTableDef` on `AdtDef`; `Applies` memo; `ResetDispatch` | S5 | G:object | no |
| F5a | core with all kinds and the refusal rule; built-in symbols fixed per pass; bp (±170, net ≈ +30) | S5 | R | adv (`Binding.RequirementInstances.cs`) |
| F5b | `(requirement, contract, subst)`; kinds Body/Builtin/Storage/Refused/Limit; Self-bearing factory; twin test (+60, t+100) | F5a, K4 | G:call | **no** (new partial; was adv) |
| F6 | **moved before F7**; `(contract, self, operators)`; own composition over the core with Requirement leaves (+60, t+50) | F5a | **G:compare** (was G:library-2) | no |
| F7 | reads F6 (not `ComparisonPlan(CallPlan)`) (+70, t+50) | F0, F6 | **G:compare** + R4 reviewer | W1 |
| F8, F9, F10 | as planned | F0 / W2 | R | W1 / W2 |
| P5 | memo lifecycle; one table | S5 | D+R (S3) | no |
| F5p (D2) | **not adopted** | — | — | — |
| C8f | prereq F6 via G:compare; G:library-2 loses F6 | C8c, F6, F7 | G:library-2 | no |
| H11 | also deletes `ComparisonPlan(CallPlan)`; keeps `CallPlan.TupleOperator` and `InstantiateRequirementCall` to R7 | H10 | G:check-del | no |
| K6 (chapter K) | handles the Storage kind (property-witness shim item) and passes `contract` | K4, F5b | G:call | no |

Order: after S5, F1, F2a–c, F3, F4, F5a and P5 run in parallel. Then F5a → F6, and in W1: F0 → F7 (F6 merged) → F8 → F9.
K4 → F5b → K6. F3 stays on the critical path (S5 → F3 → K1).

---

## 8. Departures from owner-approved texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| Plan PD15 (adopted, OQ1) | F7 reads `TyCtxt.Comparison` instead of `ComparisonPlan(CallPlan)`; `ComparisonPlan(CallPlan)` is deleted at H11, not R7 | That plan is null for every tuple with an abstract leaf (fact 1), which F7 must also expand |
| Plan F6 row | Signature `(contract, self, operators)` instead of `(self, equality, operators)`; F6 before F7 in G:compare instead of after F5b in G:library-2 | ARCH:124 allows one `bool`; F7 is F6's first consumer (R4-M2a) |
| Plan F2a, F2b rows | No module ordinal; F2b replaces the column | Fact 8; one column per fact |
| Plan F5b hold | adv → no | The core is in F5a's new file |
| Plan H11 row | `ComparisonPlan(CallPlan)` leaves the "stay until R7" list | Its readers are gone by H5 |
| PLAN10 §5.7, A5P §7, DECISIONS R4-M28 | **None.** The property-witness shim stays (D2's departure is rejected); A5P §7's table is extended by DECISIONS entries, not edited; `Witnesses`' contract is unchanged (shared core, bp) | — |

---

## 9. Owner items and asks to R4

- **Vocabulary, codes:** none. No `IntrinsicId`, `CompilerFunctionKind`, `BindingFailure`, MIR vocabulary or diagnostic code
  (PD21). The two kind enums are local, not ARCH vocabularies.
- **Representations** (decided by the planning session within OQ4's package and recorded in DECISIONS by their first units):
  §5's first table. Two entries need the temporary-structures list: the F2c view (R8), and the legacy comparison twin (A-11 or R7).
- **ARCH rows** (written by the first implementing units): a new canonical row "Dispatch facts" (`VTableDef`, `Applies`,
  `Witness`, `Comparison` over `Binding.ResolveRequirementWitness`). "Effect facts" notes that `Witnesses` shares the core.
  "Stored shape" adds `IsCLayout` and `VTableDef` through `AdtDef`. "Types and substitutions" adds `Instantiates`. "Calls" names
  `TupleOperator` as a builder fact. A new "Runtime locations" row (Core) carries the two dependency notes OQ4 already lists.
- **A-11 (restated, optional).** A4's `Witnesses` comparison branch reads `TyCtxt.Comparison(operators: false)`. Two effects:
  a closed leaf with an inherited witness becomes exact instead of a Limit (today `RequirementImplementation` refuses base paths,
  `Binding.Formatting.cs:71`), and a leaf with an abstract part may become X2 atoms (R4-M28's open item) or stay a Limit, as R4
  chooses. It removes the legacy twin's last permanent reader. Needed by: removal of the private `ComparisonPlan`.
- **A-12 (D2): withdrawn.** F5p is not adopted, and the plan's F7 row already asks for an R4 reviewer.

---

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| The witness view diverges from the materializer (six cases plus OCC-X) and native code calls the wrong body | F5a first (bp proves identity); F5b's twin test over the corpus |
| Tuple comparisons are silently wrong under sel until F7 lands | F7 in W1 early; C8f's NaN/±0 fixtures after F7 (plan) |
| Bound-reference requirement calls are rare (2 corpus sites); a lookup-supplied `DeclaringType` would hide the reference | F5b asserts it per call; on failure the reference needs its own Subst slot (full process) |
| Post-Bind proofs (`Applies`, `Witness`, `Comparison`) mutate interning or storage | Same class as `Apply`; F4/F5b/F6 assert that Binding issues and the HIR version are unchanged after the views run, plus warm 0 B |
| The premise-free scope for closed leaves disagrees with `Witnesses`' definition scope (H) | Twin test; an Origin-premised conditional conformance in the test set |
| If A-11 is declined, two comparison composition rules live until R7 | Recorded twin with its stage (open point 2) |
| Legacy generation refuses an inherited-witness tuple leaf that MIR accepts (H) | An OQ7 (a) row if a fixture shows it |
| The property shim's inherited-Field projection duplicates the builder's base-projection rule | Shim uses `AdtDef.FindField` and FunctionCodegen primitives; C's review checks it |
| Multi-level nested named functions change classification | F3 rows; bp on legacy |
| UTF-16 vs UTF-8 source order (fact 6) | An existing open issue for Field order as much as slots; record it in PLAN at F4 |
| Hold contention on `Binding.RequirementInstances.cs` (F5a) with R4's A4 lane | One adv hold line; F5a is small and bp |

---

## 11. Open points for the planning session

1. **Property witnesses.** (a) A Backend Storage shim (PLAN10 §5.7; chapter C §2.9), with K6 adding the item row. (b) D2's
   F5p: bridges created at Bind and given MIR bodies (a PLAN10 departure, a HIR walk root or a non-syntax builder entry, A1–A5
   on more bodies, A-12). **Recommend (a)** (facts 4, 12).
2. **Legacy comparison twin.** (a) Ask A-11 and accept the twin until it lands, or until R7. (b) F6's unit switches `Witnesses`'
   comparison branch itself (held file, a change to R4's contract, R4 reviewer). **Recommend (a).**
3. **File for `Instantiates`, `Erase` and the C-layout instance view.** (a) One partial `TyCtxt.Instances.cs` (the concern is
   instance identity; PD20's `TyCtxt.Erasure.cs` renamed). (b) `TyCtxt.Erasure.cs` as the plan and K design 2 say, F3 creating
   it. **Recommend (a)**, decided once for chapters F and K.
4. **Startup module column.** (a) Drop it (fact 8). (b) Keep it as an explicit two-byte contract. **Recommend (a).**
5. **F6 key.** (a) The Contract symbol. (b) The requirement symbol (D2), normalized from the built-in symbol MIR names (fact 3).
   **Recommend (a).**
6. **Projection depth in `RequirementTarget`.** (a) None (fact 10). (b) D2's `BaseDepth`, for a future layout without the
   base prefix. **Recommend (a).**
7. **Review grouping of F6.** (a) G:compare with F7 (R4 reviewer on F7). (b) F6 on its own D+R before F7. **Recommend (a).**
8. **F3 and the legacy reader.** (a) Re-point `GenericStoragePlan.cs:68` and delete the Parsing property in F3 (bp-checked).
   (b) Leave the legacy reader, so the property survives to G7. **Recommend (a).**
