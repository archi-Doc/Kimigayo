# R5 step 0, chapter F (S3): the R5 fact schema — package

Status: the decided design of chapter F for the S2–S4 design review, round 2, and the owner's one look (OQ4, DECISIONS R5-M1). It
synthesizes design 1 ([`deep-dives/q6-dispatch-facts.md`](deep-dives/q6-dispatch-facts.md), "D1") and design 2
([`F-design-2.md`](F-design-2.md), "D2") and applies the planning session's decisions in [`S2-S4-decisions.md`](S2-S4-decisions.md)
("X1"–"X25", "F 1"–"F 8" for its chapter-F rows). That file governs; this package states the result. A working record, not
authority. Date 2026-10-18.

Base: code at `origin/dev` `9dbf1393`, identical to `a8c2ec93` for every file cited. Paths: `C/` = `src/Kimi/Compiler/`, `B/` = the
new Backend. "Plan" = the R5 and R6 Implementation Plan; PD, OQ and unit names are its. (H) marks a claim not checked by running
code. "Probe" = a throwaway xUnit probe run on a detached `origin/dev` worktree, not committed; its results are quoted in §2.

Already on dev and not re-planned: P6 (`TestCase.Symbol`), Q15 (`CallPlan.Evaluated`), Q6/Q6b (StructuralCompletion and
MatchCoverage in Binding). Hold rule: DECISIONS R5-M7.

---

## 0. The design in one screen

1. **One schema rule** (§4.1). Each fact has one producer.
   - Tables are filled in the final Binding pass; views answer after it.
   - Definitions are named by `BindingSymbol`, syntax by `SourceRef`, never by a Koto.
   - View memos live until an edit (R4-M51), so a warm rebuild allocates 0 B.
   - A view writes no diagnostic, CallPlan, HIR or synthesized Koto.
2. **One witness core** (F5a). The bind-time materializer, A4's `Witnesses` and `TyCtxt.Witness` all call it. The abstract-Self
   forwarding rule stays in the materializer, and `Witnesses` keeps its current results (X24).
3. **`TyCtxt.Witness(requirement, contract, subst)`** (F5b) returns kind Body, Builtin, Storage, Refused or Limit, with no
   receiver depth. Codegen projects nothing, relying on `Layout.Of`'s base-prefix invariant (X1, X16).
4. **Standard property witnesses are Backend Storage shims** (X9, X14). The view's Storage answer carries the implementing Field,
   the conforming Self and Binding's `PropertyWitnessKind`. K6 keys a `MonoKind` property-witness item with them, and C3c writes
   the shim. F5p is not adopted.
5. **Tuple operators (PD15, refined).** F7 reads `CallPlan.TupleOperator` and `TyCtxt.Comparison(contract, self, operators)`,
   because `ComparisonPlan(CallPlan)` is null for every tuple with an abstract leaf.
   - F6 comes before F7, and the two form review group G:compare.
   - `ComparisonPlan(CallPlan)` is deleted at H11.
   - The legacy comparison composition stays as a recorded twin until A-12 lands, or until R7 (F 2, F 5, F 7, X25).
6. **Symbol columns.**
   - `StartupResult.Symbol` only, with no module ordinal (F 4); `Function` is removed at G7.
   - `LibraryImport.Decl` replaces `LibraryImport.Function`.
   - The Dictionary-storage callees get a `[Transitional("R8")]` symbol view.
7. **`VTableDef`** is stored on `AdtDef`, needs no sort and follows member order. **`TyCtxt.Applies`** is memoized.
8. **`TyCtxt.Instantiates`** (F3) lives in `Binding/TyCtxt.Instances.cs`, the file K3 extends with `Erase` (X8). It replaces
   `FunctionKoto.RequiresInstantiation`, a Syntax property that reads Binding (F 8).

---

## 1. Required meaning

| Rule | Governs | Facts |
| --- | --- | --- |
| SPEC 6.4.1–6.4.3; IMPL 21.1.3 | Static slot selection, most-derived implementation, an original's conditional premise per derived Type; tables implementation-defined, module-local, deterministic | `VTableDef`, `Applies` |
| SPEC 6.2.1 | Logical declaration order across fragments (UTF-8 byte order of logical names, then position) | slot order = member order |
| SPEC 8.6.1, 8.11.4, 8.13.3, 8.15, 11.9.2 | A closed requirement call runs the witness its Contract reference selects; a closed key selects its specialization; an abstract Self stays a requirement call; a standard property witness is a bridge under its conformance path's premises | witness core, `Witness` |
| SPEC 13.4, 13.4.1, 12.4.3 | Tuple operators compare elementwise and lexicographically; an unordered float pair makes the four ordering operators false; a `T` operand uses `T`'s Contract mapping in every instance; `Equatable.equals` on floats is NaN-reflexive | `Comparison`, F7 |
| SPEC 6.1 ("an inner declaration is generic whenever an enclosing parameter exists, even an unused one"), 8.15.1; IMPL 21.3.1; SPEC 8.13.3, 3.3.1 | Generic bodies are checked once and generated per closed instantiation; Origins are not part of identity | `Instantiates` |
| IMPL 21.1.2; SPEC 6.7.1 | C layout keeps written order; open, derived and empty structs reject it | `AdtDef.IsCLayout` |
| SPEC 22.4.1–22.4.2, 22.6 | Startup selection, `#LibraryImport`, C ABI | symbol columns |
| SPEC 22.5.1 | One location text per Abort site; `$abort` reports the `$abort` expression | P5, F9 |
| SPEC 17.6.3 | A failed verification shows values from the original evaluation | F8 |

---

## 2. Facts settled in the code

