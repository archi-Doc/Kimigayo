# Q6: Binding facts R5 consumes: dispatch, drops, objects, statics, tests

Read-only investigation. Code at checkout `dev` = `5c42b577` unless marked `origin/dev:` (`2aac946b`); documents read from
`origin/dev`. Pending branches checked where relevant (`claude/a5-1b-mir`, `claude/a5-1b-core`). Paths are repo-relative,
`C/` = `src/Kimi/Compiler/`. (H) marks an inference not verified by running code. Survey reports read first:
`emission-inventory.md`, `instances.md`, `layout-runtime.md`, `critic.md`; aligned with the deep reports `q1` and `q2`
already in this directory (their unit ids are cited where a unit here feeds or equals one there).

## 0. Answer in brief

- **Most of what R5 needs is already published.** Selection by Subst is `TyCtxt.Implementation` (origin/dev
  `C/Binding/TyCtxt.Effects.cs:64`; SPEC 8.13.3's key "excluding only Origins", `docs/spec/08-...md:912`, which
  `SignatureEquals` already ignores, `C/Binding/Binding.cs:271-312`). Destruction calls are `AdtDef.DestructorSymbol` +
  `TyCtxt.Synthesized`, exactly as A4 does (origin/dev `C/Mir/Analysis/MirEffects.Solve.cs:427-430`). Copy/Sealed are
  `TyCtxt.Copy`/`Sealed` (`TyCtxt.Effects.cs:59-61`). A selected override's context is `Synthesized(impl, declaring, [])`
  because a virtual slot has no function-owned generic parameters (`C/Binding/Binding.VirtualDeclarations.cs:30-33`).
  Caller-location is the catalog row `KimiLibraryCatalog.RequiresCallerLocation(BindingSymbol)` (origin/dev
  `KimiLibraryCatalog.cs:265-266`). Static slots are named by `MirBody.Statics[i].Symbol` (origin/dev `C/Mir/MirBody.cs:201`).
- **Of the "10 new-fact sites" only 6 need a new fact**: VTableDef with applicability (5 sites) and a Type-keyed
  comparison plan (1). `SelectSpecialization`, `HasFormattingCallback`, `TryResolveFormattingCallback` and
  `ImplementationContext` are covered by existing views (§2).
- **Facts outside the 82 sites that R5 also needs** (they hide in syntax reads, not Binding calls): requirement witnesses
  under a closed Subst (inside `InstantiateForwardedCall`), the tuple-operator comparison mode (`CallPlan.TupleOperator`,
  **dropped by MIR today**), the C/Kimigayo layout mode, symbol columns for the startup, imports, test cases and the
  Dictionary helper callees, and runtime location text by `SourceRef`.
- **Runtime type identity needs no published fact.** SPEC 3.3.1 defines it as "that Core with every Origin erased,
  recursively" (`docs/spec/03-types-and-values.md:187`). The text key `ObjectGenerationPlan.WriteKey` builds
  (`C/Emission/ObjectGenerationPlan.cs:246-307`) is used for token de-duplication and for a private constant
  `@__kimi_object_type_key<n>` that nothing reads (`LlvmModuleWriter.Objects.cs:100-106`; no reader in `src`, only
  `PipelineObjectTest.cs:32-33` asserts its presence). An interned Origin-erased Type (needed anyway by PLAN10 §5.7's
  `Layout.Of(erased Type)` and the instance key) is the key. This removes q2's proposed fact F2/G5.
