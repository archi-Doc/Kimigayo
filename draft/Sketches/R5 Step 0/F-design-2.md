# R5 step 0, chapter F: the R5 fact schema (design 2)

Date: 2026-10-18. Status: draft design (S3, second independent design). Base: `origin/dev` at `041ced79`. Line citations are at
that base. "(H)" marks a claim not checked by running code. Independence: written without reading any other design or deep
dive of this chapter; `audit.md` (S1) did not exist on `origin/dev` or `origin/claude/r5-s1` when this was written.

Conventions follow the plan (`draft/Proposals/2026-10-17 R5 and R6 Implementation Plan.md`, "the plan"): `C/` =
`src/Kimi/Compiler/`, `B/` = `src/Kimi/Compiler/Backend/`, PLAN10, A5P, DEC, ARCH. "Hold" follows plan §3.5 and the owner's
OQ8 (DEC R5-M7 once merged).

---

## 0. Decisions in one page

1. **One schema rule for every R5 fact.** A fact has one producer, is published (or answerable by a pure view) after the final
   Binding pass and before its first reader, names definitions by `BindingSymbol` (the `MirDefKey.Decl` identity, DEC R3-6)
   and syntax by `SourceRef`, and never by a Koto. A view's memo lives until an edit, as `TyCtxt.Retains` does (DEC R4-M51),
   so a warm rebuild allocates nothing. This is R7's target shape (DEC R4-M35 OD2, A5P §7).
2. **Dispatch facts are three views over one resolver.** `ResolveRequirementWitness` (F5a) becomes the single requirement
   witness rule; `TyCtxt.Witness` (codegen, F5b), `Binding.Witnesses` (A4, R4-M28) and the bind-time CallPlan materializer
   (`InstantiateRequirementCall`) all call it. `TyCtxt.Comparison` (F6) composes comparisons on top of it, and
   `TyCtxt.Applies` with `VTableDef` (F4) answers virtual dispatch.
3. **F7 reads F6, not `ComparisonPlan(CallPlan)` (refines PD15).** `ComparisonPlan` returns null for a tuple with an abstract
   leaf (H), so it cannot drive a definition-level MIR expansion, and its CallPlan leaves are unreadable by the Backend. F6
   gives abstract leaves as requirement leaves (SPEC 13.4 last paragraph), so F6 moves before F7, and `ComparisonPlan(CallPlan)`
   leaves at R6-check with its two legacy readers instead of R7.
4. **Every witness names a body that exists.** Standard property-witness bridges are created lazily, after the HIR publish,
   and appear to have no MIR body (H). A new conditional unit F5p publishes them at Bind; the alternative, a Backend property
   shim, is PLAN10 §5.7's text but is rejected here (departure noted for the owner).
5. **No held file is touched where a new partial or an existing unheld owner suffices.** New views go into
   `TyCtxt.Dispatch.cs` and `TyCtxt.Instances.cs`; `VTableDef` hangs off `AdtDef` (not `BindingModel.cs`); F2c adds no
   catalog row. Held files remain only in F2b, F3 (one line), F5a and K2.

---

## 1. Required meaning

| Rule | Text it governs | Facts |
| --- | --- | --- |
| SPEC 6.4.1–6.4.3 | A virtual slot is selected statically; the Dynamic Type selects the most-derived implementation; an original's conditional-member premise decides whether the slot exists for a derived Type | `VTableDef`, `Applies` |
| SPEC 6.2.1; IMPL 21.1.1, 21.1.3 | Logical declaration order across fragments; dispatch tables are implementation-defined, module-local, deterministic | slot order |
| SPEC 8.6.1, 8.11.4, 8.13.3 | A requirement on a closed Self runs its conformance's witness through the Contract reference the call selected; a closed key selects its specialization | `Witness` |
| SPEC 11.9.2 | A standard property witness is a bridge checked under its conformance path's premises | F5p |
| SPEC 13.4, 13.4.1, 12.4.3 | Built-in operators on Tuples compare elementwise / lexicographically, an unordered float pair makes the four ordering operators false; a `T` operand uses `T`'s Contract mapping in every instance; `Equatable.equals` on floats is NaN-reflexive | `Comparison`, tuple-operator expansion |
| SPEC 8.15.1, 8.15.3; IMPL 21.3.1 | Generic bodies are checked symbolically; closed instantiations the output requires are generated; layout obligations (C-layout zero-sized Fields) are checked per by-value use for the checked set | `Instantiates`, C-layout obligation |
| IMPL 21.1.2; SPEC 6.7.1 | C mode places Fields in written order; open, derived and empty structs reject C layout | `AdtDef.IsCLayout` |
| SPEC 22.4.1–22.4.2, 22.6; IMPL 21.2 | Startup body selection; `#LibraryImport` declarations; C ABI | startup and import symbol columns |
| SPEC 22.5.1 | One displayed Abort location per site, same at every level and instance; `$abort` reports the `$abort` expression | runtime location text, F9 |
| SPEC 17.6.3; testing profile | A failed verification shows values obtained during the original evaluation | F8 |
| SPEC 22.4.3–22.4.4 | `staticEnsure` per slot; static keys erase Origins | F10 |
| SPEC 8.13.3, 3.3.1 | Origins are not part of instance identity | `ClosureContext` as `Subst` (PD5) |

---

## 2. The schema

### 2.1 Common contract of every chapter-F fact

| Clause | Rule |
| --- | --- |
| Producer | Exactly one component decides it; consumers never re-derive it (ARCH "Missing facts (R3–R5)") |
| Timing | Computed in the final Binding pass (tables) or answered by a view after it (views); never read during a pass |
| Identity | Definitions by `BindingSymbol`; syntax by `SourceRef`; Types by interned `BoundType`; substitutions by interned `Subst` |
| Mutation | After the final pass the producer's published state is immutable. A view may intern (as `TyCtxt.Apply` does) and fill its own memo; it writes no pass state, no diagnostic, no CallPlan |
| Lifecycle | Tables are refilled per pass reusing their arrays; view memos are discarded by `Binding.Invalidate` (one `TyCtxt.ResetDispatch()` beside `ResetRetentions()`, `Binding.cs:199`) |
| Cost | 0 B per warm rebuild for every fact (tables reuse arrays, memos survive passes without edits) |
| Failure | A view returns a typed "limit" for `TyCtxt.Apply`'s depth bound (PD30); "no answer" for a closed accepted input is a broken Binding contract, recorded by the reader as `InternalInvariant_Kd` |
| D5 | New views leave the old Analysis and Emission readers unchanged (A5P §7); a legacy twin is deleted with its last legacy reader, recorded in DEC with that stage |