| # | Claim | Verdict | Evidence |
| --- | --- | --- | --- |
| 1 | `ComparisonPlan(CallPlan)` is null whenever a tuple has an abstract leaf | **True** | Probe: `(T, f64) < (T, f64)` and `==` under `T is Comparable/Equatable` give null. Closed `(i32, f64)`, `(Key, f64)` and `(i32, (f64, Key))` give full trees. A leaf resolves through `RequirementImplementation` (`Binding.ComparisonPlans.cs:48-50`). Its `ResolveConformance` returns Unknown for a `Parameter` kind (`Binding.ConditionalConformances.cs:326`) and runs in the library requirement's scope (`Binding.Formatting.cs:69`) |
| 2 | The Backend cannot read CallPlan leaves | **True for the Backend; it does not decide F7** | The Backend reads MIR, declaration tables and `TyCtxt` (ARCH:30, :36). `Mir/Build` may read Plans (ARCH:28). So this rule decides C8f; fact 1 decides F7 |
| 3 | The MIR callee of a tuple comparison names the requirement | **No: it names the synthesized built-in** | Probe: closed and abstract tuple comparisons have `Kind = Requirement` and target `compare`/`BuiltinCompare` (`Binding.Formatting.cs:117-140`, `Binding.Calls.cs:883-889`). So F6 is keyed by the Contract (F 5) |
| 4 | Standard property-witness bridges have no MIR body | **True** | Probe (`Counted`/`Own`): no body has an `IsPropertyWitness` declaration, also after the old Analysis created bridges and after a rebind. Bridges are synthesized `FunctionKoto`s outside the tree (`Binding.PropertyWitnesses.cs:12-77`); the HIR walk starts at module roots (`Binding.Hir.cs:44-48`); the builder takes published table nodes only (`MirBuilder.cs:108`) |
| 5 | `Subst.Declaring` holds a requirement call's bound Contract reference | **Holds on the corpus; the code allows otherwise** | Probe over `HirColumnsTest.Corpus` and the library: 1,587 requirement callees have an unbound reference; the 2 with a bound reference have `Subst.Declaring` reference-equal to it. The code sets `Declaring = call.DeclaringType ?? BoundContract(call)` (`TyCtxt.cs:201-204, 277-278`), so a lookup-supplied declaring Type would take the slot |
| 6 | Merged struct fragments follow SPEC 6.2.1 | **Yes, with one corner case Field order already has** | Fragments merge at parse by `AddLast` (`DeclarationContainerKoto.cs:177-186`) in source order. Sources are sorted by full path with `StringComparer.Ordinal`, on disk and in LSP (`CheckInputSource.cs:46-50`, `Lsp/DiskReader.cs:97-98`). UTF-16 ordinal and UTF-8 byte order differ only for names that mix supplementary characters with BMP characters ≥ U+E000. `AdtDef.Fields` reads the same members (`Binding.Storage.cs:226`) |
| 7 | `RequiresInstantiation` misses nested declarations | **Not for containers; open for nested named functions** | Container schemas append the parent's slots (`Binding.Origins.cs:324-341`); function schemas inherit none (`:283-286`). The property looks one owner up (`FunctionKoto.cs:87-89`), so a named function two levels inside a generic function may be misclassified (H). F3's reproducer settles it |
| 8 | The startup module is always ordinal 0 | **True** | `SourceModules[0]` is `Compilation.Kotonoha` (`Compilation.cs:163`, `Compilation.Modules.cs:82`); `CheckStartup` treats indices ≥ 1 as dependencies (`Binding.Startup.cs:57-77`) |
| 9 | `VTableDef` can live on `AdtDef` without editing `BindingModel.cs` | **True** | `BindingSymbol.Adt` exists (`BindingModel.cs:331`); `AdtDef.cs` is not on R5-M7's list |
| 10 | A base subobject is a layout prefix | **True** | "The complete base occupies a prefix" (`AggregateLayout.cs:382-383`); X1 makes it a stated invariant of `Layout.Of`, tested in P7b |
| 11 | Views write no state that a later pass or a diagnostic sees | **True once two writes are removed** | `VirtualApplicability` passes no failure node (`Binding.VirtualOverrides.cs:141-153`, `Binding.Constraints.cs:818`). Two writes remain in the core: (a) `CompilerRequirementTarget` rewrites the shared built-in symbols' `Type`/`ReceiverIndex` on every call (`Binding.Formatting.cs:137-138, 152-153`), which F5a moves to once per pass; (b) `PropertyWitnessFunction` creates bridges (`Binding.PropertyWitnesses.cs:22-77`), which the view never calls (X9). What is left is interning, proof caches and `StoredType`'s storage preparation, the class `TyCtxt.Apply` already belongs to |
| 12 | A4 needs bridge bodies | **No** | `EffectObligations` iterates function witnesses only (`Binding.EffectObligations.cs:26-37`). A1–A5 analyze definitions, and a generic body's property call stays a Requirement callee (probe: `count.Get`/`count.Set` with an abstract Self) |

---

## 3. Decision table