- **Drop glue, vtables, descriptors, entry shims and comparison/format/arithmetic shims are Backend MonoItems**, not MIR
  bodies: `DispositionKind.Shim` already says "whose body the backend generates for its instance" (`C/Binding/KimiLibraryKinds.cs:174`).
  None needs `MirBodyKind.Shim` (critic C8's owner question is moot for them).
- **Recommended process:** one R5 fact-schema design (part of R5 step 0) taken through the full process once, recorded in
  DECISIONS; then each fact unit gets one design and one review (DECISIONS Q6 form 1), and the fact unit lands in one review
  group with its first consumer (R4-M2a, no stubs; Q6 form 2).

## 1. Sources and method

- Emission's Binding calls: `emission-inventory.md` §5 (82 sites), re-grepped for the members of this topic
  (`grep -n "IsInapplicableVirtualBody|VirtualApplicability|TryGetVirtualOverride|ImplementationContext|SelectSpecialization|\.ComparisonPlan\(|TryResolveFormattingCallback|HasFormattingCallback|IsVerifiedSpecialization|DestructionCall|ProveCopy|ProveSealed|DictionaryStorageCall|StoredType|InstantiateForwardedCall" C/Emission/*.cs`
  → 19 sites, listed in §2).
- Non-Emission readers of each member (for R6 staging): `git grep` over `src/Kimi src/Benchmark tests` excluding
  `/Emission/` (results in §9).
- Governing records: A5 plan §7 (fact table, "R7's target shape: SyntaxId columns or TyCtxt views, one owner, immutable
  after Bind, checked by its producer"), §9.1, §9.3; PLAN10 §5.3, §5.6.5-§5.6.7, §5.7; ARCH dependency rules and "R4 and R5
  in parallel"; DECISIONS R2b-D3, R4-M6a/b, R4-M23, R4-M28, R4-M41, R4-M51, Q5, Q6.

## 2. The topic's Binding call sites and what replaces them

| Member (sites) | Today | R5 replacement | Status |
| --- | --- | --- | --- |
| `IsInapplicableVirtualBody` (3: `LlvmEmitter.cs:512`, `GenericStoragePlan.cs:127`, `ObjectGenerationPlan.cs:78`) | Refuted applicability of a virtual/override body's own slot (`Binding.VirtualOverrides.cs:156-161`) | `VTableDef` + `TyCtxt.Applies` (§3.1); unnecessary if "generated" = reachable from output roots | **new** |
| `VirtualApplicability` (1: `VirtualGenerationPlan.cs:193`) | proof per `VirtualSlot` (`Binding.VirtualOverrides.cs:141-154`) | `TyCtxt.Applies(original, declaring)` | **new** (wrapper) |
| `TryGetVirtualOverride` (1: `VirtualGenerationPlan.cs:343`) | override → slot (`Binding.VirtualOverrides.cs:134-138`) | `VTableDef.Implementation(struct, slot)` | **new** |
| `ImplementationContext` (1: `VirtualGenerationPlan.cs:154`) | `FunctionItemContext(FunctionItemType(fn, [], declaring))` (`Binding.FunctionItems.cs:19-21`) | `TyCtxt.Synthesized(impl, implementingType, [])` (`TyCtxt.cs:231`); no own generics (`Binding.VirtualDeclarations.cs:30-33`) | exists |
| `SelectSpecialization` (1: `GenericStoragePlan.cs:455`) | call-keyed (`Binding.Specializations.cs:222`) | `TyCtxt.Implementation(fn, subst, 0)` (`Binding.Specializations.cs:226-245`) | exists |
| `IsVerifiedSpecialization` (1: `LlvmEmitter.cs:588`) | `CheckInputs` re-check | dropped | drop |
| `InstantiateForwardedCall` (1: `GenericStoragePlan.cs:519`) | forwarded call incl. requirement → witness (`Binding.Specializations.cs:87-192`, `Binding.RequirementInstances.cs:53-123`) | `TyCtxt.Compose` (`TyCtxt.cs:242-273`) for the Subst **plus** a witness view under a closed Subst (§3.2) | partly **new** |
| `ComparisonPlan` (1: `GenericStoragePlan.Comparisons.cs:26`) | key (target symbol, Type, Contract, operators) (`Binding.ComparisonPlans.cs:7-62`) | `TyCtxt.Comparison(self, equality)` view (§3.3); operator mode moves into MIR (§3.3) | **new** (view) |
| `TryResolveFormattingCallback` (1: `GenericStoragePlan.cs:189`) | `RequirementImplementation(site, T, BufferWriter|Utf8Format)` (`Binding.Formatting.cs:20-37`) | the requirement witness of `Utf8Format.format` / `BufferWriter.reserve` on T: `TyCtxt.Witnesses` (`TyCtxt.Effects.cs:39-40`), §3.2 | exists (H: BufferWriter row) |
| `HasFormattingCallback` (1: `GenericStoragePlan.cs:254`) | static test of `CompilerFunctionKind` (`Binding.Formatting.cs:15-16`) | the callee symbol's `CompilerFunction` (declaration table) | exists |
| `DestructionCall` (1: `GenericStoragePlan.Destructors.cs:30`) | CallPlan with `declaringType: type` (`Binding.Formatting.cs:39-53`) | `AdtDef.DestructorSymbol` + `Synthesized(d, type, [])` | exists |
| `ProveCopy` (2: `ObjectGenerationPlan.cs:176`, `BodyLowering.Elements.cs:306`) | proof under a syntax scope | `TyCtxt.Copy(null, T)` for a descriptor; MIR operand Copy/Move for elements | exists |
| `ProveSealed` (1: `LlvmEmitter.cs:37`) | layout callback | `TyCtxt.Sealed(null, payload)`, read by DropGlue only (§3.4) | exists |
| `StoredType` (1: `ObjectGenerationPlan.cs:244`) | base of a struct | `AdtDef.Base(type)` (`AdtDef.cs:39`) | exists |
| `DictionaryStorageCall` (1: `LlvmEmitter.cs:35`) | hidden callee of legacy Dictionary helpers | MIR calls already name storage functions (`CallPlan.cs:169`); helper callees need symbols (§3.7) | **new** (symbols) |

`TyCtxt.Copy/Sealed(null, T)` read premises at the library root (`Binding.EffectObligations.cs:113-121`), which hold none;
for a closed T the answer does not depend on premises (H: Origin-erased T; Copy and Sealed do not depend on Origins).

## 3. Facts

### 3.1 VTableDef and override applicability

**Today.** `VirtualGenerationPlan.Begin` collects virtual/override bodies, sorts them by `SourceDocument.Path` (ordinal,
UTF-16) then `Span.Start` (`C/Emission/VirtualGenerationPlan.cs:41-54`), groups them per owning struct (`:55-71`) and
numbers each struct's own virtuals after its base's count (`Shape`, `:357-386`). `GetLayout` copies the base's choices
and replaces a slot with an override via `TryGetVirtualOverride` (`:301-355`); `GetTable` fills an entry only when
`VirtualApplicability` is `Proven`, leaves null when `Refuted`, fails otherwise (`:190-208`). `Address` synthesizes
`__kimi_virtual_item<n>` thunks for erased Items (`:227-293`). Binding keeps the semantic correspondence:
`VirtualSlot(Original, DeclaringType)`, `virtualOverrides`, `overrideEntries` (`Binding.VirtualOverrides.cs:12-19, 275-284`).

**Required meaning.** SPEC 6.4 / 13.6 dynamic dispatch; slot numbering is implementation-defined and module-local (IMPL
21.1.3: "virtual-dispatch tables ... implementation-defined, module-local", `docs/impl/21-...md:67`). PLAN10 §5.6.5: the
slot number is Binding's `VTableDef`; the re-sort is unnecessary. DECISIONS R4-M28 and the ARCH R5 row: MIR holds bodies
no instance applies; generation reads applicability to skip them.

**Finding.** The sort is a stand-in for declaration order. A struct's members are already in SPEC 6.2.1's logical
declaration order in `container.Members` (fragments merged; `docs/spec/06-...md:52-60, 125-127`), which is the order
`AdtDef.Fields` takes (`Binding.Storage.cs:225-306`), and `virtualDeclarations` is filled in node order
(`Binding.VirtualDeclarations.cs:92-99`). The path sort can differ from SPEC 6.2.1 only for multi-file fragments with
non-ASCII names (UTF-16 ordinal vs UTF-8 bytes), which no behavior observes. So VTableDef needs **no sort**: slots = the
base's slots, then the struct's own `virtual` members in member order.

**Design (R7 target shape: a declaration-table view on the struct's symbol, like `AdtDef`).**

- Producer: Binding, filled in `PrepareVirtualOverrides` per pass (the component that matches overrides), arrays reused per
  pass (warm zero allocation), immutable after Bind.
- Contract: `VTableDef.Slot(BindingSymbol original) → int` (index in the root's table, stable across derived types);
  `VTableDef.Count(BindingSymbol structure) → int` (inherited + own); `VTableDef.Implementation(BindingSymbol structure,
  int slot) → BindingSymbol` (own override, a base's override, or the original). Input is a resolved struct; a Binding
  failure leaves no row (consumers never see rejected structs).
- Applicability: `TyCtxt.Applies(BindingSymbol original, BoundType declaring) → bool?` over `VirtualApplicability`
  (true Proven, false Refuted, null Unknown; null for a closed declaring Type is a broken contract → `InternalInvariant_Kd`).
- Consumer rule (Backend `VTable(T)`, closed T): for each slot i, `impl = Implementation(T.Symbol, i)`, the implementing
  instance = the first Type on `AdtDef.Base` chain of T whose symbol owns `impl`; the declaring instance of the original
  likewise; null entry when `Applies` is false; else `Fn(impl, Synthesized(impl, implementing, []))`.
  A `Virtual` MIR callee is named by its original under its Subst (DECISIONS R4-M41; origin/dev `MirBuilder.Calls.cs:122`),
  so dispatch = load slot `Slot(original)`.
- First consumer: Backend `VTable(T)` MonoItem, the Virtual callee lowering and the virtual-Item thunk (q2 C6).
- Deletes at R6-gen: `VirtualGenerationPlan.cs` (398), `ImplementationContext`, `IsInapplicableVirtualBody` (Emission-only
  readers, §9); `VirtualApplicability` folds into `Applies` (its test `VirtualApplicabilityDefinitionTest` moves to the view).
- Size (H): Binding +80-100, tests +80.
- A5 sharing: R5-only row of A5 plan §7. A4 reads virtual call bounds (`TyCtxt.DispatchBounds`), not slots.
- Risk: `Applies` for a generic struct's instance runs a proof after Bind (same "immutable after Bind" class as
  `TyCtxt.Apply`, A5 plan §11). For non-generic declaring Types it could be precomputed in Bind.

### 3.2 Specialization and requirement witnesses by Subst

- **Specialization:** `TyCtxt.Implementation(function, subst, 0)` returns the specialization a closed key selects, else
  the function (`Binding.Specializations.cs:226-245`). The key compares Types with `SignatureEquals`, which compares
  kinds, Semantics, lengths, symbols and components, never Origins (`Binding.cs:271-312`), matching SPEC 8.13.3. So an
  Origin-erased Subst selects correctly. **No new fact.** First consumer: R5 collector (instances topic). A4 already reads it
  (`MirEffects.cs:193`, `MirEffects.Solve.cs:333,488`, per `instances.md` §1.9).
- **Forwarded calls:** `TyCtxt.Compose(inner, outer)` gives the callee Subst in an instance (`TyCtxt.cs:242-273`), checked by
  `TyCtxtTest.ComposeIsTheForwardedInstantiation`. It does **not** resolve a `Requirement` callee whose Self becomes closed.
  `InstantiateForwardedCall` delegates that to `InstantiateRequirementCall`, six cases (`Binding.RequirementInstances.cs:53-123`):
  abstract Self stays a requirement call (`:56-66`), property requirements (`:68-71`), built-in compiler targets (`:73-80`),
  fixed-array entries (PLAN G32, `:82-85`), integer positions (`:87-90`), conformance witness with base receiver projection
  (`:92-107`, `ProjectWitnessCall :137-165`, exclusive receiver → null "OCC-X").
- **What exists:** `TyCtxt.Witnesses(requirement, self, definition, into)` (`TyCtxt.Effects.cs:39-40` →
  `Binding.RequirementInstances.cs:13-50`): built-in targets and conformance witnesses as (Target, Declaring), and for
  BuiltinEquals/BuiltinCompare the comparison plan's **leaves**. That is A4's contract ("all bodies the requirement may
  run"). Code generation needs "the one body a closed requirement call runs" (for a tuple Self, the comparison shim itself,
  not its leaves), and the three cases Witnesses lacks.
- **Design:** `TyCtxt.Witness(BindingSymbol requirement, Subst closed) → (BindingSymbol Target, Subst Subst)?` sharing one
  private resolution core with `Witnesses` (`CompilerRequirementTarget`, `ConformanceWitness`, `FixedArrayWitness`,
  integer positions, property bridges). The base receiver projection is physically a no-op (a base is a prefix of the
  derived layout, `AggregateLayout.cs:287-292`), so the view returns only target and Subst. `Witnesses` becomes "Witness,
  then a comparison shim's leaves", one core. Formatting callbacks are this view on `Utf8Format.format` /
  `BufferWriter.reserve`.
- First consumer: the collector's requirement-callee resolution (q2 C3 lists "Requirement resolution fact").
- Size (H): Binding +100-130, tests +100 (a twin test like `ComposeIsTheForwardedInstantiation`: over the corpus, the
  Subst view's target equals `InstantiateForwardedCall`'s).
- A5 sharing: shares its core with A4's `Witnesses` (R4-M28 row of A5 plan §7: "Specialization by Subst"); A5's Loan flow
  reads `TyCtxt.Retains` instead (R4-M51).
- Not listed in A5 plan §7 (instances.md §6 noted the same); its contract is not fixed anywhere → schema item.

### 3.3 Comparison, formatting and arithmetic shims

- **Comparison plan is already Type-keyed.** The cache key is (site `BindingSymbol` = the BuiltinEquals/BuiltinCompare
  target, Type, Contract identity, operators) (`Binding.ComparisonPlans.cs:7, 11-12, 17-62`); "site" is a symbol, not syntax.
  A view `TyCtxt.Comparison(BoundType self, bool equality) → BoundComparison?` over the private overload (operators false),
  with each leaf's `(Target, Subst)` from `TyCtxt.SubstOf(leaf.Implementation)`, is enough for the Contract-mode shims.
  First consumer: Backend BuiltinEquals/BuiltinCompare shim (and the legacy Dictionary key-equality row, today
  `DictionaryComparison`, `:14-15`). Size (H): Binding +20, tests +40. A4 shares the same tree through `Witnesses`' leaves.
- **Finding: MIR drops the tuple-operator mode.** SPEC 13.4: tuple operators compare elementwise/lexicographically and "an
  unordered pair makes all four ordering operators false" (`docs/spec/13-...md:179-183`), while a generic `T == T` uses T's
  Contract mapping, NaN-reflexive for f64 (`:187`). Binding selects both through BuiltinEquals/BuiltinCompare and marks the
  operator form with `CallPlan.TupleOperator` (`CallPlan.cs:117-118`, set in `Binding.SelectedRequirements.cs:39`). Legacy
  generates IEEE equality vs NaN-reflexive `equals` by that bit (`GenericStoragePlan.Comparisons.cs:173`;
  `LlvmModuleWriter.BuiltinComparisons.cs:31-58`), reserves code 2 for unordered (`GenericStoragePlan.Comparisons.cs:152`;
  `LlvmModuleWriter.BuiltinComparisons.cs:78-89`) and wraps the relation as `r != 2 && r <op> 0` (`TupleRelation`,
  `:106-124`; chosen at `BodyLowering.ContractComparisons.cs:35`). MIR builds `Fn(target, SubstOf(call))` (origin/dev
  `MirBuilder.Calls.cs:60, 118-124`; `SubstOf` has no mode, `TyCtxt.cs:201-204`) and `Binary(relation, r, 0)`
  (`MirBuilder.Calls.cs:14-36`). So a MIR codegen cannot know the mode, and `(1.0, nan) > (1.0, 2.0)` would be true.
  Not a Binding fact gap (the CallPlan has it); a MIR representation gap. Options (planning session; owner only for B):
  - **C (recommended):** the MIR builder expands an operator-mode comparison by Binding's published operator-mode plan
    (P6 owns desugaring, ARCH target pipeline): numeric leaves as `Binary` (IEEE by construction), string and Contract
    leaves as calls, nested tuples recursively, lexicographic chain with early exit. Shims keep one meaning (Contract
    mapping); `TupleRelation` has no successor. Cost: more MIR per tuple-operator site; shared file `MirBuilder.Calls.cs`
    (held, and due for restructuring after 4 consecutive changes on origin/dev plus 2 on `claude/a5-1b-mir`, critic §3.7.4).
  - **A:** `MirCallee.Arg` on a Shim-disposition Fn callee carries the mode; the builder adds the `!= 2` guard. Small, but
    gives `Fn.Arg` a second meaning (today the default-template index, `MirProgram.Callee`, origin/dev `MirProgram.cs:58-59`),
    a MirCallee contract change (full process, R4-M41 precedent).
  - **B:** distinct compiler functions for operator mode: two `CompilerFunctionKind` entries (owner vocabulary approval,
    limit 76), which R8 then removes.
- **Formatting:** BuiltinFormat is a per-builtin-Type shim with no plan; `WriterWrite`/`TextToString`/`TextTryFormat`
  (Body disposition, legacy rows) and `ErasedWriter` (`KimiLibraryKinds.cs:183-226`) need T's callback = the witness view
  of §3.2 (FixedBuffer/HeapBuffer are built-in for the writer, `Binding.Formatting.cs:30-33`).
- **Arithmetic:** BuiltinArithmetic's operation is recoverable from the callee symbol (its scope owner is the arithmetic
  Contract, `Binding.Formatting.cs:122-127`; legacy reads it the same way, `BodyLowering.BuiltinArithmetic.cs:20-25`).
  No new fact.

### 3.4 Destruction and DropGlue

- **Facts used:** `AdtDef.DestructorSymbol(type)` (`AdtDef.cs:132`) + `TyCtxt.Synthesized(d, type, [])` (A4 identical,
  `MirEffects.Solve.cs:427-430`); `TyCtxt.Sealed(null, payload)`; `AdtDef.IsInlineStorage`; `MirTypes.MayNeedDrop` (the one
  presence rule, R4-M6b); `MirDropTable.Group` (A2, R4-M6a). **No new Binding fact.**
- **Why `__kimi_drop_aggregate` cannot be reused:** it calls the user destructor and then drops the Fields and the base
  (`LlvmModuleWriter.Aggregates.cs:197-207` then `:241-261`). Since R4-M6b the MIR destructor body drops its own Fields
  last-to-first and then `base1` (origin/dev `MirBuilder.Drops.cs:153-170`); "R5's glue calls it and adds no field cleanup"
  (DECISIONS R4-M6b). Porting the template destroys Fields twice.
- **Design (Backend `DropGlue(T)` MonoItem, T Origin-erased):** a struct that declares a drop → only the destructor instance;
  otherwise per immediate part from `Layout.Of(T)` whose Type `MayNeedDrop`, in SPEC 16.3.2 order (Fields last to first, then
  base; Tuple elements last to first; enum by tag; fixed array reverse loop); handles → obj/rc/arc release per
  SHARED_OBJECT_RUNTIME, the final release calling `DropGlue(payload)` directly when `Sealed`, else the descriptor's
  finalize; Function value → its ops-table drop; string → `__kimi_destroy_string`; Array/Dictionary → the legacy element
  drop + free row. Recursive Types through handles call their glue by name (function recursion), so the legacy "dynamic
  cleanup for recursive edges" fallback (`AggregateLayout.cs:106-146`) is unnecessary (H). Drop terminators: Keep → call,
  Flag → test flag, Open → its part rows, Elide → nothing (A2 rows).
- **Shared walk.** `MirTypes.Destruction` is a presence/visitor walk; on `claude/a5-1b-mir` it enters each Type once per
  query (`First`, R4-M58 on that branch), so it cannot emit per-part code; DropGlue uses Layout's part list and the
  presence rule, not the walk. `MayNeedDrop` gains a `seen` set on that branch (signature change to merge before DropGlue).
- **Dedup:** per erased Type first; shape interning like legacy (`AggregateLayout.cs:43-44, 354-372`) only if IR size or
  compile time measures worse (H).
- First consumer: Drop terminator codegen (q2 C4). Size (H): Backend +220, tests +150. Deletes at R6-gen
  `GenericStoragePlan.Destructors.cs` (88), the destruction half of `AggregateLayoutPool` (three callbacks
  `AggregateLayout.cs:66-72`, collection drops `:149-169`, destructor registry `:54, 305-311`), `WriteAggregateDestructor`.

### 3.5 Objects: runtime identity and descriptors

- Token = module-local ordinal of the interned Origin-erased payload Type (SPEC 3.3.1). Base tokens from the
  `AdtDef.Base` chain. Size/alignment from `Layout.Of`. Copy flag `TyCtxt.Copy(null, T)` (the metadata copy word is not read
  by the runtime templates: `LlvmModuleWriter.Objects.cs:24-25, 76-85` read token and destroy only (H: future Weak)).
  Destroy = `DropGlue(T)` or null. Vtable = `VTable(T)` (§3.1). Factory ABI as today (`ObjectGenerationPlan.cs:208-230`).
- Needs no Binding fact beyond an Origin erasure of a Type in `TyCtxt` (the instance-key prerequisite, A5 plan §9.1; also
  PLAN10 §5.7's `Layout.Of(erased Type)`). If the instance design erases only Substs, `TyCtxt.Erase(BoundType)` is added
  there, not here (one erasure rule).
- Deletes at R6-gen: `ObjectGenerationPlan.cs` (346) with `WriteKey`/`WriteContainers` (the project identity read,
  `:246-307`) and the `ToDictionary` LINQ use (`:17-18`). The unread type-key constant is incidental IR
  (`PipelineObjectTest.cs:32-33` → classify).
- Closure runtime keys: legacy keys a closure by path and span plus components; the erased Type keys it by symbol and its
  (normalized) context, so identities can only become finer (H: no program tests a closure's runtime identity).

### 3.6 Layout facts

- **Layout mode:** Binding decides `#Layout("C")` per struct in `ValidateLayoutFragments` (`Binding.Attributes.cs:405-475`,
  `this.cLayouts.Add`, `:474`). Publish `AdtDef.IsCLayout(type)` (one bool per declaration, set in that pass). Backend
  `Layout.Of` reads it instead of the attribute chain (`AggregateLayout.cs:256-266`). Size: Binding +8, tests +15
  (`LayoutAttributeBindingTest`). Q6 form 1 if the schema fixes it (PLAN10 §5.3 lists "レイアウト属性" in `AdtDef`).
- **Open, bases, empty:** Binding already rejects C layout on open, derived or empty structs (`Binding.Attributes.cs:466-470`,
  `InvalidCLayout`), so `AggregateLayout.cs:300` is a re-check; Kimigayo layout ignores `open`. **No `Open` fact.**
- **Destructor symbol:** exists (`AdtDef.DestructorSymbol`); Layout carries no destruction (§3.4).
- **Three callbacks:** `CollectionDrop`, `InstantiateDestructor` → DropGlue and the collector; `IsSealedObjectTarget` →
  `TyCtxt.Sealed` read by DropGlue. `Layout.Of` becomes a pure memoized function.
- **Managed Types:** field counts Dictionary 7, Array 3, Slice 2 (`AggregateLayout.cs:297`) are ported as Backend constants
  with an R8 removal mark (PLAN10 §5.7); `KimiLibraryShapeTest` pins the record Fields (`:331-340`). No fact.
- **Inferred C-layout zero-sized instantiations** ("Inferred instantiations reject at generation",
  `Binding.Attributes.cs:382-403`) are a SPEC 8.15.3 layout obligation for the collector (OD4), not a fact.

### 3.7 Locations, caller location, test cases, library callees, startup and imports

- **Location text by `SourceRef`:** `SourceLocationTable.TryGet(Koto, ...)` reads `CodeContext.SourceDocument` and
  `Span.Start` (`C/Emission/SourceLocationTable.cs:21-67`). Core owns syntax identity (ARCH canonical row); move the table to
  Core keyed by `SourceRef` (the old caller passes `SourceRef.Of(node)`, behavior-preserving). Equals q2 F1/G4. Needs an ARCH
  dependency note that the Backend reads Core's location text (§5).
- **Caller location:** the catalog row is symbol-keyed (`RequiresCallerLocation(BindingSymbol?)`). Codegen rule: a call of
  a callee that requires it passes its own hidden parameters when the calling body requires it too, else the location text
  of the call's `SourceInfo`; Aborts inside such a body report its hidden location (SPEC 22.5.1,
  `docs/spec/22-...md` §22.5.1). No fact.
- **`$abort`:** legacy reports `call.Parent` (the `$abort` macro, `BodyLowering.Calls.cs:430`); HIR publishes the macro as
  `HirOp.Group` (`Binding.Hir.cs:275`), so the MIR Abort call's `SourceInfo` is the inner invocation (H: one column later).
  SPEC 22.5.1: "the `$abort` expression". Builder rule (shared file): the Abort call takes the macro's `SourceInfo`.
- **Test cases:** `TestCase.Function` is a `FunctionKoto` (`src/Kimi/Testing/TestCatalog.cs:14`), read only by
  `TestRuntime.Create` (`TestRuntime.cs:13-36`); add a `BindingSymbol` column (MIR key `MirDefKey(symbol, Test)`,
  origin/dev `MirBuilder.cs:124`). Site ids already by `SourceRef` (`TestCatalog.SiteOf`, `:61`); MIR's `TestObserve`
  carries the site as a `Location` const encoding the `SourceRef` (origin/dev `MirBuilder.Control.cs:199`). Equals q2 F3/G6.
- **Dictionary helper callees:** `KimiLibrary` holds 11 `DictionaryStorage` functions as `FunctionKoto`
  (`C/Binding/KimiLibrary.cs:37-48`), 7 of which the legacy helpers call (`LlvmEmitter.cs:191-197`). Publish their
  `BindingSymbol`s (a lang-item view on `KimiLibrary`); the legacy Dictionary rows root `Fn(symbol, Synthesized(...))`.
- **Startup and imports:** `StartupResult.Function` and `LibraryImport.Function` are `FunctionKoto`s
  (`Binding.Startup.cs:25`; `BindingModel.cs:672`), read by `LlvmEmitter.cs:142, 264, 511`. Publish the startup's
  `BindingSymbol?` plus its module ordinal (an implicit startup's body is `MirDefKey(null, Fn, module)`, origin/dev
  `MirBuilder.cs:116-128`) and each import's symbol. "Declaration-table reads the Backend may keep" (emission-inventory §5.4)
  is true for the tables but not for these Koto-typed fields.

### 3.8 Statics, entry and test runtime

- **Slots:** `staticEnsure`'s operand is the body-local index `~place.Root` (origin/dev `MirBuilder.Places.cs:144-156`);
  `MirBody.Statics[i]` = `MirStatic(Symbol, Type, Mutable, RawHolding)` (`MirBody.cs:201`). The Backend maps it to a global
  slot keyed by the symbol; the initializer is `MirDefKey(symbol, StaticInit)` (`MirBuilder.cs:136`). No fact.
- **Generic containers:** `MirStatic` has no enclosing instance, so SPEC 22.4.4 keys (`Cache<i32>.count`) cannot be formed.
  Legacy excludes them too (`StaticScalar.IsDynamic`, `C/StaticScalar.cs:10-29`). R5: located `Unsupported_Kd` (capability
  row) until P42 adds the key to `MirStatic` (shared MIR representation, full process). Record as an open issue.
- **Cycle Abort:** SPEC requires `KIMI_E_STATIC_CYCLE` at the accessing Place (`docs/spec/22-...md:356, 403, 429`); legacy
  emits `KIMI_E_ABORT: Static initialization cycle` at the declaration (`LlvmEmitter.cs:175`, `LlvmModuleWriter.Statics.cs`),
  recorded wrong in STATUS (`docs/STATUS.md:66`) and PLAN P42 (`docs/dev/PLAN.md:85`); pinned by
  `StaticInitializationTest.cs:127`. MIR's `staticEnsure` already sits at the access with its `SourceInfo`. Recommend the SPEC
  code in the MIR codegen (ledger (a)); the test's expectation changes at R6-gen with the SPEC clause as reason.
- **Literal folding:** legacy folds bool/integer literal statics to constants (`StaticScalar.TryGet`, `:38-58`); MIR never
  folds. Codegen rule (O2 parity): a `StaticInit` body that only assigns a scalar `Const` to `_0` and returns becomes a
  constant global and its `staticEnsure` emits nothing. Semantically invisible (no initializer effects, no cycle, no drop) (H).
- **Shutdown:** SPEC 22.4.3's two-phase shutdown (`docs/spec/22-...md:362`) has no implementation; a static whose Type
  `MayNeedDrop` needs it. PLAN10 §5.6.6 keeps P42's remainder `Unsupported_Kd` until P42 resumes (PLAN: after R6). Owner
  choice (§5): keep that, or let R5's `Shim(Entry)` carry the shutdown table (≈ +60 lines) since the state machine is new code.
- **Entry:** `Shim(Entry)` = init (nothing today) → startup body → shutdown hook → `__kimi_exit(0)` (IMPL 21.4.2; legacy has
  no shutdown, `LlvmEmitter.cs:263-271`). `Shim(TestEntry)` ports `TestRuntime.Create` with names from the symbol column.
  Both are Backend MonoItems (no MIR body).
- **Test phases:** `TestPhaseEnter/Leave` have intrinsic rows (`MirDescriptors.cs:168-169`) but no MIR producer; the builder
  comment assigns phases to the backend (origin/dev `MirBuilder.Control.cs:193`; PLAN10 §5.6.7). Codegen wraps Drops with a
  runtime verdict (A2), statements in `Deferred` scopes, `staticEnsure` initializer calls and the shutdown. No fact.
- **Test record values:** legacy snapshots both scalar operands of a comparison condition (`BodyLowering.Testing.cs:31-44`,
  frame kind 7, `TestRuntime.ll:277-295`); MIR emits no `TestRecordValue`. A codegen rule reading the condition's defining
  rvalue would be a consumer deciding what the builder should publish (AGENTS symptom list). Builder rule (shared file
  `MirBuilder.Control.cs`, `Observe` at origin/dev `:237-250`): before `TestObserve`, `TestRecordValue(side, operand)` for
  each scalar operand of a built-in comparison condition.

## 4. Sharing the A5 plan §7 fact table (item 9)

| Fact | Producer | R7 shape | R5 first consumer | A5/A4 reader | Owner item |
| --- | --- | --- | --- | --- | --- |
| VTableDef | Binding (`PrepareVirtualOverrides`) | declaration-table view on the struct symbol | VTable(T), Virtual callee, virtual-Item thunk | none (A4 reads `DispatchBounds`) | none (DEC entry) |
| Override applicability | Binding | `TyCtxt.Applies` | VTable(T) null slots; body skip | none | none |
| Specialization by Subst | Binding | `TyCtxt.Implementation` (exists) | collector | A4 (exists) | none |
| Requirement witness by Subst | Binding | `TyCtxt.Witness` sharing `Witnesses`' core | collector, formatting rows | A4 `Witnesses` (same core) | none |
| Comparison plan by Type | Binding | `TyCtxt.Comparison` | equals/compare shims, Dictionary key row | A4 via `Witnesses` leaves | none |
| Tuple-operator mode | MIR builder (from `CallPlan.TupleOperator`) | builder expansion (C) | codegen | A1-A5 see the expanded comparison | only option B (vocabulary) |
| Copy, Sealed | Binding | `TyCtxt.Copy/Sealed` (exist) | descriptor, DropGlue | A4, A5 (R4-M36) | none |
| Destruction call | Binding | `AdtDef.DestructorSymbol` + `Synthesized` (exist) | DropGlue | A4 (exists) | none |
| Runtime identity | TyCtxt erasure (instance key) | erased interned Type | ObjectDescriptor | none | none |
| Layout mode | Binding | `AdtDef.IsCLayout` | `Layout.Of` | none | none |
| Symbol columns | Binding, Testing | symbols on `StartupResult`, `LibraryImport`, `KimiLibrary`, `TestCase` | Entry/TestEntry, import ABI, Dictionary rows | none | none |
| Location text | Core | `SourceRef` service | Abort/caller locations | none | ARCH dependency note |
| C2 closure contract | Binding (A5 F7) | per A5 schema | closure erasure shims, `Abi.Of` of a Closure body | A5 F7 | per A5 plan |
| C8 selected Callable signature | landed (R4-M41: Value callee `Arg`) | TypeToken const | Value-call ABI, function-item shims | A5 step 1 | none |

## 5. Decisions needed

1. **Owner — process weight of R5 facts.** A5 plan §9.3 lightens a Binding view only "when the view's contract is fixed by
   the approved schema"; for R5's rows §7 names facts without contracts. Recommend one R5 fact-schema design (this report's
   §3 as input) through the full process, recorded in DECISIONS, after which every fact unit takes one design + one review
   (Q6 form 1) and lands in one review group with its first consumer (Q6 form 2). Alternative: each view through the full
   process (about 8 double designs).
2. **Owner — statics in R5.** (a) SPEC `KIMI_E_STATIC_CYCLE` at the access in MIR codegen (an (a) ledger row; the pinned
   `StaticInitializationTest` expectation changes at R6-gen) versus reproducing the legacy code; (b) P42's remainder
   (shutdown, aggregate and mutable statics) stays `Unsupported_Kd` per PLAN10 §5.6.6, or R5's new state machine includes the
   shutdown table. Trade-off: (b) pulled in costs about 60 lines and closes a STATUS defect early, but adds P42 work to R5
   ahead of PLAN's order.
3. **Planning session (owner only for B) — tuple-operator mode in MIR:** C (recommended), A or B (§3.3).
4. **Planning session, owner confirmation — ARCH dependency rows:** the Backend reads Core's location text and `Testing`'s
   catalog (today `src/Kimi/Testing` is outside ARCH's layers, critic §3.2).
5. **Planning session — "generated":** if generation roots are the output roots (PLAN10 §5.6.6), the inapplicable-body skip
   falls out of VTable reachability and `IsInapplicableVirtualBody` has no successor; if every nongeneric body is
   generated (legacy), the skip reads `Applies`. Either uses the same view.

## 6. Risks

- **Witness fidelity:** the Subst view must reproduce `InstantiateRequirementCall`'s six cases or native output calls the
  wrong body; guard with a twin test against `InstantiateForwardedCall` over the corpus before codegen uses it.
- **Tuple comparisons silently wrong** under MIR codegen if §3.3 is not settled first; add float-tuple native fixtures
  (NaN, ±0) at O0/O2 to the comparison codegen unit.
- **Double destruction** if any legacy aggregate-destructor text is reused; plus `NativeAllocationAudit` fails open
  (`layout-runtime.md` §5), so free counts may not catch it until the audit fails closed.
- **Post-Bind proofs:** `TyCtxt.Applies` and `Witness` on instance Types run Binding proofs after Bind (same class as
  `TyCtxt.Apply`, G128); warm zero allocation and mutation must be measured.
- **Shared-file holds:** the tuple-operator, `$abort` and `TestRecordValue` builder changes touch `Mir/Build/MirBuilder*.cs`,
  held by the R4 A5 MIR lane (origin/dev PLAN §3); `MirBuilder.Calls.cs` first needs a behavior-preserving split.
- **Slot-order and object-key tests read old internals** (`VirtualConditionalDispatchTest` `module.Objects[i].VirtualSlots`,
  `PipelineObjectTest` type-key names): migrate at R6-gen, not by matching legacy numbering.
- **Generic-container statics** stay `Unsupported_Kd` because `MirStatic` lacks the enclosing instance; a later P42 change to
  `MirStatic` is a shared-representation change.

## 7. Corrections to the survey reports and plans

1. `emission-inventory.md` §5.3 and A5 plan §7 list `SelectSpecialization` as needing a new fact; `TyCtxt.Implementation(fn,
   subst, 0)` already returns the closed key's specialization (`Binding.Specializations.cs:226-245`), and the key ignores
   Origins as SPEC 8.13.3 requires (`Binding.cs:271-312`).
2. `emission-inventory.md` §5.3: `HasFormattingCallback` is a static `CompilerFunctionKind` test (`Binding.Formatting.cs:15-16`)
   and `TryResolveFormattingCallback` is a requirement witness on T (`:20-37`); neither needs a new fact.
3. `emission-inventory.md` §5.1: "`InstantiateForwardedCall` → `TyCtxt.Compose`" is incomplete: `Compose` (`TyCtxt.cs:242-273`)
   does not resolve a Requirement callee to its witness; `InstantiateRequirementCall`'s six cases (`Binding.RequirementInstances.cs:53-123`)
   need a witness view under a closed Subst.
4. A5 plan §7 and `emission-inventory.md` §5.3: `ComparisonPlan(site)` is not "keyed by syntax": the key is (target symbol,
   Type, Contract, operators) (`Binding.ComparisonPlans.cs:7-17`). The real gap is the operator mode, which MIR drops
   (`MirBuilder.Calls.cs:14-36, 118-124` vs `LlvmModuleWriter.BuiltinComparisons.cs:106-124`).
5. `layout-runtime.md` §2.2/§10, `critic.md` §3.7.2 and q2 F2/G5: the runtime type identity key needs no published Binding
   fact; SPEC 3.3.1 defines runtime identity as the Origin-erased Core (`docs/spec/03-types-and-values.md:187`), and the key
   text feeds only token de-duplication and an unread constant (`LlvmModuleWriter.Objects.cs:100-106`).
6. `layout-runtime.md` §1.3: no `Open` fact is needed; Binding rejects open, derived and empty C-layout structs
   (`Binding.Attributes.cs:466-470`), so `AggregateLayout.cs:300` is a re-check.
7. `layout-runtime.md` §7: keying `SourceLocationTable` by `SourceRef` through `SyntaxTable.Nodes` inside the Backend still
   reads syntax; the location text must be a Core service (agrees with critic §3.7.2 and q2 F1).
8. `instances.md` §4.1 / `critic.md` C8: `MirBodyKind.Shim` is not needed for Entry, TestEntry, DropGlue, VTable,
   ObjectDescriptor or the comparison/format/arithmetic shims; `DispositionKind.Shim` already assigns their bodies to the
   backend (`KimiLibraryKinds.cs:174`).
9. `emission-inventory.md` §5.4: `Startup` and `LibraryImports` are declaration tables, but their entries hand out
   `FunctionKoto`s (`Binding.Startup.cs:25`, `BindingModel.cs:672`); the Backend needs symbol columns.
10. `VirtualGenerationPlan.cs:41-54`'s sort is not a semantic order to preserve: grouping per struct makes it each struct's
    declaration order, which Binding already has (`Binding.Storage.cs:225-306`, `Binding.VirtualDeclarations.cs:92-99`).

## 8. What R6 deletes because of these facts

- **R6-gen** (Emission-only readers, §9): `VirtualGenerationPlan.cs` 398, `ObjectGenerationPlan.cs` 346,
  `GenericStoragePlan.Comparisons.cs` 196, `GenericStoragePlan.Destructors.cs` 88, `StaticScalarEntry.cs` 19,
  `LlvmModuleWriter.Statics.cs` 66, `LlvmEmitter` static registration and `SupportedContainers` (≈ 45), the destruction half of
  `AggregateLayoutPool` (≈ 150), `WriteAggregateDestructor` (≈ 120); Binding `ImplementationContext`, `IsInapplicableVirtualBody`,
  `IsVerifiedSpecialization` (definition-only outside Emission); `TestRuntime`'s `FunctionKoto` map. ≈ −1,450.
- **R6-check** (readers in old Analysis and `Binding.EffectBounds`, §9): `StaticScalar.cs` 59 (`OwnershipAnalysis.cs` ×5,
  `Binding.EffectBounds.cs` ×1), `DestructionCall`, `TryResolveFormattingCallback`, the CallPlan overloads of `ComparisonPlan`
  and `SelectSpecialization`, `InstantiateForwardedCall` (≈ 105) and the call-keyed `InstantiateRequirementCall` path (≈ 120,
  replaced by the Subst view's core), and `CallPlan.TupleOperator` under option C. ≈ −350.

## 9. Non-Emission readers (R6 staging evidence)

`git grep` over `src/Kimi src/Benchmark tests`, excluding `/Emission/`: `ImplementationContext` 1 (definition),
`IsInapplicableVirtualBody` 1 (definition), `IsVerifiedSpecialization` 1 (definition), `VirtualApplicability` 2 + 2 in
`VirtualApplicabilityDefinitionTest`, `TryGetVirtualOverride` Hover ×3, `SemanticQuery`, 6 test files (stays),
`HasFormattingCallback` 2 (Binding), `TryResolveFormattingCallback` `Binding.EffectBounds` 1, `DestructionCall`
`Binding.EffectBounds` 1, `ComparisonPlan(` `Binding.EffectBounds` 1 and `Binding.RequirementInstances` 1,
`SelectSpecialization(` `Binding.EffectBounds` 1, `InstantiateForwardedCall` `Binding.EffectBounds` 1 + `TyCtxt.cs` (doc) +
`TyCtxtTest` 2 + `VirtualCallSelectionTest` 2, `StaticScalar.` `OwnershipAnalysis` 5 + `Binding.EffectBounds` 1.

## Units

Verification rows (AGENTS "Verification and Delivery"): **Compiler** = unit checks of the related tests plus a Session at the
end of the working session; **Documentation only** = diff and reference review. Codegen units also need the R5 evidence (A5
plan §9.1): legacy `.ll` unchanged, native outputs equal across pipelines on explicitly selected fixtures (`-Fixtures
'Family*.ll'`), and warm zero allocation on their inputs. Shared-file units record a hold in PLAN §3 before starting.

| id | title | stage | change (owner, files) | prerequisites | deliverable | verification | net size | diff. | review weight | MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- |
| Q6-0 | R5 fact schema | R5 (step 0) | design doc (`draft/Sketches/R5 Step 0/facts.md`, on instruction) fixing the contracts of §3: VTableDef/Applies, Witness, Comparison, tuple-operator representation, erasure as runtime identity, IsCLayout, symbol columns, Core location text; DEC entry draft | this report; instances and codegen designs (q2 S0) | approved contracts; owner answers to §5 items 1-2 | Documentation only (design review) | +0 code (doc ~250) | 3 | full process | no |
| Q6-1a | VTableDef and Applies | R5 | Binding: `Binding/VTableDef.cs` (contract header), fill in `Binding.VirtualOverrides.cs` `PrepareVirtualOverrides`, `TyCtxt.Applies` in `TyCtxt.Effects.cs` or a `TyCtxt.Dispatch.cs` | Q6-0 | slot, count and implementation per struct; applicability per closed declaring Type | new `VTableDefTest` (base prefix, member order across fragments, override replacement, inherited override, Refuted → null, generic instance, edit invalidation, warm zero allocation); `Virtual*Test` unchanged; Compiler row | +90 Binding, +80 tests | 3 | one design + one review (Q6 form 1), group with Q6-1b | no |
| Q6-1b | VTable(T), Virtual calls and virtual-Item thunk | R5 | Backend: `VTable` MonoItem, Virtual callee lowering (slot load), erased virtual-Item thunk (q2 C6 slice) | Q6-1a; Layout/Abi; collector; ObjectDescriptor (Q6-5) | virtual dispatch from MIR | `VirtualDispatchTest`, `VirtualConditionalDispatchTest`, `VirtualGenericDispatchTest`, `VirtualEntryErasureTest` under the selector; `Virtual*.ll`, Milestone 44 at O0/O2; Compiler row | +120, +60 tests | 3 | one design + one review (group with Q6-1a) | no |
| Q6-2 | Requirement witness by Subst | R5 | Binding: `TyCtxt.Witness(requirement, Subst)`; one private core with `Witnesses` (`Binding.RequirementInstances.cs`, `Binding.PropertyWitnesses.cs`, `Binding.FixedArrays.cs`, `Binding.IntegerPositions.cs`) | Q6-0; erased Subst (instances topic) | target and Subst of a closed requirement call, all six cases | twin test in `TyCtxtTest` vs `InstantiateForwardedCall`; `MirEffectsTest` unchanged (A4 shares the core); Compiler row | +120 Binding, +100 tests | 4 | one design + one review; group with the collector unit | no |
| Q6-3 | Comparison by Type and equals/compare shims | R5 | Binding: `TyCtxt.Comparison(self, equality)` over `Binding.ComparisonPlans.cs`; Backend: BuiltinEquals/BuiltinCompare shim bodies (tree, string compare, scalar leaves) | Q6-0, Q6-2, Q6-4b | Contract-mode comparison shims per Type | `ComparisonContractTest`, `ComparisonCompositionTest`, `BuiltinComparisonTest`, `ElementComparisonTest` under the selector; `*Comparison*.ll` O0/O2; Compiler row | +20 Binding, +100 Backend, +60 tests | 3 | one design + one review | no |
| Q6-4a | Split `MirBuilder.Calls.cs` | early-R6-prereq | Mir/Build: behavior-preserving split by responsibility (calls, comparisons, defaults); MIR goldens unchanged | hold free (R4 A5 MIR lane merged) | file below the three-consecutive-units trigger | `MirBuildTest`, `MirDumpTest`, tests/mir goldens equal; Compiler row | +130/−130 | 1 | one review | yes |
| Q6-4b | Tuple-operator comparisons in MIR | R5 | Mir/Build: expand an operator-mode comparison by the published plan (option C) or the chosen alternative | Q6-0 decision, Q6-4a | SPEC 13.4 tuple semantics representable in MIR | tests/mir golden (f64 tuple `<`, nested, string element); `MirBuildTest`, `MirValidatorTest`, `MirEffectsTest`; later `ComparisonOperatorTest` with native NaN/±0 fixtures under the selector; Compiler row | +60 Mir, +50 tests | 3 | full process unless Q6-0 fixes it, then one design + one review | yes |
| Q6-5 | ObjectDescriptor(T) | R5 | Backend: descriptor records (token by erased Type, base tokens via `AdtDef.Base`, `TyCtxt.Copy`, destroy = DropGlue, vtable), factory ABI; runtime text from q2 G3 | erased Type (instances), Layout, Q6-7, q2 G3 | object creation and type tests from MIR | `SharedObjectRuntimeTest`, `SharedArcRuntimeTest`, `SealedObjectFinalizationTest`, `GenericObjectFactoryTest`, `PipelineObjectTest` under the selector; `Object*`/`Shared*`/`Dynamic*` fixtures O0/O2 with allocation counts; Compiler row | +130, +60 tests | 3 | one design + one review | no |
| Q6-6 | `AdtDef.IsCLayout` | R5 | Binding: `AdtDef.cs` flag set in `Binding.Attributes.cs` `ValidateLayoutFragments`; first consumer Backend `Layout.Of` (layout topic) | Q6-0; Layout unit | layout mode without attribute syntax | `LayoutAttributeBindingTest` (C, Kimigayo, fragments); Layout golden tests; Compiler row | +8 Binding, +15 tests | 1 | one design + one review (Q6 form 1), group with the Layout unit | no |
| Q6-7 | DropGlue(T) and Drop codegen | R5 | Backend: `DropGlue` MonoItem per erased Type (destructor-only for drop-declaring structs, R4-M6b), handle release per SHARED_OBJECT_RUNTIME with Sealed direct path, legacy Array/Dictionary rows; Drop terminators by A2 verdicts (with q2 C4) | Layout.Of, collector, q2 G1/G3, NativeAllocationAudit fail-closed (tests topic), merge of `claude/a5-1b-mir` (`MayNeedDrop` signature) | owned values released exactly once | `MirDropTest`, `GenericDestructorEmissionTest`, `SharedObjectCleanupTest`, `SealedObjectFinalizationTest` under the selector; destructor/partial-move/handle fixtures O0/O2 with free counts; Compiler row | +220, +150 tests | 4 | one design + one review (contract R4-M6b + Q6-0) | no |
| Q6-8 | Location text by SourceRef | early-R6-prereq | Core: `SourceLocationTable` moved to Core, keyed by `SourceRef`; old callers pass `SourceRef.Of(node)` (= q2 G4) | Q6-0 or its own design; ARCH dependency note (§5 item 4) | Backend-legal location service | `AbortEmissionTest`, overflow/abort stderr fixtures; legacy `.ll` equal; Compiler row | +75/−69, +20 tests | 1 | one design + one review (fixed by Q6-0), else full process (moves a responsibility to Core) | no |
| Q6-9 | Caller location and `$abort` location | R5 | Backend: hidden location operands per `RequiresCallerLocation`, forwarding inside caller-location bodies; Mir/Build: the `$abort` call takes the macro's `SourceInfo` | Q6-8; q2 C3; Q6-4a | SPEC 22.5.1 locations from MIR | `AbortEmissionTest`, Array/Dictionary bounds stderr fixtures (G71 cases recorded) under the selector; tests/mir golden for `$abort`; Compiler row | +40 Backend, +3 Mir, +40 tests | 2 | one review | yes (builder part) |
| Q6-10 | Declaration-table symbol columns | R5 | Binding: `StartupResult` symbol + module ordinal (`Binding.Startup.cs`), `LibraryImport` symbol (`BindingModel.cs`), `KimiLibrary` DictionaryStorage symbols; Testing: `TestCase` symbol (= q2 G6) | Q6-0; each lands with its consumer (Q6-12, import ABI, legacy Dictionary rows) | no `FunctionKoto` reaches the Backend | `StartupBindingTest`, `LibraryImportTargetBindingTest`, `KimiLibraryShapeTest`, `TestProtocolTest`; legacy `.ll` equal; Compiler row | +20, +20 tests | 1 | one design + one review (Q6 form 1) | no |
| Q6-11 | Statics codegen | R5 | Backend: global slots by `MirStatic.Symbol`, `staticEnsure` state machine with `KIMI_E_STATIC_CYCLE` at the access, constant-`StaticInit` fold rule, Abort reason row; capability rows (generic-container statics, and drop-needing statics unless owner pulls shutdown in) (with q2 C7) | Q6-0 and §5 item 2; collector StaticInit item; Q6-8 | static programs from MIR within the decided boundary | `StaticInitializationTest` under the selector (SPEC cycle expectation per pipeline), `Static*` fixtures and tests/mir statics natively O0/O2; Compiler row | +160 (+60 if shutdown), +100 tests | 3 | one design + one review | no |
| Q6-12 | Shim(Entry) and Shim(TestEntry) | R5 | Backend MonoItems (LLVM text): entry calls the startup body key, shutdown hook, exit; test entry ported from `TestRuntime.Create` with names by symbol; Library output has none | Q6-10; q2 C0 | `__kimi_start` from the MIR backend for application and test builds | `TestProtocolTest`, `PipelineSwitchTest`, `test-testing.ps1` and `test-library.ps1` selected; Compiler row | +90, +40 tests | 2 | one design + one review | no |
| Q6-13 | Test phases in codegen | R5 | Backend: `TestPhaseEnter/Leave` around runtime Drops (A2), `Deferred` scopes, `staticEnsure` initializers and shutdown, test builds only (with q2 C10) | Q6-7, Q6-11, Q6-12 | cleanup/initialization phases reported as legacy | `TestProtocolTest` cleanup-phase cases under the selector; `test-testing.ps1` O0/O2; Compiler row | +50, +40 tests | 2 | one review | no |
| Q6-14 | `TestRecordValue` producer | R5 | Mir/Build: `MirBuilder.Control.cs` `Observe` emits `TestRecordValue(side, operand)` for scalar operands of a built-in comparison condition | Q6-4a hold window | operand snapshots representable in MIR | tests/mir golden; `MirBuildTest`, `MirEffectsTest` (Environment effect, test-only); `TestProtocolTest` under the selector later; Compiler row | +25 Mir, +25 tests | 2 | one review | yes |
| Q6-15 | R6-gen removals of dispatch, object, static and destructor plans | R6-gen | delete `VirtualGenerationPlan`, `ObjectGenerationPlan`, `GenericStoragePlan.Comparisons/.Destructors`, `StaticScalarEntry`, `LlvmModuleWriter.Statics`, `LlvmEmitter` static registration and `SupportedContainers`, the destruction half of `AggregateLayoutPool`; Binding `ImplementationContext`, `IsInapplicableVirtualBody`, `IsVerifiedSpecialization`; migrate `VirtualConditionalDispatchTest`, `PipelineObjectTest`, `VirtualApplicabilityDefinitionTest`, `StaticInitializationTest` (SPEC 22.4.3 reason) | R6-gen switch | no Emission dispatch/destruction plan remains | Session; all `Virtual*`/`Object*`/`Shared*`/`Static*` fixtures O0/O2; `ArchitectureRulesTest` | ≈ −1,450 | 2 | one review | no |
| Q6-16 | R6-check removals of call-keyed dispatch helpers | R6-check | delete `StaticScalar.cs`, `DestructionCall`, `TryResolveFormattingCallback`, CallPlan overloads of `ComparisonPlan`/`SelectSpecialization`, `InstantiateForwardedCall` and the call-keyed `InstantiateRequirementCall` path, `CallPlan.TupleOperator` (option C) | R6-check (old Analysis and `Binding.EffectBounds` deleted) | Binding dispatch facts only as Subst/Type views | Session; `TyCtxtTest`, `MirEffectsTest`, `Virtual*Test`, comparison tests | ≈ −350 | 2 | one review | no |