### 2.2 The facts

| Id | Fact (unit) | Producer and file | Representation | Post-Bind mutations or syntax reads it removes | R7 shape | First consumer |
| --- | --- | --- | --- | --- | --- | --- |
| F-a | `VTableDef` (F4) | Binding, end of `PrepareVirtualOverrides` (`Binding.cs:484`); `C/Binding/VTableDef.cs` | per struct: slot originals and implementations, base prefix | `VirtualGenerationPlan`'s path/span sort and per-generation slot rebuild (`VirtualGenerationPlan.cs:41-54, 357-386`); `ImplementationContext` → `FunctionItemContext` CallPlans and Item interning at generation (`Binding.FunctionItems.cs:20-21, 61-70`) | declaration table (PLAN10 §5.3) | K8 |
| F-b | `TyCtxt.Applies` (F4) | view over `VirtualApplicability` (`Binding.VirtualOverrides.cs:141-154`); `TyCtxt.Dispatch.cs` | `bool?`, memo | re-proof per generation (`VirtualGenerationPlan.cs:193`, `LlvmEmitter.cs:512`, `GenericStoragePlan.cs:127`) | TyCtxt view | K8 |
| F-c | Witness core (F5a) | Binding; new `C/Binding/Binding.RequirementWitness.cs` | `RequirementWitness` struct | per-call writes of the synthesized built-in targets (`Binding.Formatting.cs:137-138, 152-153`) | internal of R7's ContractSolver | `InstantiateRequirementCall`, `Witnesses` |
| F-d | `TyCtxt.Witness` (F5b) | view over F-c; `TyCtxt.Dispatch.cs` | `RequirementTarget`, memo | `InstantiateForwardedCall`'s CallPlan writes (`forwardedRequirements`, destinations; `Binding.Specializations.cs:146-164`), `InstantiateDefaultCall`'s `defaultCalls` (`:18-29`), `RequirementImplementation`'s refreshed CallPlans (`Binding.Formatting.cs:60-109`) | TyCtxt view | K6 |
| F-e | Property bridges published at Bind (F5p, conditional) | Binding (`Binding.PropertyWitnesses.cs:12-100`) and the HIR publisher | synthesized bodies with HIR columns | lazy bridge creation by the old Analysis (`OwnershipAnalysis.cs:1777` → `Binding.Specializations.cs:44-80`) and by generation | published bodies | MIR builder, K6 |
| F-f | `TyCtxt.Comparison` (F6) | view over F-c; `TyCtxt.Dispatch.cs` | `ComparisonNode` tree, memo | `comparisonPlans` refresh per read (`Binding.ComparisonPlans.cs:17-61`) and its `RequirementImplementation` leaves | TyCtxt view | F7 |
| F-g | Tuple-operator inputs (F7) | `CallPlan.TupleOperator`, set at selection (`Binding.Comparisons.cs:43`, `Binding.SelectedRequirements.cs:39`) + F-f | flag + tree | the dropped operator mode (`MirBuilder.Calls.cs:14-35`) | CallPlan field (R7 CallPlan pipeline) | F7 |
| F-h | `AdtDef.IsCLayout` (F1) | Binding, `ValidateLayoutFragments` (`Binding.Attributes.cs:405-482`) | `bool` on `AdtDef` | the attribute-chain walk in layout (`AggregateLayout.cs:257-265`) and its `Open` re-check | AdtDef column (PLAN10 §5.3) | P7b |
| F-i | Startup entry (F2a) | Binding, `CheckStartup` (`Binding.Startup.cs:44-125`) | `StartupResult.Symbol`, `.Module` | `LlvmEmitter.cs:259-264, 511, 517` FunctionKoto reads | symbol until R7 (DEC R3-6) | C0b2 |
| F-j | Import declaration (F2b) | Binding, `ValidateLibraryImports` | `LibraryImport.Decl` replaces `Function` (`BindingModel.cs:672`) | FunctionKoto-keyed import ABIs (`LlvmEmitter.cs:318-389`) | same | P8 |
| F-k | Dictionary storage callees (F2c) | `KimiLibrary` (`KimiLibrary.cs:37-48`) | `[Transitional("R8")]` symbol per row callee | the generation bridge `DictionaryStorageCall` (`LlvmEmitter.cs:35`, `Binding.DictionaryStorage.cs:11-23`); FunctionKoto lookups (`LlvmEmitter.cs:191-197`) | removed at R8 | C8e |
| F-l | Test-case declaration (P6) | Testing, `TestCatalog.Discover` (`TestCatalog.cs:14, 259`) | `TestCase` symbol | FunctionKoto→ABI map in `TestRuntime.Create` (`TestRuntime.cs:13-33`) | symbol | C10a |
| F-m | Runtime location text (P5) | Core; `SourceLocationTable` moved from `C/Emission/SourceLocationTable.cs` | text by `SourceRef` | two Koto-keyed tables (`BodyLowering.cs:30, 544`, `LlvmEmitter.cs:26, 168`) | Core, SourceRef | C1a |
| F-n | `TyCtxt.Instantiates` (F3) | view; new `TyCtxt.Instances.cs` | `bool` | `FunctionKoto.RequiresInstantiation`, a Syntax property that reads Binding (`FunctionKoto.cs:87-89`, ARCH "Syntax must not read Binding") | TyCtxt view | K1 |
| F-o | `ClosureContext` as `Subst` (K2, shared with chapter K) | Binding `CloseClosureTypes` via TyCtxt interning | `Subst?` on `BoundType` (`BindingModel.cs:517`) | the never-cleared `closureContexts` list and scan, appended post-Bind through `Apply` (`Binding.ClosureInstances.cs:9, 54-72`); `SubstOf(context)` per `SubstOfClosure` (`TyCtxt.cs:208-221`) | TyCtxt value (PLAN10 §5.3) | K3, K5 |
| F-p | C-layout obligation (K10b) | view; `TyCtxt.Instances.cs` | `bool` | generation-time rejection of inferred instantiations (`Binding.Attributes.cs:382-383`) | TyCtxt view | collector (K10b) |
| F-q | MIR builder facts (F8, F9, F10) | `MirBuilder`, `MirProgram` | `TestRecordValue` calls; `$abort` SourceInfo; `MirProgram.StaticInit` | a codegen rule reading a condition's rvalue (PD22); two copies of the staticEnsure→StaticInit lookup (`MirBuildTest.cs:39`, `MirEffects.cs:159-160`) | MIR (not Binding) | C10a, C3b, K7 |