| Topic | D1 | D2 | Decided | Reason |
| --- | --- | --- | --- | --- |
| Common contract | implicit | §2.1 | D2's, without bridge creation | one rule; R4-M51 memo pattern |
| `VTableDef` | per struct symbol; no sort | on `AdtDef`; no sort | stored on `AdtDef`, keyed by struct symbol | facts 6, 9 |
| `Applies` | `(original, declaring) → bool?` | same + memo, representative | D2 | PD5; warm 0 B |
| Witness core | one core with `Witnesses` | same, refusal inside | D2, with the forwarding rule left in the materializer and `Witnesses` unchanged (X24) | fact 11a; one refusal rule |
| `Witness` signature | `(requirement, Subst)` | `(requirement, contract, subst)` + `BaseDepth` | `(requirement, contract, subst)`; Body, Builtin, Storage, Refused, Limit; no depth (X1, X16) | the Contract reference decides bound references and the fixed-array and integer cases (`Binding.RequirementInstances.cs:82-92, 170-197`); fact 10 |
| Property witnesses | inside the core | F5p: bridges with MIR bodies | Backend Storage shim; K6 item, C3c writer (X9, X14) | facts 4, 12; PLAN10 §5.7 |
| `Comparison` | `(self, equality)` over the private plan | `(requirement, self, operators)` over the core | `(contract, self, operators)` over the core (F 5) | facts 1, 3; ARCH:124 allows one `bool` |
| PD15 | builder expands by `ComparisonPlan(CallPlan)` | `TupleOperator` + F6 | D2; F6 before F7 in G:compare (F 7) | fact 1 |
| Legacy comparison twin | — | removed at A-11 or H5 | a recorded temporary structure until A-12, else R7 (F 2) | A4's `Witnesses` reads it permanently |
| `IsCLayout` | in `ValidateLayoutFragments` | same, for every struct | D2 | agreed |
| Startup column | symbol + module | `Symbol` + `Module` | `Symbol` only (F 4) | fact 8 |
| Import column | add | replace | replace | one column |
| Dictionary storage | lang-item view | `[Transitional("R8")]` view | D2 | held catalog; R8 deletes the rows |
| P5 | Core table by `SourceRef` | same + memo lifecycle | D2, with the stale-memo fix (§4.10) | agreed |
| `Instantiates` | — | enclosing-declaration rule; deletes the property | D2; file `TyCtxt.Instances.cs` (X8, F 8) | fact 7; ARCH:36 |
| Asks | — | A-11, A-12 | A-12 = D2's "A-11" (optional); D2's A-12 not used (X11) | — |

---

## 4. Components and contracts

### 4.1 Common contract of every chapter-F fact

| Clause | Rule |
| --- | --- |
| Producer | Exactly one component decides the fact; a consumer never re-derives it (ARCH "Missing facts (R3–R5)") |
| Timing | Tables are filled in the final Binding pass and views answer after it; nothing reads a fact during a pass |
| Identity | Definitions by `BindingSymbol` (DECISIONS R3-6), syntax by `SourceRef`, Types by interned `BoundType`, substitutions by interned `Subst` |
| Inputs | Proof-reading views (`Applies`, `Witness`, `Comparison`, and `Copy`/`Sealed` as before) take the representative unerased Type or Subst, never an erased value (PD5, K's prohibition) |
| Mutation | After the final pass, published state is immutable. A view may intern, prepare instance storage (as `Apply` does) and fill its own memo. It writes no pass state, diagnostic, CallPlan, HIR or synthesized Koto |
| Lifecycle | Tables are refilled per pass with reused arrays. View memos are discarded by `Binding.Invalidate` through `TyCtxt.ResetDispatch()`, beside `ResetRetentions()` (`Binding.cs:199`) |
| Cost | 0 B per warm rebuild; cold cost is one entry per distinct key, bounded by the checked set |
| Failure | `Apply`'s depth bound is a typed Limit (PD30). No answer for a closed, accepted input is a broken contract, which the reader records as `InternalInvariant_Kd` |
| D5 | Old Analysis and Emission readers stay unchanged. A legacy twin goes with its last reader, at a stage recorded in DECISIONS |

### 4.2 `VTableDef` (F4) — `C/Binding/VTableDef.cs`

- **Responsibility.** The dispatch shape of a struct family (SPEC 6.4): for each struct that has or inherits a virtual slot, its
  slots in order and the function each runs.
- **Inputs.** `virtualOverrides` and `overrideEntries` (`Binding.VirtualOverrides.cs:16-19`); the struct's merged `Members`, in the
  order `PrepareStorage` uses (`Binding.Storage.cs:226`).
- **Outputs.**
  - `VTableDef.Of(BindingSymbol structure)` gives the table, or null when the struct has no slot.
  - `Originals[slot]`.
  - `Implementations[slot]`: the struct's own override, else the base's entry, else the original.
  - `BaseCount`.
  - `Slot(BindingSymbol original)`, searched in the original owner's own range.
- **Invariants.**
  - A struct's table starts with its base's table.
  - Own slots follow member order (fact 6).
  - A table exists only for a struct whose declaration and overrides resolved (R4-M18).
  - Filled at the end of `PrepareVirtualOverrides` (`Binding.cs:484`); stored on `AdtDef`; refilled per pass.
- **Prohibitions.** No path or span sort, applicability, ABI, receiver adaptation or CallPlan.
- **Consumer rule** (K8, C6c).
  - Slot i of a closed `T` runs `Implementations[i]` in the first layer on `T`'s `AdtDef.Base` chain that owns it, under
    `TyCtxt.Synthesized(impl, layer, [])`. Virtual functions have no own generic parameters (`Binding.VirtualDeclarations.cs:30-33`).
  - A Virtual callee names the original, so the call loads `Slot(original)`; it has no collector target (X5).

### 4.3 `TyCtxt.Applies(BindingSymbol original, BoundType declaring)` (F4) — `C/Binding/TyCtxt.Dispatch.cs`

- SPEC 6.4.3: whether `original`'s slot exists for the declaring instance, which is the layer of the representative that declares
  the original.
- Answered from `VirtualApplicability` (`Binding.VirtualOverrides.cs:141-153`): Proven gives true, Refuted gives false. Anything
  else for a closed declaring Type is a broken contract.
- Memo `(original, declaring)` until an edit.
- False makes the entry null. The body stays a checked-only definition (R4-M28) and is never Generated.

### 4.4 Witness core (F5a) — `C/Binding/Binding.RequirementWitness.cs`

- **Responsibility.** The one rule from a requirement, a Self (any non-abstract Self; the materializer forwards abstract-part Selfs
  before calling the core), a Contract reference and a premise scope to what the requirement runs (SPEC 8.6.1, 8.11.4, 11.9.2; IMPL 21.3.1).
- **Signature.** `ResolveRequirementWitness(BindingSymbol requirement, BoundType self, BindingSymbol contract, BindingScope scope)`.
  `contract` comes from the extracted matching half of `InstanceReference` (`Binding.RequirementInstances.cs:170-197`).
- **Result kinds.** These are today's cases (`Binding.RequirementInstances.cs:68-123`, `Binding.PropertyWitnesses.cs:109-150`,
  `Binding.FixedArrays.cs:84-101`, `Binding.IntegerPositions.cs`):
  - Builtin (`CompilerRequirementTarget`).
  - Conformance (the witness, its declaring instance, its base path).
  - Property: an accessor-call witness's accessor, or a standard witness's Field and `PropertyWitnessKind`.
  - FixedArrayEntry.
  - IntegerPosition.
  - Refused: a base-path witness whose requirement receiver is not `ref` (`:157-160`, OCC-X).
  - None.
- **Callers** (X24).
  - `InstantiateRequirementCall` keeps the abstract-Self forwarding rule (`:56-66`, SPEC 8.15) and materialization: Origin
    translation, receiver projection and CallPlan storage. It also creates the standard bridge function, for legacy readers only.
  - `Binding.Witnesses` keeps its contract (R4-M28) and its results. Builtin becomes `(target, null)`; Conformance becomes
    `(witness, declaring)`; Property, FixedArrayEntry, IntegerPosition, Refused and None become `false`, as today. Refused cannot
    occur for its inputs anyway, since their requirements take `ref/Self` (`:11-12`).
  - `TyCtxt.Witness` (§4.5).
  - Scopes stay per caller: `ConstraintScope(outer)`, `EffectPremises(definition)` and the library root respectively.
- **Read-only after Bind.** The built-in target symbols get `Type` and `ReceiverIndex` once per pass, after headers (fact 11a).
- **Checks.** bp (diagnostic and semantic snapshots; legacy `.ll` byte-identical). The comparison, format, iterator and
  fixed-array families. `MirEffectsTest`, including `AnOpenSelfRunsItsWitness` (X24).

### 4.5 `TyCtxt.Witness(BindingSymbol requirement, BindingSymbol? contract, Subst subst)` (F5b) — `TyCtxt.Dispatch.cs`