---

## 3. Components and contracts

### 3.1 `VTableDef` (F4) — `C/Binding/VTableDef.cs`

- **Responsibility.** The dispatch shape of an open-struct family (SPEC 6.4): for each struct that has or inherits a virtual
  slot, its slots in order and the body it runs for each.
- **Inputs.** `virtualOverrides`/`overrideEntries` (`Binding.VirtualOverrides.cs:16-19`); the struct's merged `Members`, enumerated
  exactly as `PrepareStorage` enumerates Fields (`Binding.Storage.cs:226`).
- **Output.** `VTableDef.Of(BoundType type)` → `VTableDef?` (null: no slot); `Originals` (slot → original virtual's symbol),
  `Implementations` (slot → the body this struct runs: its own override, else the base's entry, else the original),
  `BaseCount`; `Slot(BindingSymbol original)` → index (searched in the original owner's own range).
- **Invariants.** Prefix: a struct's table starts with its base's table. Own slots follow the struct's member order, the same
  order `AdtDef.Fields` uses, so slot order and Field order follow one enumeration (SPEC 6.2.1; H that merged fragments are in
  logical order — F4's two-fragment test checks both orders together). A table is published only for a struct whose
  declaration and overrides all resolved (the DEC R4-M18 rule for published parts).
- **Prohibitions.** No path or span sort; no applicability (F-b decides it per instance); no ABI, receiver adaptation or
  `FunctionAbi`.
- **Storage.** Held by `AdtDef` (a `VTable` reference beside the Fields), so `BindingModel.cs` (held) is untouched; refilled once
  per pass with `AdtDef`'s array reuse (`AdtDef.cs:145-164`), stamped by `AdtDef.Version`. Cost O(total slots) per pass, 0 B warm.
- **Declaring instance of an entry.** The consumer walks `AdtDef.Base` from the closed Type to the layer whose symbol owns the
  implementation and forms `TyCtxt.Synthesized(implementation, layer, [])` (virtual functions have no own generic parameters,
  SPEC 6.4.1). No CallPlan and no FunctionItem Type is formed (deletes the generation use of `ImplementationContext`).

### 3.2 `TyCtxt.Applies(BindingSymbol original, BoundType declaring)` (F4) — `TyCtxt.Dispatch.cs`

- SPEC 6.4.3: whether the slot of `original` exists for the declaring instance. `true`/`false`; `null` only when undecided,
  which for a closed declaring Type is a broken contract (the collector records `InternalInvariant_Kd`).
- Over `Binding.VirtualApplicability`; memo `(original, declaring)` until an edit (the prover interns constraints, H that it
  writes no pass-visible state; F4 asserts diagnostics and the HIR version are unchanged after a run of views).
- `declaring` is the representative unerased instance (PD5: proofs read Origins), never the erased key.

### 3.3 The requirement-witness core (F5a) — `C/Binding/Binding.RequirementWitness.cs`

- **Responsibility.** The one rule mapping (requirement, closed-or-partly-abstract Self, Contract reference, premise scope) to
  what the requirement runs (SPEC 8.6.1, 8.11.4, 11.9.2, 21.3.1).
- **Signature.** `RequirementWitness ResolveRequirementWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract,
  BindingScope scope)`; `contract` is the instance's Contract reference, computed by the split-out matching half of
  `InstanceReference` (`Binding.RequirementInstances.cs:170-197`) from the bound reference Type.
- **Result kinds** (today's six cases, `Binding.RequirementInstances.cs:53-128`, `Binding.PropertyWitnesses.cs:109-152`,
  `Binding.FixedArrays.cs:84-102`, `Binding.IntegerPositions.cs:59-90`):

  | Kind | Today | Carries |
  | --- | --- | --- |
  | `Builtin` | `CompilerRequirementTarget` (comparison, formatting, arithmetic) | target symbol |
  | `Conformance` | `ConformanceWitness` | implementation, declaring instance, base path |
  | `Property` | `InstantiatePropertyRequirementCall` | bridge or accessor function, declaring instance, base path |
  | `FixedArrayEntry` | `InstantiateFixedArrayEntry` | Kimi group member (slots E and N filled by the shared helper) |
  | `IntegerPosition` | `InstantiateIntegerPosition` | `tryResolve` (slot I) |
  | `Refused` | `ProjectWitnessCall` returning false (`:157-160`) | a base-path witness whose requirement receiver is not `ref` (OCC-X, PLAN10 §5.5) |
  | `None` | null | — |

  A Self with an abstract part is not resolved (the caller keeps the requirement), as today (`:56-66`).
- **Callers.** `InstantiateRequirementCall` keeps only materialization (Origin translation through the extracted
  `TranslateWitnessOrigins`, receiver projection, CallPlan storage reuse); `Binding.Witnesses` calls it for its non-comparison
  branch; `TyCtxt.Witness` calls it. The refusal rule moves into the core, so the materializer and the view cannot disagree.
- **Built-in target symbols** (`builtinEquals`, `builtinCompare`, `builtinFormat`, the numeric targets) are refreshed once per
  pass at a fixed point after headers, so the core is read-only after Bind (today every call rewrites `Type` and
  `ReceiverIndex`, `Binding.Formatting.cs:137-138, 152-153`).
- **Behavior.** F5a is behavior-preserving (bp): each caller keeps its scope (`ConstraintScope(outer…)` for the materializer,
  `EffectPremises(definition)` for `Witnesses`).

### 3.4 `TyCtxt.Witness(BindingSymbol requirement, BindingSymbol? contract, Subst subst)` (F5b) — `TyCtxt.Dispatch.cs`

- **Input.** A MIR Requirement callee after composition: `requirement` = `MirCallee.Symbol`, `contract` = `MirCallee.Contract`
  (`MirBody.cs:108`, set at `MirBuilder.Calls.cs:121`), `subst` closed. `subst.Self` is the conforming Type; for a bound
  Contract reference, `subst.Declaring` holds it (`TyCtxt.SubstOf`, `TyCtxt.cs:201-204, 277-278`), closed by `Compose`.
- **Output.** `RequirementTarget(TargetKind Kind, BindingSymbol? Target, Subst? Subst, int BaseDepth)`:

  | Kind | Meaning | Reader's action |
  | --- | --- | --- |
  | `Body` | a function with a MIR body: conformance witness, property bridge or accessor, fixed-array or integer-position member; `Subst` = its declaring instance, the call's own Type and length arguments (positional, as `InstantiateRequirementCall` passes them), translated Origins | Fn edge through `TyCtxt.Implementation(Target, Subst, 0)` (specialization stays `Implementation`'s, SPEC 8.13.3) |
  | `Builtin` | a compiler function (comparison, format, arithmetic); `Subst` binds Self | built-in shim item; C8f reads F-f, C9b the format rows |
  | `Refused` | OCC-X | located `Unsupported_Kd` at the requesting call (K6) |
  | `Limit` | `TyCtxt.Apply` null (G128) | R4-M51's code at the requesting call (PD30) |

  `BaseDepth` is the number of base steps from Self to the witness's declaring Type, so codegen can project the receiver
  (chapter C decides how); 0 otherwise.
- **Memo** `(requirement, contract, subst)` until an edit; 0 B warm.
- **Contract.** For a closed accepted Self the result is never `None` (Binding verified the conformance); `Body` targets have MIR
  bodies (F5p ensures it for bridges); the twin test (F5b) compares every result with `InstantiateForwardedCall` over every
  requirement call in the generic library and milestone bodies that K4's collector instantiates.

### 3.5 `TyCtxt.Comparison(BindingSymbol requirement, BoundType self, bool operators)` (F6) — `TyCtxt.Dispatch.cs`

- `requirement` is `Equatable.equals` or `Comparable.compare`; `operators` is the source operator semantics (IEEE floats,
  numeric ordering of floats) as opposed to the Contract mapping (NaN-reflexive `equals`). One `bool` keeps ARCH's parameter
  limit (the plan's `(self, equality, operators)` has two).
- **Output.** `ComparisonNode(BoundType Type, ComparisonKind Kind, BindingSymbol? Target, Subst? Subst, ComparisonNode[] Parts)`,
  or null for a limit (PD30):

  | Kind | When (Binding's existing predicates) | Payload |
  | --- | --- | --- |
  | `Composite` | `ComparisonTypes.IsComposite`: a Tuple (its elements) or a safe `ref` layer (its referent) | `Parts` |
  | `Builtin` | `ComparisonTypes.IsBuiltin(type, contract)`, or `operators` and numeric (`Binding.ComparisonPlans.cs:48`) | the Type |
  | `Witness` | a closed leaf: F-c's result in a premise-free scope (a closed proposition needs no assumption; H that no closed conditional conformance reads premises) | `Target`, declaring instance |
  | `Requirement` | a leaf with an abstract part: its Contract mapping in every instance (SPEC 13.4) | `Target` = `requirement`, `Subst` binds Self |

- **Memo** `(requirement, self, operators)` until an edit; nodes are allocated once per distinct key; 0 B warm.
- **Readers.** F7 (operator mode, source tuple comparisons), C8f (Contract mode, the built-in comparison shim for a Witness
  `Builtin` target, and the Dictionary key-equality row that `DictionaryComparison` serves today). A4 may switch
  `Binding.Witnesses`' comparison branch to it (ask A-11, §4); until then that branch keeps the private `ComparisonPlan`.
- **Legacy twin.** `ComparisonPlan` and `BoundComparison` stay untouched for their legacy readers (D5) and are deleted with them:
  the CallPlan overload at H5 (readers `Binding.EffectBounds.cs:1029`, `GenericStoragePlan.Comparisons.cs:26`, the latter gone at
  G7), the private overload when A4 migrates (A-11) or at H5. DEC records the twin with these removal stages.

### 3.6 Tuple-operator expansion inputs (PD15, F7)

- The builder expands a comparison whose `CallPlan.Of(node).TupleOperator` is set (the published, syntax-selected flag,
  `CallPlan.cs:117-118`; it is set exactly for a built-in operator on a Tuple, `Binding.Comparisons.cs:43`), whether its plan is
  the resolved built-in call (closed) or a requirement call (dependent Self), by `TyCtxt.Comparison(requirement, self,
  operators: true)`: `Composite` → `Field`/`Deref` projections of the shared views the operands' inspection Loans already form
  (SPEC 13.4 "Inspection Loans"); `Builtin` → `BinaryOp`; `Witness` → a Fn callee; `Requirement` → a Requirement callee with no
  Contract reference (R4-M22's convention: a closed Self with a user witness is a Fn callee). The lexicographic chain and the
  unordered rule are the builder's (F7 owns the shape; A1–A5 see it, so an R4 reviewer signs off, as the plan says).
- Nothing else is needed: no `ComparisonPlan(CallPlan)`, no new callee mode, no `CompilerFunctionKind` (PD21).

### 3.7 `TyCtxt.Instances.cs`: `Instantiates` (F3) and the C-layout obligation (K10b)

- `bool Instantiates(BindingSymbol definition)`: whether a MIR body keyed by `definition` is checked symbolically and generated
  only per closed `Subst` (SPEC 8.15.1, IMPL 21.3.1): true when the definition is not a specialization and it or any enclosing
  declaration declares a Type, Semantics or length parameter (SPEC 6.1.3). Origin parameters do not count: the instance key
  erases Origins (SPEC 8.13.3, 22.4.4), so an Origin-only generic body is one root.
  - Refines `RequiresInstantiation`, which inspects only the function, its direct owner's `Schema` and an anonymous function's
    enclosing function (`FunctionKoto.cs:87-89`): a function in a group nested in a generic struct and a named function nested
    in a generic function are roots there (H that the language admits a parameter reference in either; F3's tests cover both and
    every `MirBodyKind`'s key symbol: Fn, Test, Closure, Getter/Setter executing functions, StaticInit Property, DefaultTemplate).
  - F3 re-points both callers (`Binding.ClosureInstances.cs:38`, a one-line adv hold; `GenericStoragePlan.cs:68`, a D5 mechanical
    follow-up this chapter names) and deletes the Parsing property, removing a Syntax→Binding dependency.
- `bool CLayoutObligation(BoundType instance)` (K10b): SPEC 8.15.3 / IMPL 21.1.2–21.1.3: a closed instance of a C-layout struct
  (`AdtDef.IsCLayout`) one of whose stored parts has zero stride. It shares `Binding.HasZeroSizedCField(type)` with
  `CheckCLayoutInstance` (`Binding.Attributes.cs:384-403`, `Binding.Pointers.cs:11-58`), so Binding owns the rule once; the
  collector reports Binding's existing Language code at the by-value use.

### 3.8 `AdtDef.IsCLayout` (F1)

- Written for every struct in `ValidateLayoutFragments`: `true` exactly where it adds the struct to `cLayouts`
  (`Binding.Attributes.cs:472-475`: declared `#Layout("C")`, fragments valid, not open, derived or empty), else `false`, so each
  pass refills it. `AdtDef` is created later in the pass (`Binding.Storage.cs:207`, `Binding.cs:476-479`), so the write uses the
  same `Adt ??= new()`.
- Binding's pre-storage queries keep `HasCLayout` (`Binding.Attributes.cs:34-44`): they run before the fact exists (header
  binding). Two answers to one question exist only inside Binding, in two phases; recorded in F1's commit.

### 3.9 Symbol columns (F2a, F2b, F2c, P6)

- **Rule (one DEC entry, F2a).** A Backend root or extern names its definition as `MirDefKey` does. A syntax column is replaced
  where every reader can derive the Koto from the symbol; otherwise the symbol column is added and the syntax column goes with
  its last syntax reader (recorded with that stage).
- **F2a.** `StartupResult` gains `BindingSymbol? Symbol` and `ushort Module`: the startup body's key parts (explicit `main`:
  `(main, 0)`; implicit: `(null, root module)`, `MirBuilder.cs:116-128`); none for a Library or a test build. `Function` stays
  for its legacy and public readers and goes at G7 if none remains (H). The root module is probably always ordinal 0 (H); the
  column keeps the contract explicit at 2 bytes.
- **F2b.** `LibraryImport(BindingSymbol Decl, string Library, string Symbol, string Kind)` replaces `FunctionKoto Function`;
  the legacy readers use `Decl.Declaration` (they read `CodeContext.Kotonoha`, `EmissionArtifacts.cs:150`, `LlvmEmitter.cs:325`).
  Held file (`BindingModel.cs`): adv.
- **F2c.** `[Transitional("R8")] KimiLibrary.DictionaryStorage(DictionaryStorageFunction which) → BindingSymbol` over the
  existing handles (`KimiLibrary.cs:37-48`): the nine callees of the legacy Dictionary rows (`initialize`, `appendSlot`,
  `clearLinks`, `requireAbsent`, `reserve`, `append`, `shrinkToFit`, generic `findEntry`, `clearEntries`). No
  `KimiDeclarationId` or catalog row (PLAN10 §5.8 shrinks that list at R8; `KimiLibraryCatalog.cs` is held). R8 deletes it with
  the rows.
- **P6** (in progress) follows the rule; `TestCatalog`'s site ids are already `SourceRef`-keyed (`TestCatalog.cs:61`).

### 3.10 Runtime location text (P5) — `C/Core/SourceLocationTable.cs`

- `Compilation.Locations.Text(SourceRef site)` → SPEC 22.5.1's `path:line:column` (project-relative logical path, `\\` for a
  backslash, `\u{HEX}` for control and non-ASCII), the existing algorithm (`SourceLocationTable.cs:21-67`), resolved through
  `Compilation.SyntaxOf` (`Compilation.Pipeline.cs:139-140`). One table per compilation replaces two.
- Memo keyed `(SourceDocument, offset)` as today, cleared when the project directory or the program version changes, so retained
  memory is bounded by the current program and a warm rebuild allocates nothing.
- A site without source provenance is a broken contract at its reader. Which site an Abort reports (invoking user expression,
  caller-location forwarding) is chapter C's; this component only renders a `SourceRef`.
- `TestCatalog`'s file/line/column (`TestCatalog.cs:256-265`) is a different contract (SPEC 20.6 test manifest, no escaping) and
  stays separate.

### 3.11 `ClosureContext` as `Subst` (K2, schema part)

- `BoundType.ClosureContext: Subst?` = the substitution of the instance that closed the Closure (target, declaring Type, Types,
  lengths, Origins, inputs), interned, so type interning keeps reference identity (`TyCtxt.cs:132, 159`) and
  `MirTypes.Agree` (`MirTypes.cs:84`) is unchanged. The old CallPlan's `ReturnType` drops out: it is `Apply` of the Closure's
  result under the `Subst` (H: deterministic).
- `SubstOfClosure` becomes a field read; `ClosureSignature` uses `TyCtxt.Apply`. The legacy readers get the
  `[Transitional("R6-gen")]` `Binding.ClosureCall(Subst)` bridge (plan K2). Review and units stay chapter K's.

### 3.12 Builder and program facts (F8, F9, F10)

PD22's `TestRecordValue` (F8), `$abort`'s SourceInfo (F9, SPEC 22.5.1 bullet 6) and `MirProgram.StaticInit(body, operand)` (F10)
are MIR facts in W1/W2, as the plan states; no Binding fact is added for them.

### 3.13 ARCH rows (written by the first implementing unit)

| Row | Content |
| --- | --- |
| New "Dispatch facts" | `VTableDef`, `TyCtxt.Applies`, `TyCtxt.Witness`, `TyCtxt.Comparison`, over `Binding.ResolveRequirementWitness`; `Binding/VTableDef.cs`, `Binding/TyCtxt.Dispatch.cs`, `Binding/Binding.RequirementWitness.cs` (F4) |
| "Stored shape" | `AdtDef.IsCLayout`; `VTableDef` reached through `AdtDef` (F1, F4) |
| "Types and substitutions" | `ClosureContext` is a `Subst` (K2); `TyCtxt.Instantiates`, `TyCtxt.CLayoutObligation` in `TyCtxt.Instances.cs` (F3) |
| "Effect facts" | `Witnesses` shares the core (F5a; R4's row text otherwise unchanged) |
| New "Runtime locations" | `SourceLocationTable` in Core (P5); dependency note: the Backend reads it (OQ4) |
| "Calls" | `CallPlan.TupleOperator` is a builder fact (F7) |
| Dependency note | the Backend reads `TestCase`'s symbol column (P6, OQ4) |

---

## 4. Coexistence with R4's facts (A5P §7)

One table serves both streams (A5P §7 "One table for both consumers"). S5 records this chapter's rows in DEC as the extension of
that table; the A5 plan text is not edited.

| A5P §7 row | Producer | Readers | This chapter |
| --- | --- | --- | --- |
| C1 requirements, premises | R4 (step 1; `TyCtxt.Retains`, R4-M51) | A5 | not read by R5 |
| C2 Closure contract | R4 (A5-F7) | A5; R5 erasure shims (C5b) | read as published; no R5 producer. If C5b needs it before A5-F7 lands, C5b reads the MIR Closure Type and `Cast(Erasure)` only (H) |
| C8 selected Callable signature | R4 (step 1, R4-M41) | A5; R5 item adapters | read as published |
| C5, M39, E4/E5, U8p | R4 | A5, A6 | none |
| `VTableDef` | R5 (F4) | K8, C6c | F-a |
| Override applicability | R5 (F4) | K8 | F-b |
| Copy and Sealed | existing (`TyCtxt.Copy`, `Sealed`, R4-M23) | A4, A5, R5 Layout and drops | unchanged; R5 calls them on closed Types |
| Specialization by Subst | existing (`TyCtxt.Implementation`, R4-M23) | A4, K4 | unchanged; the Fn-edge selector after `Witness` |
| Comparison and formatting by Type | R5 (F6, F5b) | F7, C8f, C9b; A4 via A-11 | F-f, F-d |
| Destruction calls | existing (`AdtDef.DestructorSymbol` + `TyCtxt.Synthesized`, as A4 uses, `MirEffects.Solve.cs:427-429`) | A4, K7, C4a | no new fact |
| `Witnesses` (R4-M28) | R4's contract, re-implemented over F-c (bp) | A4 | shared core; `TyCtxt.Effects.cs` untouched |

Rules: a row R4 produces is never re-derived by R5; a fact R5 produces that would improve an A4/A5 result is offered as an ask,
never applied to R4's files. New asks:

| Ask | Content | Needed by |
| --- | --- | --- |
| A-11 (optional) | A4's `Binding.Witnesses` comparison branch reads `TyCtxt.Comparison`: composites with an abstract part then contribute their leaves' atoms instead of a Limit (R4-M28 Open "composites with an abstract part a Limit"); a ledger row change R4 classifies | removal of the private `ComparisonPlan` |
| A-12 | An R4 reviewer on F7 (the plan's F7 row already asks) and on F5p (bridges add MIR bodies that A1–A5 analyze) | F7, F5p |

---

## 5. Positions on the planning decisions

| PD | Position | Detail and cost |
| --- | --- | --- |
| PD5 | Agree | The representative unerased `Subst` is the input of `Applies`, `Witness` and `Comparison`; `ClosureContext` as `Subst` removes the scan (F-o). Erasure recurses into `ClosureContext` (K3) |
| PD15 | **Refine** | F7's inputs are `CallPlan.TupleOperator` and `TyCtxt.Comparison(…, operators: true)`, not "the operator-mode plan of `ComparisonPlan(CallPlan)`". Reason: `ComparisonPlan` resolves a leaf through `RequirementImplementation` in the requirement declaration's scope (`Binding.ComparisonPlans.cs:50`, `Binding.Formatting.cs:68-74`), so a tuple with an abstract leaf (`(T, f64) < (T, f64)` under `T is Comparable`) has no plan (H); and its leaves are CallPlans, which the Backend may not read. Cost: F6 moves before F7 and joins its review; gain: `ComparisonPlan(CallPlan)` leaves at H5 instead of R7 (H11's "stay until R7" list loses it) |
| PD16 | Agree, widened | The core covers all six cases and the OCC-X refusal, and `RequirementImplementation` is a third resolver with the same cause (comparison leaves, formatting callbacks). F6 and C9b read the core through the views, so `RequirementImplementation` keeps only legacy readers and goes at R6 (EffectSummary H5, GenericStoragePlan G7, `TryResolveFormattingCallback` H11). Added contract: witness targets have MIR bodies (F5p) |
| PD17 | Agree | No Binding declaration-key fact; descriptor identity is the erased Type's ordinal (chapter C/K). `ObjectGenerationPlan.WriteKey` goes at G7 |
| PD20 | Agree, narrowed | Held files touched: `BindingModel.cs` (F2b, K2), `Binding.ClosureInstances.cs` (F3 one line, K2), `Binding.RequirementInstances.cs` (F5a), `TyCtxt.cs` and `HoverBuilder.cs` (K2). Not touched: `TyCtxt.Effects.cs`, `TyCtxt.Retention.cs`, `KimiLibraryCatalog.cs` (F2c uses handles; F3, F4, F5b, F6, K10b use new partials). So F3's, F5b's and K10b's "adv" become "no" except F3's one line |
| PD21 | Agree | No new `IntrinsicId`, `CompilerFunctionKind` or `BindingFailure`; K10b reuses Binding's C-layout code; F7 avoids two kinds |
| PD22 | Agree | F8 produces the values; which values are shown is implementation-defined (SPEC 17.6.3). For a tuple-operator condition F8 records the two operands, not the expansion's leaves (H; F8's golden fixes it) |
| PD30 | Agree, made typed | `Witness` and `Comparison` return a distinguishable `Limit` (null node) for `Apply`'s depth bound, so the collector reports R4-M51's code and never confuses it with a broken contract |

**Departures from the plan's PDs and unit rows** (the plan is owner-adopted, OQ1): PD15's input (above); F6's signature (one
`bool`); F6 before F7 with D+R (S3) instead of G:library-2; F5p added; F2b replaces instead of adding; F3's rule broadened; the
hold column for F3, F5b, K10b.

**Departures from other owner-approved texts:**

1. PLAN10 §5.7 lists the property witness among Backend shims. This design publishes standard bridges as Binding bodies checked
   by A1–A5 (F5p), the mechanism Binding already uses (SPEC 11.9.2 bridge premises). Reason: one execution path, checked like any
   body; a shim would duplicate Field projection and skip the analyses. Cost: more MIR bodies (one per verified path and standard
   property witness). It sits beside PD3's departure in the OQ4 package.
2. PLAN (not owner policy, `docs/dev/PLAN.md:33`) puts `ClosureContext` as `Subst` at R7; A5P §9.1 (owner-approved) pulls it
   forward. This design follows A5P §9.1, as the plan does.

---

## 6. Unit mapping

| Unit | Change against the plan | Prereq | Review | Hold | Size (H) | DEC / ARCH |
| --- | --- | --- | --- | --- | --- | --- |
| F0 | as planned | W1 | R | W1 | ±130 | — |
| F1 | as planned; first consumers P7b and K10b | S5 | D+R (S3) | no | +8, t+15 | AdtDef column |
| F2a | adds `Module`; consumers C0b2, K11 | S5 | D+R (S3) | no | +10, t+10 | symbol-column rule |
| F2b | replaces `Function` by `Decl` | S5 | D+R (S3) | adv | +6/−4, t+10 | — |
| F2c | `[Transitional("R8")]` handle view, no catalog row | S5 | D+R (S3) | no | +20, t+10 | temporary structure |
| F3 | new `TyCtxt.Instances.cs`; enclosing-declaration rule; deletes `FunctionKoto.RequiresInstantiation` | S5 | D+R (S3) | adv (one line) | +20/−4, t+30 | Syntax→Binding read removed |
| F4 | `VTableDef` via `AdtDef`; `Applies` memo; `ResetDispatch` | S5 | G:object | no | +100, t+80 | component, ARCH row |
| F5p (new, conditional) | probe first (does a bridge have a MIR body?); if not, create standard bridges in the final pass and publish them | S5 | D+R (S3) + A-12 | no (check PLAN §3 for a HIR-publisher hold) | +40, t+40 | bridges published at Bind |
| F5a | the core with all kinds; built-in symbols fixed per pass; bp | S5 | R | adv | ±170 (net ≈ +30) | one resolver |
| F5b | `RequirementTarget` with `BaseDepth`, `Limit`; twin test | F5a, F5p, K4 | G:call | no | +70, t+100 | view |
| F6 | moved before F7; `(requirement, self, operators)`; abstract leaves | F5a | D+R (S3) | no | +50, t+50 | view, `ComparisonNode`, legacy twin removal stages |
| F7 | reads F6 | F0, F6 | D+R (S3) + R4 reviewer | W1 | +70, t+50 | ARCH "Calls" |
| F8 | as planned | F0 | R | W1 | +25, t+25 | — |
| F9 | as planned | F0 | R | W1 | +3, t+20 | — |
| F10 | as planned | W2 | R | W2 | +8/−6 | — |
| P5 | memo lifecycle; Core partial for the accessor | S5 | D+R (S3) | no | +80/−69, t+20 | component move, ARCH row |
| P6 | as implemented; follows the symbol-column rule | OQ2 | R | no | +20/−12 | — |
| K2 | schema as §3.11; units and review chapter K's | S5 | D+R (S2) | adv | +40/−40 | R5-C1 |
| K10b | view in `TyCtxt.Instances.cs`; shares `HasZeroSizedCField` | K10a, K0, F1 | D+R (S2) | no | +50, t+50 | — |

Order inside this chapter: S5 → {F1, F2a–c, F3, F4, F5a, P5} in parallel; F5a → F6; F5p after its probe; W1: F0 → F7 (after F6)
→ F8 → F9; K4 → F5b. F3 stays on the critical path (S5 → F3 → K1) and is small.

---

## 7. Alternatives considered

| Decision | Alternative | Why rejected |
| --- | --- | --- |
| Witness by Subst | (a) Inside the owning component: synthesize a CallPlan from the Subst and call `InstantiateForwardedCall` | Keeps the post-Bind CallPlan writes this chapter removes; a CallPlan is not a Backend input |
| | (b) Shared structure: precompute every witness at Bind into an `ImplTable` | Closed instances are known only after collection (OD4); R7's ContractSolver owns that table; unbounded over all Types |
| | (c) Widen `Witnesses` to return the codegen target too | Mixes an effect view (flattened leaves) with a dispatch answer (one target); PD16's reason |
| Comparison composition | (a) Map `BoundComparison` leaves through `SubstOf` (thinnest view) | Null for abstract leaves; inherits `RequirementImplementation`'s base-path refusal (`Function.BasePath: null`, `Binding.Formatting.cs:71`), a legacy gap for inherited witnesses (H) |
| | (b) Change `ComparisonPlan` itself to the new leaves | Edits frozen readers' results (EffectSummary, GenericStoragePlan) and A4's (`Witnesses`): D5 and R4 ownership |
| Operator mode (PD15) | Encode it in the callee (a third synthesized built-in symbol, or a `CompilerFunctionKind`) | A mode steering a shim through identity; PD21 |
| Property witnesses | Backend shim (PLAN10 §5.7) | §5 departure 1 |
| `VTableDef` storage | A field on `BindingSymbol` (`BindingModel.cs`, held) or a Binding dictionary | Held file; a dictionary is a second lookup path beside `AdtDef`, which already lives on the symbol |
| `VTableDef` per closed instance | Tables per instance at Bind | Instances are unknown at Bind; applicability is the only per-instance part (F-b) |
| Symbol columns | Keep Koto columns and let the Backend read `.BoundSymbol` | ARCH: the Backend reads no syntax |
| F2c | New `KimiDeclarationId` rows for the seven nongeneric callees | Grows a list PLAN10 §5.8 shrinks; held catalog; R8 deletes the rows anyway |
| `Instantiates` | Keep `FunctionKoto.RequiresInstantiation` | A Syntax property reading Binding, and possibly wrong for nested declarations (H) |
| View memo lifetime | Version-stamped per pass (as `ComparisonPlan`) | Every warm rebind bumps the storage version (`Binding.cs:330`), so it reallocates; until-edit is R4-M51's settled pattern |

---

## 8. Deletions and effect

| Deleted | Size | Stage |
| --- | ---: | --- |
| `FunctionKoto.RequiresInstantiation` (Syntax reading Binding) | 3 | F3 |
| `closureContexts` list and scan | ≈ 30 | K2 |
| Second `SourceLocationTable` instance; the Emission copy of the class | 69 (moved) | P5 |
| Per-call writes of built-in target symbols | ≈ 6 | F5a |
| `VirtualGenerationPlan` (sort, slots, re-proof) | 398 | G7 (plan) |
| `ImplementationContext`, `IsInapplicableVirtualBody`, `DictionaryStorageCall`'s generation use | ≈ 35 | G9 (plan) |
| `GenericStoragePlan.Comparisons`, `LlvmModuleWriter.BuiltinComparisons`, `BodyLowering.ContractComparisons` | 360 | G7–G8 (plan) |
| Koto columns of `StartupResult` and `TestCase` (if no reader remains) | ≈ 6 | G7 |
| `ComparisonPlan(CallPlan)` (now R6-check, was R7) | ≈ 5 | H5 |
| Private `ComparisonPlan`, `comparisonPlans`, `BoundComparison` | ≈ 80 | A-11 or H5 |
| `RequirementImplementation`, `requirementCalls`, `FindRequirementWitness` | ≈ 60 | H5/H11 (after `TryResolveFormattingCallback`) |
| `InstantiateForwardedCall`, `forwardedRequirements`, `InstantiateDefaultCall`, `defaultCalls` | ≈ 130 | H11 (plan) |
| `F2c` view | ≈ 20 | R8 |

- Size in R5: chapter F adds ≈ +700 compiler lines and removes ≈ −110 (net ≈ +590; the plan's "facts ≈ +550" plus F5p and F6's
  abstract leaves). Beyond the plan's R6 totals it moves ≈ −145 lines (ComparisonPlan, RequirementImplementation) from R7 to R6-check.
- Speed: every dispatch answer is a memo hit after its first instance (one `Dictionary` probe); no CallPlan is synthesized or
  re-set during collection or codegen; `VTableDef` costs O(slots) per pass and no sort. Warm rebuild: 0 B for every fact
  (asserted by F4's, F5b's and F6's `Purpose=Allocation` rows and by K1/K4's collector rows). Cold cost: one node or result
  per distinct key, bounded by the checked set.

---

## 9. Risks, open questions and owner items

| # | Item | Kind | Resolution |
| --- | --- | --- | --- |
| 1 | Standard property bridges are created after the HIR publish and are not under any module root, so they have no MIR body (`Binding.Hir.cs:35-51` walks from roots; `Binding.PropertyWitnesses.cs:43-45` only sets `Parent`) (H) | probe | F5p's first step; drop F5p if bodies exist |
| 2 | `ComparisonPlan(CallPlan)` is null for a tuple with an abstract leaf (H) | probe | F6's first test row; PD15's refinement rests on it |
| 3 | The merged `Members` of a fragmented struct follow SPEC 6.2.1's logical order (H); slot order and Field order share the enumeration either way | probe | F4 two-fragment test |
| 4 | Views over the prover (`Applies`, `Witness`) write no pass-visible state (capability results, diagnostics) (H) | test | F4/F5b assert diagnostics and the HIR version unchanged |
| 5 | A closed conditional conformance is premise-free, so `Comparison`'s and `Witness`' premise-free scope agrees with `Witnesses`' definition scope on closed Selves (H) | test | F5b twin test |
| 6 | `Subst.Declaring` of a requirement call holds the bound Contract reference only when `call.DeclaringType` is null (`TyCtxt.cs:204`) (H) | probe | F5b; if a requirement call has a declaring Type, the reference needs its own slot (a `Subst` change, full process) |
| 7 | `Instantiates` for nested declarations differs from `RequiresInstantiation` (H) | test | F3 rows |
| 8 | `CallPlan.Of(DictionaryLiteral, 0)` re-sets a cached CallPlan on each read (`CallPlan.cs:168-169`), a write inside a published accessor the MIR builder calls | open issue | PLAN issue for R4-M61's owner; not chapter F |
| 9 | `ComparisonPlan` rejects inherited (base-path) witnesses in tuple leaves while Binding accepts the comparison (H): legacy generation would fail where MIR codegen succeeds | deviation | OQ7 class (a) if a fixture shows it |
| 10 | A-11 declined: the private `ComparisonPlan` stays until H5 and A4 keeps the composite Limit | R4 choice | recorded either way |
| 11 | PLAN10 §5.7 departure (property witness not a Backend shim) | owner | OQ4 package, beside PD3 |
| 12 | Startup `Module` may be constant 0 (H) | probe | F2a; drop the column only if the owner of startup confirms |

Review questions for the design review: whether the "Dispatch facts" row should absorb `Witnesses` and `Implementation` from
"Effect facts" now (R4's row) or at R6-check; whether `ComparisonKind` and `TargetKind` (local enums, not ARCH vocabularies)
need DEC entries of their own or one shared entry; whether F5p should precede K6 on the critical path or only F5b's twin test.