- **Input** (X16).
  - At a MIR Requirement callee: `MirCallee.Symbol`, `MirCallee.Contract` (`MirBuilder.Calls.cs:121`) and the composed Subst, which
    is closed. Its `Self` is the representative conforming Type; its `Declaring` holds a bound reference (fact 5).
  - At a compiler-function row that calls a user witness (K's rows 2 and 6): the row's requirement and its Contract, with a Subst from F6's
    Self-bearing `Synthesized` factory (§4.6).
- **Output.** `RequirementTarget(Kind, Target, Subst, Access)`. `Access` is Binding's existing `PropertyWitnessKind`: `AccessorCall`
  for every kind except Storage, and Copy, Borrow or Set for Storage. Readers act as follows (K6):

  | Kind | Target, Subst | K6 action |
  | --- | --- | --- |
  | Body | A function with a MIR body: a conformance witness (inherited ones included), an accessor-call witness's executing accessor (Getter/Setter key), a fixed-array or integer-position member. Subst is its instance: declaring instance, own Types and lengths per case, translated Origins | Body item through `TyCtxt.Implementation(Target, Subst, 0)` and `MirProgram.Callee` |
  | Builtin | A compiler function (comparison, format, arithmetic); Subst binds Self | Builtin shim item; its leaves come from F6 |
  | Storage | A standard property witness. Target is the implementing stored Field (it may sit in a base layer, which `AdtDef.FindField` finds from Self); `Subst.Self` is the conforming Self; `Access` is Copy, Borrow or Set | `MonoKind` property-witness item keyed by (Field, erased Self, witness kind), exact `MonoKey` K's (X14); C3c writes it; Set adds a `DropGlue` target for the Field's Type |
  | Refused | — | OCC-X capability row (`Unsupported_Kd` at the requesting call) |
  | Limit | — | storage-nesting capability row (PD30, R4-M51's code) |

- **Contract.**
  - A closed, accepted Self never gets None.
  - The receiver's base projection is a no-op under `Layout.Of`'s base-prefix invariant (X1), so no depth is returned and
    codegen projects nothing.
  - For Storage, C3c's ABI comes from the key: Binding accepts a storage witness only with a `ref/Self` getter or `uniq/Self`
    setter, a result `F` or `ref/F`, and a Set input `F` (`Binding.PropertyMatching.cs:102-117`). K6 asserts that the requirement's
    instance signature (`TyCtxt.Signature(requirement, subst)`) agrees and records `InternalInvariant_Kd` otherwise.
  - Memo `(requirement, contract, subst)` until an edit.
- **Twin test.** Over every requirement call K4a's collector instantiates in the library and milestone bodies:
  - the view's target equals `InstantiateForwardedCall`'s;
  - `Subst.Declaring` equals the instantiated bound reference wherever `contract` is bound (fact 5). If not, the reference needs
    its own Subst slot, a full-process change.

### 4.6 `TyCtxt.Comparison(BindingSymbol contract, BoundType self, bool operators)` (F6) — `TyCtxt.Dispatch.cs`

- **Inputs.** `contract` is Equatable or Comparable: the scope owner of the call's target symbol, or of the shim's compiler
  function. `operators` selects source operator semantics (IEEE floats) instead of the Contract mapping (NaN-reflexive `equals`).
- **Output.** `ComparisonNode(Type, Kind, Target, Subst, Parts)`, or null for a Limit:

  | Kind | When (Binding's predicates) | Payload |
  | --- | --- | --- |
  | Composite | `ComparisonTypes.IsComposite` (a Tuple or a safe `ref` layer) | `Parts` |
  | Builtin | `ComparisonTypes.IsBuiltin(type, contract)`, or `operators` and numeric (`Binding.ComparisonPlans.cs:48`) | the Type |
  | Witness | a closed leaf: the core's Conformance result in the library-root scope (H: no closed conditional conformance needs premises) | `Target`, declaring instance as `Subst` |
  | Requirement | a leaf with an abstract part: its Contract mapping in every instance (SPEC 13.4) | `Target` = the Contract's requirement, `Subst` binding Self |

- **Factory.** F6 creates the Self-bearing `Synthesized` factory in `TyCtxt.cs` (adv hold). Requirement leaves and the calls of K's
  rows 2 and 6 share it; factories are not split by hold.
- **Memo.** `(contract, self, operators)` until an edit; one node per key.
- **Readers.**
  - F7, with `operators: true`.
  - C8f, with `operators: false`: the BuiltinEquals/BuiltinCompare shims and the Dictionary key-equality row.
  - A4, only through A-12.
- **Twin assertion.** Over the corpus, wherever the legacy `ComparisonPlan` is non-null, its leaves equal F6's: the same Types,
  kinds and witness targets. F6 is non-null where legacy is null only for abstract leaves and inherited witnesses.

### 4.7 The legacy comparison twin (F 2; a temporary structure)

| Member | Readers and removal |
| --- | --- |
| `ComparisonPlan(CallPlan)` | `Binding.EffectBounds.cs:1029` (H5), `GenericStoragePlan.Comparisons.cs:26` (G7); the overload itself goes at H11 |
| `DictionaryComparison` | `Binding.EffectBounds.cs:282` (H5); deleted at H11 with the overload |
| `RequirementImplementation`, `requirementCalls` | the twin's leaves; `TryResolveFormattingCallback` (H11) |
| Private `ComparisonPlan`, `BoundComparison`, `comparisonPlans` | A4's `Witnesses` (`Binding.RequirementInstances.cs:17-19`), until A-12 lands, else R7 |

While the twin lives, A4 and codegen resolve comparison leaves with different resolvers. This suspends A5P §7's one-producer rule
for comparison leaves (X25). Where legacy is non-null the two agree (§4.6). Elsewhere A4 records a Limit where codegen is exact:
a loss of precision, never wrong code.

### 4.8 Tuple-operator expansion (F7, PD15 refined)

- **Input.** A comparison whose `CallPlan.Of(node).TupleOperator` is set (`CallPlan.cs:121-123`; set for a built-in operator on a
  Tuple at `Binding.Comparisons.cs:43` and `Binding.SelectedRequirements.cs:39`), closed or dependent.
- **Expansion.** The builder reads `TyCtxt.Comparison(contract, call.ConformingType, operators: true)` and writes:
  - Composite → projections of the operands' shared views;
  - Builtin → `BinaryOp` (IEEE), or the string call the plan's F7 row names;
  - Witness → a Fn callee (R4-M22);
  - Requirement → a Requirement callee formed as `MirBuilder.Calls.cs:121` forms one;
  - then the lexicographic chain with early exit and the unordered rule.
- **Null answer.** A null `Comparison` is one located `Unsupported_Kd` with an Opaque value (`MirBuilder.Unsupported`, PD30's
  cause).
- **Goldens.** `tests/mir` goldens pin the expansions of four cases, whose required values follow from SPEC 13.4:
  - `(nan, 1.0) < (nan, 2.0)` is false;
  - `(-0.0, 1) < (0.0, 2)` is true;
  - `(1.0, nan) > (1.0, 2.0)` is false;
  - the generic `(T, f64) == (T, f64)` on `(nan, 1.0)` with `T = f64` is true: the `T` leaf uses the Contract's NaN-reflexive
    `equals`.
- **Values.** The goldens fix the shape that yields these values. The values themselves are asserted natively by C8f's fixtures
  under sel (plan C8f), since F7 lands before MIR codegen.
- **Ownership and review.** F7 owns the shape. A1–A5 see it, so an R4 reviewer signs off. F7 builds on F6's pushed branch inside
  G:compare; neither merges before the group review. No new callee mode, `CompilerFunctionKind` or `IntrinsicId` (PD21).
- **After F7.** `CallPlan.TupleOperator` is the only builder fact of this form (ARCH "Calls", until R7).

### 4.9 `AdtDef.IsCLayout` (F1)

- True exactly where `ValidateLayoutFragments` adds the struct to `cLayouts` (`Binding.Attributes.cs:472-475`); written for every
  struct in every pass.
- `HasCLayout` (`:33-44`) stays for header binding, which runs before the fact exists. The commit records that two answers exist,
  in two phases.
- First consumer P7b, which deletes the attribute walk and the `Open` re-check (`AggregateLayout.cs:257-265, 300`).
- **The zero-sized C-layout rule** (X18). `Layout.Of` is its only owner at any depth: it returns OK, Limit or
  CLayout(instance) and reads `IsCLayout`. K10a/K10b map CLayout to `InvalidCLayout_Kd` at the by-value use. Chapter F publishes
  no C-layout obligation view, and `TyCtxt.Instances.cs` holds none.
- **Binding's checks.**
  - Binding keeps rejecting written declarations: a C-layout struct's own zero-sized Field (`ValidateCLayoutFields`,
    `Binding.Attributes.cs:359-372`) and the fragment and shape rules of `ValidateLayoutFragments`.
  - Its eager report on written instantiations, `CheckCLayoutInstance` (`:384-403`, called at `:378` and
    `Binding.Types.cs:803`), goes at G2b, chapter K's unit. From then on `Layout.Of` reports by-value uses of written and inferred
    instances alike.

### 4.10 Symbol columns (F2a, F2b, F2c; P6 landed) and runtime location text (P5)

- **Rule** (one DECISIONS entry, F2a). A Backend root or extern names its definition the way `MirDefKey` does. A Koto column is
  replaced where every reader can derive the Koto from the symbol; otherwise the symbol is added, and the Koto column goes with its
  last syntax reader.
- **F2a.** `StartupResult.Symbol: BindingSymbol?` holds the explicit `main`. `Kind == Implicit` means the startup body is
  `MirDefKey(null, Fn, 0)` (fact 8). `StartupResult.Function` goes at **G7** with its last reader, `LlvmEmitter`
  (`LlvmEmitter.cs:265, 512`); its three test reads (`StartupBindingTest.cs:76, 286`, `TestExecutionAnalysisTest.cs:23`) move to
  `Symbol`/`Kind` in F2a.
- **F2b.** `LibraryImport(BindingSymbol Decl, string Library, string Symbol, string Kind)` replaces `FunctionKoto Function`
  (`BindingModel.cs:672`). Legacy readers use `(FunctionKoto)Decl.Declaration` (`EmissionArtifacts.cs:150`, `LlvmEmitter.cs:325-326`,
  three test lines).
- **F2c.** A `[Transitional("R8")]` symbol view of the nine `DictionaryStorage` callees the legacy rows call (`KimiLibrary.cs:37-48`,
  `LlvmEmitter.cs:191-197`, `Binding.DictionaryStorage.cs:13`). No catalog row and no `KimiDeclarationId`. The legacy row
  declares its callees through it (X4).
- **P5.** `C/Core/SourceLocationTable.cs`: `Text(SourceRef)` renders SPEC 22.5.1's `path:line:column` with today's algorithm
  (`C/Emission/SourceLocationTable.cs:21-67`), resolving through `Compilation.SyntaxOf`. One table per compilation.
  - **Stale-memo fix.** Today's memo, keyed (document, offset), is cleared only when the project directory changes
    (`SourceLocationTable.cs:12, 23-27`), so it keeps the texts of documents that edits replaced. P5 keeps the key and also clears
    the memo when the program version changes, so retained memory is bounded by the current program. A `SourceRef` is never a memo
    key, since ids renumber on edit (`SyntaxTable` remarks).
  - Which site an Abort reports is chapter C's. The test manifest's text (`TestCatalog.cs:256-265`) stays separate.

### 4.11 `TyCtxt.Instantiates(BindingSymbol definition)` (F3) — `C/Binding/TyCtxt.Instances.cs` (X8)

- **Rule.** True when the definition is not a specialization and it, or any enclosing declaration on its scope chain, has a Type,
  Semantics or length slot ("has", even an unused one: SPEC 6.1). Origin slots do not count; requirements have no bodies or
  defaults (SPEC 8.4.2).
- **Coverage.** Every `MirBodyKind` key symbol: Fn, Test, Closure (through its enclosing chain), the Getter/Setter executing
  functions, the StaticInit property and DefaultTemplate. `Erase`'s null rule (K3) calls it.
- **Changes.**
  - F3 creates the file.
  - It re-points `Binding.ClosureInstances.cs:38` (one held line, adv) and `GenericStoragePlan.cs:68`.
  - It deletes `FunctionKoto.RequiresInstantiation` (F 8).
- **Legacy classification.** F3 first builds fact 7's reproducer, a named function two levels inside a generic function.
  - If legacy generation changes for it, the change is classified as a D5 miscompile fix, with the reproducer as its regression.
  - Otherwise F3 shows the case absent and is bp.

### 4.12 Builder and program facts (F8, F9, F10)

As the plan says: `TestRecordValue` operands (F8, PD22), `$abort`'s SourceInfo (F9), and `MirProgram.StaticInit(body, operand)`
(F10, W2). No Binding fact.

---

## 5. Representations and what becomes unnecessary

| New or changed representation | Owner | R7 shape |
| --- | --- | --- |
| `VTableDef` (R2b-D3's deferred view) on `AdtDef` | Binding | declaration table |
| `RequirementTarget` (kind enum local; `Access` = existing `PropertyWitnessKind`), `ComparisonNode` (kind enum local), view memos, the Self-bearing `Synthesized` factory | Binding (`TyCtxt.Dispatch.cs`, `TyCtxt.cs`) | TyCtxt views |
| `Binding.ResolveRequirementWitness` | Binding | internal of R7's Contract solver |
| `AdtDef.IsCLayout`; `StartupResult.Symbol`; `LibraryImport.Decl` | Binding | declaration-table columns |
| `TyCtxt.Instantiates` in `TyCtxt.Instances.cs` | Binding | TyCtxt view |
| `SourceLocationTable` in Core | Core | `SourceRef` service |
| Temporary: the F2c view (R8); the legacy comparison twin (A-12 or R7) | Binding | removed |

| Deleted or made unnecessary | Stage |
| --- | --- |
| `FunctionKoto.RequiresInstantiation` | F3 |
| Per-call writes of the built-in target symbols | F5a |
| `LibraryImport.Function`; the Emission copy of `SourceLocationTable` and its second instance | F2b; P5 |
| `VirtualGenerationPlan` (sort, slot rebuild, re-proof; 398 lines); `StartupResult.Function` | G7 |
| `GenericStoragePlan.Comparisons`, `LlvmModuleWriter.BuiltinComparisons`, `BodyLowering.ContractComparisons` (≈ 360) | G7–G8 |
| `ImplementationContext`, `IsInapplicableVirtualBody`, `DictionaryStorageCall` readers | G9 |
| `ComparisonPlan(CallPlan)`, `DictionaryComparison`, `InstantiateForwardedCall`'s requirement path, `TryResolveFormattingCallback` | H11 |
| Private `ComparisonPlan`, `BoundComparison`, `comparisonPlans`, `RequirementImplementation` | A-12, else R7 |
| Post-Bind bridge creation (`CollectWitnesses`, forwarded calls) | with its legacy callers; MIR never creates bridges |
| The F2c view | R8 |

- **Size (H).** In R5 chapter F adds ≈ +500 compiler lines and removes ≈ −80, so ≈ +420 net, below the plan's facts total of
  ≈ +550. Tests add ≈ +540. Property-witness codegen (C3c) is chapter C's.
- **Speed.** Every answer after an item's first is one memo probe. No CallPlan is synthesized or re-set during collection or
  codegen. `VTableDef` costs O(slots) per pass, with no sort.
- **Allocation.** A warm rebuild allocates 0 B, asserted by `Purpose=Allocation` rows in F4, F5b and F6 and by K's collector rows.

---

## 6. Interfaces to chapters K and C

**Provided by F.** Each row names the consumer's action, so K's and C's rows can state the same contract.

| Fact | Consumer | Contract the consumer relies on |
| --- | --- | --- |
| `Instantiates` (F3) | K1 roots, K3's `Erase` null rule (X8) | §4.11's rule ("has a slot", SPEC 6.1); created in `TyCtxt.Instances.cs` before K3 (K3 needs F3) |
| `VTableDef`, `Applies` (F4) | K8 items; C6c slot loads (X5) | §4.2–4.3; `Applies` takes the representative |
| `Witness` (F5b) | K6 at K's rows 2, 3 and 6 (X16) | called with `MirCallee.Contract` (formatting rows pass their Contract); Body → Body item; Builtin → shim item; Storage → `MonoKind` property-witness item keyed (Field, erased Self, `Access`), written by C3c (X14); Refused → OCC-X row; Limit → storage-nesting row |
| `Comparison` (F6) | K's Builtin item targets; C8f; F7 | `(contract, self, operators)`; leaves Witness `(Target, Subst)` and Requirement `(requirement, Self-bearing Subst)` |
| Self-bearing `Synthesized` (F6) | K6 at K's rows 2 and 6 | the one factory for a requirement's Subst on a Self |
| `AdtDef.IsCLayout` (F1) | P7b's `Layout.Of`, the one owner of the zero-sized C-layout rule (X18), read by K10a/K10b | §4.9 |
| `StartupResult.Symbol` (F2a), `TestCase.Symbol` (P6) | C0b2 Entry, K11 roots, C10a | implicit startup = `MirDefKey(null, Fn, 0)` |
| `LibraryImport.Decl` (F2b) | P8 C-convention `Abi.Of` | §4.10 |
| Dictionary-storage symbols (F2c) | the legacy Dictionary rows' declared callees (X4), C8e | §4.10 |
| Location text (P5) | C's FunctionCodegen input (C1a, C2a, C3b) | §4.10 |
| F8, F9, F10 | C10a, C3b, K7 | as plan |

**Needed by F:**

- **From K.** Representative unerased Substs as inputs. Copy and Sealed run on representatives only: D1's erased-`T` remark is
  not adopted. K6 is F5b's first consumer, in G:call.
- **From C.** P7b states and tests `Layout.Of`'s base-prefix invariant (X1). C3c writes the Storage shim through FunctionCodegen's
  primitives, finding an inherited Field with `AdtDef.FindField`. C9b reads K6's target and never calls `Witness` (X15).
- **Chapter K's, noted here.** The closure bridge `Binding.ClosureCall` is memoized keyed (closure symbol, Subst), reconciled with
  the old dedup (X10, K2); chapter F has no closure-bridge text. The pure `SubstOfItem` that removes
  `FunctionItemContext`'s cache mutation is K5's.

---

## 7. Unit mapping (changes to the plan's rows)

| Unit | Change | Prereq | Review | Hold |
| --- | --- | --- | --- | --- |
| F0 | as planned | W1 | R | W1 |
| F1 | `false` written for other structs | S5 | D+R (S3) | no |
| F2a | `Symbol` only; test reads moved; `Function` removal at G7 stated | S5 | D+R (S3) | no |
| F2b | replaces `Function` by `Decl` (+6/−4, t+10) | S5 | D+R (S3) | adv (`BindingModel.cs`) |
| F2c | `[Transitional("R8")]` view, no catalog row | S5 | D+R (S3) | no |
| F3 | creates `TyCtxt.Instances.cs`; enclosing-declaration rule; reproducer first; deletes the Parsing property (+20/−4, t+30) | S5 | D+R (S3) | adv (one line, `Binding.ClosureInstances.cs`) |
| F4 | `VTableDef` on `AdtDef`; `Applies` memo; `ResetDispatch` | S5 | G:object | no |
| F5a | core; forwarding rule left in the materializer; `Witnesses` results unchanged; built-in symbols fixed per pass; `MirEffectsTest` added to its checks; bp (±170, net ≈ +30) | S5 | R | adv (`Binding.RequirementInstances.cs`) |
| F5b | `(requirement, contract, subst)`; Body/Builtin/Storage/Refused/Limit; twin test (+60, t+100) | F5a, K4a | G:call | no (new partial) |
| F6 | before F7; `(contract, self, operators)` over the core; Self-bearing factory; twin assertion (+60, t+60) | F5a | G:compare | adv (`TyCtxt.cs`) |
| F7 | reads F6; located `Unsupported_Kd` on null; the four goldens; built on F6's pushed branch (+70, t+60) | F0, F6 | G:compare + R4 reviewer | W1 |
| F8, F9, F10 | as planned | F0 / W2 | R | W1 / W2 |
| P5 | one table; stale-memo fix | S5 | D+R (S3) | no |
| C8f | F6 from G:compare (G:library-2 loses F6) | C8c, F6, F7 | G:library-2 | no |
| H11 | also deletes `ComparisonPlan(CallPlan)` and `DictionaryComparison`; `CallPlan.TupleOperator` and `InstantiateRequirementCall` stay until R7 | H10 | G:check-del | no |
| K6, C3c (chapters K, C) | K6 builds the property-witness item from Storage and asserts the key's signature agrees with the requirement's instance signature; C3c writes it, its ABI from the key (fixtures `Counted`/`Own`) (X14) | K6: K4a, F5b, F6, P1d; C3c: K6, P7b, C4a | K6: G:call; C3c: G:C-drop (X32) | — |

**Order.** After S5, F1, F2a–c, F3, F4, F5a and P5 run in parallel. F5a feeds F6. W1 opens after R4's E13/E14 MirBuilder units land
and R4 says so; in W1 the order is F11 (chapter C's validator unit, X30) → F0 → F7 (on F6's branch) → F8 → F9. F5b follows K4a;
F5b and F6 precede K6, so G:compare merges before G:call; C3c follows K6 and C4a in G:C-drop (X32). F3 stays on the critical path (S5 → F3 → K1), and K3 needs F3.

---

## 8. Departures from owner-approved texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| Plan PD15 (adopted, OQ1) | F7 reads `TyCtxt.Comparison` instead of `ComparisonPlan(CallPlan)`, and `ComparisonPlan(CallPlan)` is deleted at H11 instead of R7 (X25) | fact 1: no plan exists for tuples with an abstract leaf, which F7 must also expand |
| Plan F6 row | Signature `(contract, self, operators)` instead of `(self, equality, operators)`; F6 before F7; group G:compare instead of G:library-2; adv hold for the factory (X25) | ARCH:124 allows one `bool`; F7 is F6's first consumer (R4-M2a) |
| A5P §7 (one producer per fact) | The legacy comparison twin suspends the rule for comparison leaves until A-12, else R7 (X25) | A4's `Witnesses` reads the twin; the difference costs precision, never wrong code (§4.7) |
| Plan F2a, F2b, F5b, H11 rows | No module ordinal; F2b replaces the column; F5b needs no hold; H11 also deletes `ComparisonPlan(CallPlan)` and `DictionaryComparison` | fact 8; one column; the core lives in F5a's new file; their readers are gone by H5 |
| PLAN10 §5.7, DECISIONS R4-M28 | None. The property-witness shim stays (X9). `Witnesses`' contract and results are unchanged (X24) | — |

---

## 9. Owner items and asks to R4

- **Vocabulary and codes.** None. No `IntrinsicId`, `CompilerFunctionKind`, `BindingFailure`, MIR vocabulary or diagnostic code
  (PD21). The kind enums are local, not ARCH vocabularies. `MonoKind`'s property-witness member is chapter K's (X14).
- **Representations.** §5's first table is approved with the package. Its temporary structures are the F2c view (R8) and the
  legacy comparison composition (A-12 or R7).
- **ARCH rows**, written by the first implementing units:
  - a new canonical row "Dispatch facts" (`VTableDef`, `Applies`, `Witness`, `Comparison` over `Binding.ResolveRequirementWitness`);
  - "Effect facts" notes that `Witnesses` shares the core;
  - "Stored shape" adds `IsCLayout` and `VTableDef`;
  - "Types and substitutions" adds `Instantiates` in `TyCtxt.Instances.cs`;
  - "Calls" names `TupleOperator` as a builder fact;
  - a new "Runtime locations" row (Core) carries OQ4's dependency notes;
  - the two temporary structures are added to the list.
- **A-12 (optional; D2's "A-11", X11).** A4's `Witnesses` comparison branch reads `TyCtxt.Comparison(operators: false)`.
  - A closed leaf with an inherited witness becomes exact instead of a Limit (`RequirementImplementation` refuses base paths,
    `Binding.Formatting.cs:71`).
  - A leaf with an abstract part may become X2 atoms or stay a Limit, as R4 chooses (R4-M28's open item).
  - It removes the twin's last permanent reader. D2's own A-12 is not used.

---

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| The witness view and the materializer diverge, and native code calls the wrong body | F5a first (bp, `MirEffectsTest`); F5b's twin test |
| Bound-reference requirement calls are rare (2 corpus sites) | F5b asserts fact 5 per call; failure means a full-process Subst change |
| Tuple comparisons are wrong under sel until F7 lands | F7 early in W1; the four goldens; C8f's native fixtures |
| Post-Bind proofs mutate interning or storage | Same class as `Apply`; F4, F5b and F6 assert Binding issues and the HIR version unchanged after views run; warm 0 B |
| The library-root scope for closed leaves disagrees with `Witnesses`' scope (H) | Twin test and twin assertion, with an Origin-premised conditional conformance in the set |
| A-12 declined: two comparison resolvers until R7 | §4.7's recorded twin; F6's leaf-equality assertion bounds the difference |
| Legacy generation refuses an inherited-witness tuple leaf that MIR accepts (H) | An OQ7 (a) row if a fixture shows it |
| The Storage shim's signature, which comes from the key, differs from the requirement's instance signature | K6 asserts the agreement (`InternalInvariant_Kd`); fixtures `Counted`/`Own` |
| Nested named functions change classification | F3's reproducer; D5 miscompile classification or bp |
| UTF-16 versus UTF-8 source order (fact 6) | An existing open issue for Field order as much as for slots; F4 records it in PLAN |
| Hold contention on `Binding.RequirementInstances.cs` (F5a) and `TyCtxt.cs` (F6) | One adv hold line each; both units are small |
