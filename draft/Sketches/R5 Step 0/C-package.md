# R5 step 0, chapter C (S4): package

Status: synthesis of the two designs of chapter C for the S2–S4 design review and the owner's one look (OQ4, R5-M1). Design 1 =
`deep-dives/q2-codegen-shape.md` ("D1"); design 2 = `C-design-2.md` ("D2"). Round-1 redo: the text states the design as decided in
`S2-S4-decisions.md` (X1–X25 and the adopted non-blocking items), which governs where anything here differs. Working record, not
authority.

Base: `claude/r5-package` at `e3b9a735`. Citations are at its code, which is `a8c2ec93`'s. The load-bearing MIR facts were
re-checked at `origin/dev` `5ae8ebb7` (A5 step 1 MIR lane merged). There they hold with shifted lines: `MirInit.cs:61, 286`,
`MirDrops.cs:294`, `MirBuilder.Places.cs:172-175`, `MirTypes.Destruction.cs:78-131`. A-9's `MayNeedDrop(type, seen)` has landed.

Paths as in the plan: `B/` = `src/Kimi/Compiler/Backend/` (new), `E/` = `src/Kimi/Compiler/Emission/`, `M/` = `src/Kimi/Compiler/Mir/`,
`Bd/` = `src/Kimi/Compiler/Binding/`. "Verified" = read for this package; (H) = estimate or unverified inference.

---

## 0. Summary

- **Design B** (PD1): `FunctionCodegen` writes LLVM text from validated MIR, one Generated item at a time; no physical IR. Design A is
  judged on paper in §5 with checkable evidence (R5-M3).
- **Promote, don't copy** (PD2): a promoted member takes generator-neutral inputs only. Layout and Abi are shared with legacy, so bp
  holds.
  - `Layout.Of` returns OK, Limit or CLayout(instance) (X18) and states the base-prefix invariant (X1).
  - IMPL 21.1.3's one-word `Option<ref/T>` stays a STATUS limit until after G8 (PLAN U8; P7a records it).
- **The collector resolves; codegen reads.**
  - The view comes from `View(item)`, selected with `MirCases.Select` (X13).
  - Types come from `TypeOf(item, t)` (X2), callees from per-site targets, and glue children from `CheckedSet.Find`.
  - Intrinsics carry a target only where the row table's whitelist column says so (X15).
  - Virtual calls read F4's `VTableDef` (X5). Legacy rows declare their own callees and needs (X4). There is no receiver projection
    (X1).
- **PD12's gate reads A2's per-body completeness fact** (X12). Ask A-2 is required and lands in R4's unit 1c. So C4a, the drop
  chain and X1–X3 wait for A5 step 1.
- **DropGlue is recursive per erased Type** (X3): `(ptr, ptr location, i64)`, targets in SPEC 16.3.2 order, a closure-environment row
  (X23). Open drop rows go through the same per-level function.
- **New units.**
  - C3c writes the property-witness Storage shim (X14).
  - F11 adds G131's Never-temp validator rule in window W1, before C0b1.
  - C7 splits into C7a (the reason row, which changes every legacy `.ll`) and C7b. G9 splits into G9a and G9b.
- **Files.** The selector lands in K1 (X7). C0a creates `Core/Compilation.Generation.cs`, which holds the generation entry and the
  gate (X17).
- **PD23 reversed**: `transferBytes` keeps `memcpy`. The departure from PLAN10 §5.8's `Storage.moveBytes` is listed (X25).
- **Successor of `GenerationFailed_Kd`**: `InternalInvariant_Kd` for contract failures; publication failures are an owner item
  (recommended: new Input code `ArtifactPublicationFailed_Kd`; §3.10 lists the reuse options).
- **Owner facts.**
  - P42's generic static keys need `MirStatic` to carry the container's Type arguments.
  - The static fold keeps no slot state, so P42 must narrow it or restore the state.

---

## 1. Required meaning

D2 §1 is adopted unchanged (IMPL 21.1–21.4, SPEC 14.10.3, 16.3.2–16.3.3, 22.4.3–22.4.4, 22.5.1–22.5.2, 23.1.1, 23.2.2, ARCH
dependency rules, R5-M8). These rows are added:

| Rule | Consequence here |
| --- | --- |
| SPEC 16.4 (`docs/spec/16-scope-exit-and-destruction.md:211`): remaining captures are destroyed in reverse initialization order | DropGlue's closure-environment row (X23) |
| SPEC 22.4.3 example (`docs/spec/22-core-execution-and-foreign-functions.md:403`): the cycle is "reported at `Values`" | The static Abort location is the access's qualifier: the `staticEnsure` call's SourceInfo (H: that its span starts there; C7b tests it) |
| SPEC 22.4.3 shutdown (`:362`): an access during shutdown succeeds only if the slot finished initialization earlier | A folded static has no state to decide this. P42's fact (§3.7) |
| SPEC 23.1.1 (`docs/spec/23-compiler-services.md:10-19`): codes other than SPEC-named ones are implementation-defined; the category set is closed | A publication failure is Input; reusing an existing code is allowed (§3.10) |
| IMPL 21.1.3 nonnull Option (`docs/impl/21-layout-runtime-and-code-generation.md:65`) | Not laid out (`Bd/Binding.Attributes.cs:318`). STATUS records only the import ABI (`docs/STATUS.md:80`) |
| AGENTS: O2 run time and native allocations never regress | Decides PD23 (§2 row 24) and the fold's input set (§3.7) |

---

## 2. Decision table

The Chosen column names the governing decision where one exists.

| # | Topic | Design 1 | Design 2 | Chosen | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | Codegen shape | B | B | B | PLAN10 §5.7, ARCH target pipeline; §5 |
| 2 | Sharing | Promote, don't copy (G0–G7) | Same, plus generator-neutral inputs (§2.10) | D2 | The input rule keeps promoted kernels free of `EmissionModule`/`AggregateLayout`, so G8 can delete them |
| 3 | `LegacyRuntimeHelpers` scope | ≈970 module-level lines; `DictionaryIteration` not verbatim (§4) | Same, through P1a–P2b | Agree | Plan P1a–P2b follows D1 §4 |
| 4 | Facts for Backend legality | F1 location by `SourceRef`, F2 declaration key, F3 test column | P5, P6; no declaration key (PD17) | D2 | PD17 removed F2 |
| 5 | `FunctionCodegen` contract | §3.4 (5 invariants) | §2.2 (I1–I11) | D2, with rows 6–8 | Superset |
| 6 | Semantics view of an item | Codegen computes `MirCases.Views` | The collector supplies the view | **Collector's `View(item)`; codegen calls `MirCases.Select` (X13)** | One owner of the item's view |
| 7 | Callee lookup | "collector names" | `(Block, Target)` rows, binary search | **K's per-site targets `(Item, At, Runtime)` in `MirLocation` order, forward cursor; intrinsic targets by the whitelist column (X15); none for Virtual (X5) or `#LibraryImport`** | The producer owns the shape; one target per site |
| 8 | Instance Types | `TyCtxt.Apply(t, subst)` per instance | Collector-owned function | **`CheckedSet.TypeOf(item, t)`; codegen never composes `Erase(Apply(…))` (X2)** | One rule |
| 9 | Blocks | `bb<n>` | `b<index>` | D2 | Runtime-reached = `MirReach.Walk(body, 0, checks: false)` (`M/MirReach.cs:48`) |
| 10 | Frame | alloca per non-zero-sized local; bool `i8`; flags `i8` | Same; zero-sized address = one 16-aligned module symbol; pointer-passed argument used in place | D2 | IMPL 21.1.1 allows shared zero-sized addresses |
| 11 | Text sink | (requests) | Pooled buffer, two assembly phases | D2 | PD8: `EmitFixture` scans from the first `__kimi_f`/`__kimi_entry_body` definition to the end (`tests/xUnitTest/Tests/ScalarEmissionTest.cs:136-138`). Runtime helpers contain `select i1 %zero,` and the like (`E/LlvmModuleWriter.Dictionaries.cs:35`, `.Arrays.cs:32-38`, `.Comparisons.cs:27-39`), so helpers must precede functions, and requests are known only after codegen |
| 12 | Failure block | Shared kernels, today's `WriteArithmeticFailure` | Per check; P4a promotes the per-check writer | D2 | Verified per check (`E/LlvmModuleWriter.Scalars.cs:441-490`); the plan's "shared failure block" means the shared writer |
| 13 | O2 parity list | §3.3 | §3 | D2 + D1's `poison` row | §4 |
| 14 | ABI attributes on value calls | "reuse `AbiParameter.Attributes`" | Wherever a parameter list is written | **D2 (round-0 O8)** | Legacy omits them only on value calls (`E/LlvmModuleWriter.Closures.cs:265-290`; direct calls `E/LlvmModuleWriter.cs:641-642`). They state the callee's guarantee, which is true at every site. Strict parity would need a flag steering the writer. IR text is information (PD9) |
| 15 | Layout | Port `GetCore`, split; facts replace syntax reads | Shared `Layout.Of`, verbatim rules, tagged Option kept | D2 + D1's split + **OK/Limit/CLayout results (X18)**, base-prefix invariant (X1) | bp; one layout rule; one owner of the C-layout zero-sized rule at any depth |
| 16 | Abi | `Abi.Of` via `GetDefinition`; delete `Supports` | Pure core; C convention; no gate; `PreparedSlot` not ported | D2 | `PreparedSlot` serves only legacy default evaluators (`E/DefaultGenerationPlan.cs:148`, `E/BodyLowering.Defaults.cs:42`) |
| 17 | DropGlue | Shim of the instances topic | Per-Type table; drop-declaring struct = destructor only (PD18) | **Recursive per erased Type, targets in SPEC 16.3.2 order (X3); closure-environment row (X23)** | Legacy is recursive per aggregate (`E/LlvmModuleWriter.Aggregates.cs:224, 264, 279`); SPEC order is per level |
| 18 | Selector | PD4 in C0a | PD4 + identity mark in C0a | **K1, its first consumer (X7)**; C0a keeps the entry, gate, `kimi test` path, `-Codegen`, `summary.json` | R4-M2a |
| 19 | Generation entry | — | `Compilation.Generate(TextWriter)`, typed records, out-string adapters | D2, **in the new partial `Core/Compilation.Generation.cs` (X17)** | No out-string failure text; no fourth change of the held `Compilation.Pipeline.cs` |
| 20 | PD12 gate | A2 completeness required | `Incomplete(body)`, A-2 required | **A-2 required, R4 unit 1c (X12)** | §3.8 |
| 21 | Coverage rows | Rows per slice | Dispatch tables are the rows; `Uncovered`; no partial module | D2 + S1 UNOWNED 2 | §3.9 |
| 22 | Deviation form | — | `Expect(target, legacy, Deviation)` + one catalog | D2 | Each item declared once (R5-M6); plan wording in §8 |
| 23 | Statics | `staticEnsure` state machine | Four `i8` states, cycle at access, fold, C7a/C7b | D2 + per-slot cold `ensure`; the generic storage key is K7's capability row (X6) | §3.7 |
| 24 | `transferBytes` (PD23) | — | `memcpy`; correct the `MoveBytes` comment | D2 | The declaration says the ranges do not overlap (`src/Kimi/Library/StorageOperations.kimi:71-75`). The only producer is `Bd/KimiLibraryKinds.cs:208`. Both callers copy into a fresh allocation (`src/Kimi/Library/DictionaryStorage.kimi:103-107, 133-151`). The O2 constraint decides. PLAN10 §5.8's `moveBytes` is R8's (departure listed, X25) |
| 25 | `GenerationFailed_Kd` successor | `InternalInvariant_Kd` | + `ArtifactPublicationFailed_Kd` | **Owner item (round-0 O2)** | §3.10 |
| 26 | Debug information | — | Record only | D2 | PD24 |
| 27 | G9 | — | G9a, G9b | D2 | Two concerns |
| 28 | `WriteLineUtf8` (S1 UNOWNED 1) | — | — | C9a (26 rows) | ABI `E/WindowsLowering.Formatting.cs:60`, runtime `E/Utf8BufferRuntime.ll.in:25`, both moved by P1d |
| 29 | `EmitFixture` names | Keep | Keep | Keep (PD8) | `__kimi_f<N>`, `__kimi_entry_body`, `%zero<N>`/`%minusone<N>` from P4a's kernel |
| 30 | Requirement receiver | — | — | **None: `TyCtxt.Witness` returns `(Kind, Target, Subst)`; codegen projects nothing (X1)** | `Layout.Of`'s base-prefix invariant makes an inherited member's address the receiver's |
| 31 | Legacy rows' callees and needs | Collector roots them | — | **The row declares them; the collector reads them (X4)** | One owner per row |
| 32 | Property witnesses | — | — | **Backend Storage shim, unit C3c (X9, X14)** | §3.12 |
| 33 | Never temps | — | — | **F11: G131's structural validator rule before C0b1** | The validator accepts a Never-typed operand on every path; a runtime read would be lowered as uninitialized storage |

---

## 3. Components and contracts

### 3.1 Overview

| Component | Files | Lands in | Lifetime | Contract |
| --- | --- | --- | --- | --- |
| `LlvmModuleText`, `LlvmConstantPool` | `B/LlvmModuleText.cs`, `B/LlvmConstantPool.cs` | P0 | permanent | §3.4 |
| Runtime ABI table, abort reasons, ABI and layout records | `B/RuntimeAbi.cs`, `B/AbortReasons.cs`, `B/AbiRecords.cs` | P0b | permanent | §3.4 |
| `LegacyRuntimeHelpers` | `B/LegacyRuntimeHelpers*.cs` | P1a–P2b | `[Transitional("R8")]` | §3.4 |
| `ObjectRuntime` | `B/ObjectRuntime*.cs` | P3b | permanent (PLAN10 §5.8) | §3.4 |
| `LlvmScalarText` | `B/LlvmScalarText.cs` | P4a, P4b | permanent | §3.4 |
| `Layout`, `Abi` | `B/Layout.cs`, `B/Abi.cs` | P7a–P7c, P8 | permanent | §3.5 |
| `ModuleCodegen`, `FunctionCodegen`, intrinsic row table | `B/Codegen/ModuleCodegen.cs`, `FunctionCodegen.cs` (contract entry), `FunctionCodegen.<Concern>.cs`, `IntrinsicRows.cs` | C0b1+ | permanent | §3.2, §3.3 |
| `DropGlue`, `ObjectDescriptors` (with VTables), `Statics`, `Shims` (closure and item adapters, comparison, format, arithmetic, property-witness Storage) | `B/Codegen/*.cs` | C4a, C6a, C7b, C5b/C8f/C9b/C3c | permanent | §3.6, §3.7, §3.12; D2 §2.9 |
| Generation entry and gate | `Core/Compilation.Generation.cs` (contract entry; X17) | C0a; C4a and K11 change it | the refusal path until H5 | §3.8 |
| Deviation catalog, pending ledger | `tests/xUnitTest/Codegen*.cs`, `scripts/codegen-pending.ps1` | E10–E12 | G4 | D2 §2.13 |

The collector, `CheckedSet`, `MonoKind`, `Capability`, the selector (K1) and the Generated column are chapter K's (§6.1).

### 3.2 `FunctionCodegen`

D2 §2.2 is adopted, with these edits.

- **Responsibility.** It writes the LLVM definition of one Generated body item from its validated, analyzed MIR body. It provides the
  primitives every Backend mono item writes through: slot, load, store, address, call, switch, branch, check and abort. One ABI
  writer, one attribute writer and one location rule serve all functions.
- **Inputs.**
  1. The accepted program version: `MirProgram.Bodies` and `.Drops`, passed by the gate.
  2. From `CheckedSet` (K):
     - the item (kind, body ordinal, representative) and its targets `(Item, At, Runtime)` in `MirLocation` order;
     - `View(item)`, selected once with a codegen-owned `MirCases.Select(body, view)` (`M/MirCases.cs:43`);
     - `TypeOf(item, t)`;
     - `Find(MonoKey)`, for an Open drop row's child glue;
     - the Generated mark.
  3. `TyCtxt.Signature`; F4's `VTableDef` for virtual slots.
  4. `MirReach` runtime blocks.
  5. `Layout.Of` and `Abi.Of`.
  6. Location text by `SourceRef` (P5) and `KimiLibraryCatalog.RequiresCallerLocation` (`Bd/KimiLibraryCatalog.cs:265`).
  7. ModuleCodegen's request tables.
- **Outputs.** As D2:
  - definition text in the pooled buffer;
  - requests;
  - typed records: `Unsupported_Kd` per uncovered location under sel until X2, and `InternalInvariant_Kd` at a broken input
    contract, after which no module is published.
- **Invariants.** D2's I1–I11, with these edits:
  - **I1.** No checkout path in the text, so E4's byte comparison applies to MIR `.ll`.
  - **I6.** A runtime Drop with `Group == -1` in a body that A-2's fact marks complete is `InternalInvariant_Kd`. Open rows are
    written in SPEC 16.3.2 order through DropGlue's per-level function (§3.6).
  - **I7.** Every Type is `Layout.Of(TypeOf(item, t))`.
  - **I8.** A call reads the target at its `MirLocation` with a forward cursor. Fn, Requirement, Value, `Drop`, `Aggregate(Closure)`,
    `Const(FnItem)` and `Cast(Erasure)` sites carry targets. An intrinsic site carries one exactly when its row's whitelist column
    says so. Virtual calls load the slot by `VTableDef` (X5). `#LibraryImport` calls call the external symbol. A missing expected
    target is `InternalInvariant_Kd`.
- **Prohibitions.** As D2: no syntax, HIR, plans or Binding internals; no re-derived acceptance fact; no `Supports*`, out-string,
  LINQ, catch-all or member over 60 statements. It never runs on a refused program. It never composes a Subst or a Type, resolves a
  callee or calls `TyCtxt.Witness`.
- **Dispatch.**
  - One switch per closed MIR vocabulary.
  - One intrinsic row table (`B/Codegen/IntrinsicRows.cs`, one row per `IntrinsicId`: writer kind and the whitelist column)
    plus the Body-disposition `CompilerFunctionKind` rows (`Bd/KimiLibraryKinds.cs:183-219`).
  - The whitelist (X15): `ErasedWriter`, `StaticEnsure`, `MakeObj`, `MakeRc`, `MakeArc`, `Clone`, `TypeTest`, `DestroyValues`. K6–K8
    read the column; the first of C0b1, K6, K7, K8 to land creates the table (R4-M2a).
  - The default arm is `Uncovered`.

### 3.3 `ModuleCodegen`

D2 §2.3 is adopted:

- **Items.** It names items in Generated order (`__kimi_f<N>` for body items, kind prefixes for artificial items) and runs
  FunctionCodegen and the mono-item writers. It writes Entry or TestEntry around the Generated roots; the startup root comes from
  `StartupResult.Symbol` (F2a; no module column).
- **Assembly.** It writes the module through `LlvmModuleText` in legacy order (§2 row 11). Function text is buffered once: two
  phases over one buffer, never two passes over MIR.
- **Folded statics.** At a StaticInit item it asks `Statics.ConstantOf(body)`. When the body folds, it writes the constant global
  and no function, so a folded StaticInit body is never generated as a function. ModuleCodegen skips it through Statics' one rule;
  K's item and mark stay unchanged.
- **Limits.** It decides neither what is generated (K11) nor any order other than Generated order.

### 3.4 Promoted components (P0–P4b; bp each)

| Component | Responsibility | Inputs | Output | Invariant | Prohibition |
| --- | --- | --- | --- | --- | --- |
| `LlvmModuleText` | Header, footer `#0`, memory and overflow declarations, runtime template splice, external names, number formatting | writer, request flags | text | legacy `.ll` byte-identical while shared | no per-generator branch |
| Runtime ABI and reasons | Runtime `FunctionAbi` table; the closed SPEC 22.5.2 reason table | — | records | contiguous indices (`E/WindowsLowering.Abort.cs:53-80`) | LINQ (`.Any` at `:61` rewritten in P0b) |
| `LegacyRuntimeHelpers` | Array, Dictionary, formatting and string-comparison runtime text; one row per Body-disposition kind, which declares its library callees (F2c) and type-driven needs (X4) | element descriptor (`ValueLowering`, drop name, text flag, Option offsets) | helper text, ABIs, the row's callees and needs for the collector | a new kind adds a row; written once, in today's order | no `AggregateLayout`, `EmissionModule` or MIR type |
| `ObjectRuntime` | obj/rc/arc runtime, descriptors, creation | descriptor rows | text | release order per SHARED_OBJECT_RUNTIME | — |
| `LlvmScalarText` | Checked arithmetic, division, shift and conversion checks; the per-check failure block | type text, SSA names, labels, reason, location operands | text | `select i1` only in its failure block (PD8) | no opcode or MIR type |

### 3.5 `Layout.Of` and `Abi.Of`

**`Layout.Of(BoundType)`** (P7a–P7c):

- **Responsibility.** Size, alignment, stride and field offsets of a closed Type, and the C-layout zero-sized rule at any depth (X18).
- **Output.** One of three results:
  - `OK(ValueLowering)`;
  - `Limit(kind)`: size, count or depth;
  - `CLayout(instance)`: the C-layout instance, at any depth, with a zero-sized stored Field. Today that is the old pool's null at
    `E/AggregateLayout.cs:336-339`.
  K10a and K10b map `CLayout` to `InvalidCLayout_Kd` and `Limit` to the Resource record at the by-value use. Any other failure is
  `InternalInvariant_Kd`. The old pool maps both results to its existing failures (bp).
- **Rules.** Ported verbatim: scalars and handles; Array 3, Slice 2 and Dictionary 7 words (`:297`); C written order and Kimigayo
  alignment groups; `i32` enum tag; depth 64 (`:47`); 2^31−1 (`:171-176`).
- **Invariants.**
  - Determinism and memoization by interned Type, 0 B warm.
  - **The complete base occupies a prefix of the derived layout, tail padding included**, in both modes (`:382-383`; X1). P7b tests
    it.
- **Prohibitions.**
  - It reads no Origins; MIR callers pass `TypeOf` results. The old pool keeps its Origin guard (`:276`, `:483-484`) for bp.
  - No destruction: the old pool keeps its callbacks until G8.
  - No syntax read: `IsCLayout` (F1) replaces the attribute walk (`:255-265`), and the `Open` re-check (`:300`) is deleted.
  - No nonnull Option before U8.

**`Abi.Of(convention, flags, signature)`** (P8): D2 §2.6, `FunctionAbiPool`'s pure core plus the `#LibraryImport` C branch. It
returns no name (ModuleCodegen names). It has no `Supports` gate, and `PreparedSlot` is not ported.

### 3.6 `DropGlue` (C4a; C5a for closure environments; C6b for handles)

- **Responsibility.** One internal function per erased Type that `MirTypes.MayNeedDrop` admits. It destroys a value of that Type in
  SPEC 16.3.2–16.4 order.
- **ABI.** `void (ptr value, ptr location, i64 length)`, as legacy glue (`E/LlvmModuleWriter.Aggregates.cs:158`). The location reports
  failures inside a release.
- **Body, one level per Type.**
  - A drop-declaring struct calls only its destructor instance (PD18, R4-M6b).
  - Another struct destroys its Fields last to first, then its base.
  - Tuples and fixed arrays destroy elements in decreasing index (a fixed array in one loop over the element glue).
  - An enum switches on its tag and destroys the active Case's payloads in reverse.
  - A concrete closure environment destroys its remaining captures in reverse initialization order (SPEC 16.4; X23). An inline
    environment of 8 bytes or less (PD19) is reached through the environment word's address.
  - A string uses the runtime release. Array and Dictionary use the `LegacyRuntimeHelpers` row with the element glue's name.
  - obj/rc/arc release through `ObjectRuntime`: a Sealed payload calls its glue directly, any other runs the descriptor finalizer.
  - An erased function value calls its environment table's drop entry. InlineStorage has no glue. An abstract part is
    `InternalInvariant_Kd`.
- **Targets (K7, X3).** In SPEC 16.3.2 order: the glue of each direct part whose Type may need drop, or the destructor instance, or
  the Sealed payload's glue.
- **Per-level function.** The order of a level's parts is one function, `DropGlue.Parts`. C4c's Open rows use it: each direct part
  in SPEC order takes its child row's verdict if it has one, else the own bit with the part's glue from `Find`.
- **Invariant.** Exactly-once destruction, which C4s's matrix checks through E2's fail-closed audits.
- **Prohibition.** Never reuses `__kimi_drop_aggregate`, which also destroys the Fields after the destructor (`:192-203`).

### 3.7 `Statics` (C7a, C7b)

- **Slot.** Per `MirStatic.Symbol`: `@__kimi_static<N>`, Layout-aligned, and `@__kimi_static<N>.state`, an `i8`. States 0 Not started,
  1 Initializing, 2 Initialized and 3 Finished are named constants (SPEC 22.4.3, R5-M13(b)). Legacy has three
  (`E/LlvmModuleWriter.Statics.cs:23`).
- **Access.** The hot test is inline at each `staticEnsure`: `load i8`, `icmp eq 2`, `br`. The cold path calls a per-slot
  `__kimi_static_ensure<N>(location, length)`.
  - That function stores 1, calls the StaticInit target into the slot and stores 2, or Aborts with `KIMI_E_STATIC_CYCLE` at the
    passed location (state 1 only in R5).
  - Inside a `RequiresCallerLocation` body the location is the caller location.
  - P42 adds state 3's `KIMI_E_STATIC_SHUTDOWN`.
- **Fold.** `Statics.ConstantOf(body)`: a StaticInit whose runtime blocks hold only storage markers and `_0 = Use(Const)` becomes an
  `internal unnamed_addr constant`, with no state. ModuleCodegen applies it (§3.3).
  - C7b's test covers legacy's folded set (`src/Kimi/Compiler/StaticScalar.cs:38-58`: bool and signed integer literals).
  - A signed literal is one `HirOp.Literal` with a folded value (`Bd/Binding.Hir.cs:105-109, 272`). (H) A parenthesized literal
    builds the same way.
- **C7a.** The reason row `KIMI_E_STATIC_CYCLE` (18th row).
  - The runtime with the expanded table is in every module (`E/LlvmModuleWriter.cs:71, 733`; `E/WindowsRuntime.ll.in:119, 128`). So
    the row changes every legacy `.ll` and no native behavior: the declared change of plan §5.3 item 7.
  - Evidence: E4's report plus a throwaway hunk check (round-0 O7).
- **D2 rows (C7b).** Mutable, aggregate and drop-needing statics only (X6). They are coverage rows until X2, then capability rows. The
  generic storage key is K7's capability row (X6; X22 is the owner's).
- **Owner facts.**
  - `MirStatic(Symbol, Type, Mutable, RawHolding)` (`M/MirBody.cs:201`) is deduplicated by symbol (`:442-451`), and its Type is the
    declared `symbol.Type` (`M/Build/MirBuilder.Places.cs:158-161`). So SPEC 22.4.4's key is not representable, and P42 needs a MIR
    change. R5-M13(b)'s "no representation change" holds for the slot state only.
  - The fold keeps no slot state, so it cannot answer SPEC 22.4.3's shutdown access rule. P42 must narrow the fold or restore the
    state.
  - X22: the old path also accepts immutable scalars in a generic container's group, and it folds the literal ones:
    `StaticScalar.TryGet` has no generic-container test, unlike `IsDynamic` (`StaticScalar.cs:20-26`). That corrects R5-M13(b)'s
    premise.

### 3.8 Generation entry and gate (C0a, C4a; X7, X17)

- **Selector (K1).** `KIMI_CODEGEN` lands in K1. From K1 until C0b1, generation under sel writes no module and records one
  `Unsupported_Kd` at each output root's subject: the not-yet-written frame row.
  - K1 adds this with the selector.
  - C0a moves it into the entry.
  - C0b1 deletes it, and FunctionCodegen's rows take over.
- **Entry (C0a).** C0a creates `Core/Compilation.Generation.cs` with its contract entry. It moves `WriteIr`, `Validate` and
  `Generates` out of `Compilation.Pipeline.cs` (one adv line, C0a's only change there).
  - One `Compilation.Generate(TextWriter)` with typed records serves `EmissionArtifacts.Publish`, `Project.Emit` and `kimi test`.
    `kimi test` today publishes only a string (`src/Kimi/Unit/Command/TestCommand.cs:131-135`).
  - The out-string `WriteIr`/`Validate` adapters stay until G9b. C0a also adds `-Codegen` and `summary.json`.
  - C4a's gate step 2 and K11's mark change only this partial, which is R5-owned and needs no hold.
- **Gate, step 1 (C0a).** `MirRecords == 0`: construction and validator records and, from K4b, collector records.
- **Gate, step 2 (C4a).** For each Generated body item, read A2's per-body completeness fact (X12; the name is R4's, for example
  `MirDropTable.Incomplete(body) → (Code, Subject)?`).
  - The fact holds A1's or A2's own first problem in that body, or A1's arena record.
  - A refusal carries that code at that subject in the Emission partition with the `Emission` requirement (R5-M4).
  - It applies wherever MIR codegen runs: sel from C4a, the default pipeline with `KIMI_CODEGEN=mir` from G2, the default from G3.
    It is removed at H5.
- **Why a fact (verified).**
  - `MirReport.Add` merges by (code, subject) and `MirProblem` carries no body (`M/Analysis/MirReport.cs:22, 35-47`). The inline
    default fold copies one template's SourceRefs into several bodies (`M/Build/MirBuilder.Defaults.cs:8-12`).
  - A1's arena record is `OwnershipStorageLimit_Kd` at `body.Source` (`M/Analysis/MirInit.cs:58`), and R4-M15a gives A5's arena the
    same code.
  - Without the fact, a runtime `Group == -1` (`M/Analysis/MirDrops.cs:88`) is either an arena skip or a broken contract.

### 3.9 Coverage rows (PD11; C0b1 … X2)

- **Rows (D2 §2.12).** FunctionCodegen's dispatch arms and table rows plus the mono-item kinds are the rows. An unwritten one calls
  `Uncovered(SourceInfo)`: `Unsupported_Kd` once per location, under sel only.
- **Reporting.** Generation runs on to report every gap (SPEC 23.1.2), then discards the text.
- **Metric and removal.** M2 is counted from the same tables. At X2 the remaining rows, all decided D2 rows, move into K's
  `Capability`, and `Uncovered` is deleted.
- **No-op rows.** C0b1 lists `StorageLive`, `StorageDead`, `FakeRead` and `EndBorrow` (`M/MirVocabulary.cs:18-21`) as rows that emit
  nothing. Flag changes at their locations come from A2's flag points.

### 3.10 Successor of `GenerationFailed_Kd` (G9b; owner item)

- **Contract failures.** Codegen, Layout and Abi contract failures become `InternalInvariant_Kd`. The 446 `Fail(` strings
  (`tests/xUnitTest/Tests/ArchitectureRulesTest.cs:16`) go with G7 and G8.
- **Publication failures.** These are output I/O failures and the publication-only configuration and supply checks
  (`E/EmissionArtifacts.cs:34-185`). They take the owner's choice below.
- **Deleted.** The repeated `NativeConfiguration.Validate` (`:98`; already run at preparation via
  `src/Kimi/SolutionAndProject/DependencyConfiguration.cs:77`).
- **Reuse is permitted.** SPEC 23.1.1 makes non-SPEC codes implementation-defined.

| Option | For | Against |
| --- | --- | --- |
| **(a) New `ArtifactPublicationFailed_Kd` (Input)**, recommended | Names the owning operation (`EmissionArtifacts.Publish`); `GenerationFailed_Kd` deleted as PLAN10 §5.7 says; net 0 codes | A new code |
| (b) Reuse `ProjectPreparationFailed_Kd` (Input, free-text note) | No new code | Misnames output I/O; moving the configuration checks into preparation would change `check` outcomes |
| (c) Keep `GenerationFailed_Kd`, recategorized Internal → Input | No new code | Departs from PLAN10 §5.7; misnames publication; a published code changes category |
| (d) For the missing-supply cause only (`:169`): `MissingNativeRequirement_Kd` at the `#LibraryImport` | An existing Input code for native-requirement resolution | Its text speaks of a missing requirement or self-targeted record in the defining module, not a missing root supply. It combines with (a), (b) or (c) for the other causes |

`DiagnosticPartition.Emission` and the `Emission` requirement remain; a rename is left to R9. Toolchain failures stay plain text (an
R9 issue).

### 3.11 Debug-information record (PD24)

D2 §7 is adopted. No DI is generated in R5. What already holds:

- I2 slots every named local.
- `StatementStart` exists (`M/MirDump.cs:141`); control headers, match arms and `=>` lack it, and KD2's first builder unit adds them.
- `MirScope` gives scopes.
- ModuleCodegen owns debug names, unique per module.
- The runtime components list their artificial functions.
- Variable states need an A1 fact (KD7).

This replaces the Debug Adapter plan's §6.1 placement in `EmissionModule`.

### 3.12 Property-witness Storage shim (C3c; X9, X14)

- **Responsibility.** The body of a standard property witness. It is a Backend mono item whose key carries the Field symbol, the Self
  Type and the witness kind (K6 creates it from `TyCtxt.Witness`'s Storage kind; K fixes the `MonoKey` shape).
- **Body** by `PropertyWitnessKind` (`Bd/Binding.Properties.Model.cs:78-84`):
  - `StorageCopy` copies the Field's value to the result.
  - `StorageBorrow` returns the Field's address.
  - `StorageSet` destroys the old value through the item's `DropGlue(field Type)` target, then stores the new one.
  - The Field's offset is `Layout.Of`'s. An inherited Field is found with `AdtDef.FindField`, and the base-prefix invariant makes its
    address the receiver's plus that offset.
- **ABI.** `Abi.Of` of the requirement's signature instance.
- **Prohibitions.** No MIR body (PD3); writes only through FunctionCodegen's primitives.
- **Unit.** C3c in G:call. Prerequisites: K6, P7b, C4a (Set needs DropGlue). Fixture families `Counted*` and `Own*`. Size (H)
  +100, t+40.

### 3.13 Memory and time

As D2 §2.15:

- `Layout` and `Abi` are memoized; the text buffer and per-function scratch are pooled; warm generation allocates 0 B (186 sites,
  PD13).
- Codegen time is at most legacy `EmissionLowering + LlvmIrWriting`, recorded by E14 and X1.
- O0 argument spills cost one store and one load per argument, recorded.

---

## 4. O2 parity list

D2 §3 is adopted (`docs/dev/LLVM_OPTIMIZATION.md` §2, rows 実装あり/限定実装):

- `align` on alloca, load, store, globals and aggregate `memcpy`.
- `noundef nonnull align 8 dereferenceable(24)` on `SharedReference` parameters at definitions and at every call.
- `noalias nocapture` only on the `memcpy` declaration. The `memmove` declaration has `nocapture` without `noalias`
  (`E/LlvmModuleWriter.cs:21`), which is correct for overlapping ranges and kept.
- `noreturn` from `Abi`, plus `unreachable` after diverging calls.
- `unnamed_addr` on pooled constants and folded statics.
- The `#0` group.
- Bool stored as `i8`.
- The per-check failure block.
- `memcpy` for `transferBytes`; `memmove` for Array insert, remove and window advance.
- D1's `poison` start value of formatting aggregates (F02), inside the promoted formatting kernels.
- **The Ryu and formatting runtimes are spliced verbatim** from their `.ll.in` files, with their own attributes (`captures(none)`,
  `nuw`/`nsw`/`inbounds` in Ryu; LLVM_OPTIMIZATION A05, B01–B03). They are not rewritten and not extended to generated code.
- **Never written** (I9): `mustprogress`, `willreturn`, `nounwind`, `nsw`/`nuw` on checked operations, `inbounds` in generated code,
  `lifetime.*`, `range`, TBAA. The 13 test files asserting `mustprogress`/`willreturn` absent are class R and move into E15. `nsw` and
  `inbounds` absence is class I (R5-M12).
- **Gate.** No new optimization attribute in R5. E13 and the corpus A/B gate (R5-M8).

---

## 5. Design A versus design B (checkable on paper; R5-M3)

A = MIR lowered into `EmissionModule`, then the existing `LlvmModuleWriter`. B = §3.

### 5.1 The 49 opcodes (`E/EmissionModule.cs:9-75`; ARCH caps `EmissionOpcode` at 49, so A can add none without owner approval)

| Class | Opcodes | Count |
| --- | --- | ---: |
| Direct MIR counterpart | Label, Branch, ConditionalBranch, ReturnVoid, ReturnScalar, Unreachable, Call, VirtualCall, CallValue, Scalar, Convert, LoadScalar, StoreScalar, LoadElement, StoreElement, ElementAddress, BorrowAddress, LoadPointer, StorePointer, PointerOffset, ObjectPayload, ObjectBorrow, ObjectTypeTest, TransferAggregate, CreateClosure, EraseClosure, FillArray, Sequence, StringEquals, StringCompare, BuiltinComparison, DestroyAggregate, TestPhaseEnter, TestPhaseLeave, TestSnapshot (only after F8 gives `TestRecordValue` a producer) | 35 |
| Narrow: usable for one form only | StoreStaticString, MoveString (string only), SwapScalars (scalar replace/exchange/swap), TupleRelation (no producer once F7 expands tuple operators in MIR) | 4 |
| Old-IR protocol, no MIR producer | Phi; StoreLiveFlag, InitializeLiveFlag, DestroyStringIfLive (string live flags); StorePathFlag, DestroyPart, EndPartDestruction (old partial destruction); StringPattern, CompositePattern, PatternRead (old match plans) | 10 |

- D1's "38 mapped, 11 not" is the first two rows without TestSnapshot (pre-F8). D2's "35 and 14" puts the narrow row with the
  protocols. Both partitions sum to 49.
- **No opcode for:** a multiway `SwitchInt` (A writes compare chains); A2's Flag and Open verdicts (composed from LoadScalar,
  ConditionalBranch and DestroyAggregate at subslots); case views (`Adapt`, `PairLayer`, `PairAcquire`).

### 5.2 Criteria

| Criterion | A | B | Evidence |
| --- | --- | --- | --- |
| Run time (O2) | Same instruction text if the kernels are shared | Same | PD2; P4a/P4b kernels shared by both |
| Compile time | One more record pass | Direct | Writer stage 0.016–0.066 ms = 0.04–0.23 % of 22–43 ms; EmissionLowering 0.4–8.4 % (`artifacts/benchmarks/r3-u16c-v14/exit-compiler-pipeline-1/phases.json`, re-read). Neither difference is measurable |
| Representation | 19-field record with 4 string fields; `Operation` = old ownership id (54 writer reads); arithmetic keyed by `KotoKind` | MIR forms directly; operator table by `MirBinaryOp` (needed by both) | `E/EmissionModule.cs:141`; `grep -c instruction.Operation E/LlvmModuleWriter*.cs` = 54; `E/NumericArithmetic.cs:45-61` |
| Freeze (D5) | Redefines `Operation` as a per-function value id: a contract change of frozen code | Old code changes only through bp promotions | ARCH:172 |
| Retained code | `EmissionModule` 433 + `LlvmModuleWriter*` 4,601 past R6-gen. In the Backend (no baseline) about 9 members over 60 statements (D1 §2.5 proxy, H) and `Complete(out string?)` must be fixed; or a second rewrite (B) later | G8 deletes both | `wc -l`; ARCH "Structure limits" |
| New lines in R5 (H) | Lowering ≈2,500–3,500 + 500–800 for missing forms | ≈4,200 + ≈3,450 promoted | D1 §2.6; D2 §10 + C3c |
| Risk | MIR semantics forced into writers that assume `BodyLowering`'s invariants (live flags, operation ids) | New text, so new bugs are possible; gated by native behavior, C4s, E13 | — |
| Where A is better | First scalar slices reuse ≈2,300 tested serializer lines; `TryPrepare` structure tests keep working (class S, deleted at G5 anyway, PD14); the DI plan's placement fits | B's first slice is the frame (C0b1) | — |
| Owner texts | Departs from PLAN10 §5.7 ("物理 IR 層は置かない"), the ARCH target pipeline and the R0 ledger, unless a removal stage is named | As approved | — |

**Verdict: B.** A's material advantage, reuse of module-level and kernel text, is captured by PD2. Its costs are permanent or force a
second rewrite. B's first slice is measured against legacy (E13 at C1a).

---

## 6. Interfaces

### 6.1 Chapter K (S2)

| Direction | Item | Decision |
| --- | --- | --- |
| C provides | `Layout.Of` results OK/Limit/CLayout(instance) for K10a and K10b | X18 |
| C provides | DropGlue item: recursive per erased Type, targets in SPEC 16.3.2 order (§3.6); K's glue rows follow | X3 |
| C provides | Legacy rows declare their library callees and needs; the collector reads them | X4 |
| C provides | The intrinsic row table's whitelist column | X15 |
| C provides | Entry and TestEntry around the Generated roots; decided D2 rows into `Capability` at X2 | K-D6 |
| C needs | Items in Generated order (kind, body ordinal, representative), `View(item)`, `TypeOf(item, t)`, `Find(MonoKey)`, Generated mark; outside → `InternalInvariant_Kd` | X2, X13 |
| C needs | Per-site targets in `MirLocation` order: Fn, Requirement (K6: `Witness(requirement, contract, subst)`, kinds Body, Builtin, Storage, Refused, Limit; X16), Value, Drop, `Aggregate(Closure)`, `Const(FnItem)`, `Cast(Erasure)`, whitelisted intrinsics. None for Virtual or `#LibraryImport` calls | X5, X15, X16 |
| C needs | The property-witness `MonoKind` member and its `DropGlue(field Type)` target for Set | X14 |
| C needs | The selector from K1, with the sel refusal until C0b1 | X7 |
| C needs | The generic storage key as K7's capability row | X6 |

### 6.2 Chapter F (S3)

| Needed | From | Consumer |
| --- | --- | --- |
| `Compilation.Locations.Text(SourceRef)` | P5 | C1a+; which site an Abort reports is C's rule (`LocationOf`, I5) |
| `AdtDef.IsCLayout` | F1 | P7b |
| `StartupResult.Symbol` (no module column) | F2a | C0b2 |
| Import symbol column | F2b | P8 |
| Dictionary-storage symbols | F2c, through the legacy rows' declared callees | C8e |
| `VTableDef` slot order | F4 | C6c (X5) |
| `TyCtxt.Witness` results, through K6's targets only | F5b | C3b, C3c, C9b |
| `TyCtxt.Comparison(contract, self, operators)` tree | F6 | C8f (leaf targets from K) |
| `TestRecordValue`, `$abort` SourceInfo, `MirProgram.StaticInit` | F8, F9, F10 | C10a, C3b, K7 |
| Test-case symbol column | P6 (landed) | C10a, which adds the case-count check |

### 6.3 Asks to the R4 stream

| Ask | Status |
| --- | --- |
| **A-2** (required, X12) | Per-body completeness published by A1 and A2 from their own outcome (the first problem A1 adds for the body, `MirInit.Transfer.Add`; the arena record; A2's `MovedPlace_Kd`). R4 lands it in unit 1c (A5 step 1), independent of G125, and writes its DEC entry |
| A-9 | Met: `MayNeedDrop(type, seen)` is on dev at `5ae8ebb7` |

G131's validator rule is an R5 unit, F11 (§7). It needs no R4 ask, but it does need an R4 reviewer (O2 in §12).

---

## 7. Unit mapping (changes to plan §4 only)

| Unit | Change | Prerequisites | Review | Hold |
| --- | --- | --- | --- | --- |
| **F11 (new)** | Never-temp validator rule (G131). Validates that a local of Type Never is read only in blocks no runtime edge reaches (`MirReach` without CheckTarget edges). Adds one `MirViolation` row and a regression that fails before. Zero new findings over the library and the 40 milestones under mir. +20, t+30 (H) | W1 | R + an R4 reviewer (O2) | **W1**, whose file list gains `Mir/MirValidator*.cs` |
| P4a | Wording: the per-check failure writer | as plan | R | no |
| P7a | Also records the in-memory nonnull `Option` limit in STATUS (round-0 O9) | as plan | D+R (S4) | no |
| P7b | `Layout.Of`'s three results (X18) and the base-prefix test (X1) | as plan, F1 | D+R (S4) | no |
| P8 | `PreparedSlot` not ported; no gate | as plan | D+R (S4) | adv |
| E12 | One `CodegenDeviations` catalog; lands with the first of C5a, **C7b** and K13a | E4, C0a | with its first consumer | no |
| E15 | Carries the class-R absent-attribute requirements | C0b1 | G:C-frame | no |
| C0a | Loses the selector to K1 (X7). Creates `Core/Compilation.Generation.cs`: entry, gate step 1, typed records, out-string adapters, `kimi test` path, `-Codegen`, `summary.json` (X17) | S5, K1 | G:C-frame | adv (moving three members out of `Compilation.Pipeline.cs`) |
| C0b1 | Four no-op rows; zero-sized address symbol; the intrinsic row table if first; deletes the K1/C0a sel refusal | C0a, P0, P7a, P8, **F11** | G:C-frame | no |
| C0b2 | Entry mono item from `StartupResult.Symbol`; `writeLine(string)` only | C0b1, F2a, E10 | G:C-frame | no |
| C3a | Reads K's targets by cursor; "prepared slots" dropped | C2b, K4a | G:call | no |
| C3b | Requirement callees through K6's target | C3a, K6, P5, F9 | G:call | no |
| **C3c (new)** | Property-witness Storage shim (§3.12) | K6, P7b, C4a | G:call | no |
| C4a | DropGlue recursive per Type (X3); gate step 2 in `Compilation.Generation.cs` | C4s, K7, A-2 (R4 1c) | G:C-drop | no |
| C4c | Open rows in SPEC 16.3.2 order through `DropGlue.Parts` and `Find` | C4b | G:C-drop | no |
| C5a | Closure-environment glue (X23); ABI attributes on value calls | as plan | G:closure | no |
| **C7a (new)** | `KIMI_E_STATIC_CYCLE` row; declared legacy-text change; E4 report plus a throwaway hunk check. ≈ +3 | P0b | R | no |
| C7b | C7 without the row: slot states, per-slot `ensure`, fold (applied by ModuleCodegen), D2 rows, the cycle test through E12 (`static-cycle`, class (a)) | C3a, K7, E12 | D+R (S4) | no |
| C8a | `transferBytes` → `memcpy`; the `MoveBytes` comment corrected (`Bd/KimiLibraryKinds.cs:116`) | as plan | G:library-1 | no |
| C9a | 26 rows (+ `WriteLineUtf8`) | as plan | G:format | no |
| C9b | Reads K6's target for ErasedWriter and the witness wrappers; never calls `TyCtxt.Witness` (X15) | C9a, **K6** (replaces F5b) | G:format | no |
| C10b | as plan | C10a, C4c, **C7b** | G:test | no |
| G9 → **G9a**, **G9b** | G9a: Emission-only Binding members. G9b: §3.10's successor, the repeated validation and the adapters deleted, DIAGNOSTICS §4.2 row, ratchet 446 → 0 | G8 | G:R6-gen-del | no |

- **Groups.** G:call is F5b, K6, C3a, C3b and **C3c**. The others are as the plan and K's package state.
- **Order.** C4a, the drop chain (C4b–C6c, C3c) and X1–X3 wait for R4's unit 1c (A-2).

---

## 8. Departures from owner-approved texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| PLAN10 §5.4/§5.7, A5P §9.1 | PD3: no `MirBodyKind.Shim`; shims, the property-witness Storage shim included, are Backend mono items | Already highlighted (R5-M1); X14 |
| ARCH:45-47 | PD11: codegen reports `Unsupported_Kd` under sel until X2, including the sel refusal from K1 to C0b1 | Already highlighted (R5-M1) |
| Plan §3.4 A-2 "(Optional)"; PD12's mechanism; plan §3.2's fallback (d) | A-2 required, in R4's 1c. C4a, the drop chain and X1–X3 wait for A5 step 1, so fallback (d) loses its meaning | §3.8; X12 |
| Plan PD23 (`memmove`) | `memcpy` | The library declaration rules out overlap; O2 |
| PLAN10 §5.8 (`Storage.moveBytes`, overlap allowed) | C8a's comment states "no overlap" for `MoveBytes` until R8 changes the declaration and the lowering together | X25 |
| R5-M13(b) | "No representation change" holds for the slot state only: generic keys need `MirStatic` Type arguments. The fold keeps no slot state (P42 narrows it or restores it). Its premise misses generic-container scalars (X22) | §3.7 |
| PLAN10 §5.7 (`GenerationFailed_Kd` deleted) | Deleted; publication failures take the owner's code (§3.10) | A cause PLAN10 did not cover |
| Kimi Debug Adapter Plan §6.1 | DI from FunctionCodegen | PD24 |
| Plan wording | E12's `Codegen.Expect(target, legacy, item, class)` → `Expect(target, legacy, Deviation)` + the `CodegenDeviations` catalog. C0a's selector → K1 (X7). PD8's "shared failure block" → the shared per-check writer. C0b2's "`Shim(Entry)`" → Entry mono item. C3a's "prepared slots" dropped | Facts and decisions above |

---

## 9. Owner items

1. The publication-failure code: (a) new `ArtifactPublicationFailed_Kd`, recommended, or (b), (c), with (d) as an option for the
   missing-supply cause (§3.10).
2. Keep `DiagnosticPartition.Emission` and the `Emission` requirement name through R6.
3. Facts and departures: §8. No new MIR vocabulary, `IntrinsicId`, `CompilerFunctionKind` or `BindingFailure` (PD21). The reason row
   is runtime data already declared in plan §5.3 item 7. Temporary structures: the out-string adapters (C0a to G9b), `Uncovered` and
   the sel refusal (to X2 and C0b1), and the deviation catalog (to G4).

---

## 10. What becomes unnecessary; size and speed

- **R5.** No `EmissionModule` pass under sel, and no second copy of checked-arithmetic, helper, ABI or layout rules. P units move
  ≈ −3,150 from `E/` and ≈ +3,450 into `B/`.
- **G7/G8.** These delete:
  - `BodyLowering*`, `LlvmEmitter*` and the plans;
  - `EmissionModule` (433) and the rest of `LlvmModuleWriter*`;
  - `AggregateLayout`'s destruction half;
  - `FunctionAbiPool`'s `FunctionKoto` keying and its `Supports` gate.
- **G9b.** `GenerationFailed_Kd`, 446 `Fail(` strings, the repeated `Validate` and the adapters.
- **X2/G4.** `Uncovered` (≈ −50); the selector, ledger, catalog and `.deviation` (≈ −400).
- **New.** ≈ +4,200 (H: D2's ≈ 4,100 plus C3c's ≈ 100) against the plan's ≈ +4,200, and F11 +20.
- **Speed.** Compile time at most legacy's generation stages. O2 parity by §4, measured by E13 and the corpus. The O0 cost is
  recorded.

---

## 11. Risks

| # | Risk | Response |
| --- | --- | --- |
| R1 | A-2 lands in R4's 1c, so C4a, the drop chain and X1–X3 wait for A5 step 1 | Accepted (X12). C0–C3 and the library and formatting chains proceed meanwhile |
| R2 | The three packages are redone in parallel and diverge on X2, X3, X13–X16 | Round 2 reviews them together |
| R3 | F11's rule finds a builder fault (a runtime read of a Never temp) | Fixed in W1 as a builder fault before C0b1; never suppressed |
| R4 | Recursive glue costs calls at O0 | Legacy shape; O2 inlines (H); O0 recorded |
| R5 | C7a touches every legacy `.ll` | Throwaway hunk check in C7a's evidence |
| R6 | The fold misses a form legacy folds, so a static read regresses at O2 | C7b's test over `StaticScalar.TryGet`'s set |
| R7 | Storage shim Set destroys twice or not at all | C4s's matrix gains Set rows; E2's fail-closed audits; `Counted*` fixtures |
| R8 | Static D2 rows are untested until H5 | Accepted; X2 puts them in `Capability` |
| R9 | The tagged `Option<ref/T>` persists to U8 | STATUS row (P7a) |
| R10 | Thread-safe static initialization is not designed (H: no offload yet) | Open issue; the `i8` state admits `cmpxchg` |

---

## 12. Open points for the planning session

Round-0 O1–O9 are decided (`S2-S4-decisions.md`: X12, owner item, X3, X2, X1, X4, O7, O8, O9). What remains:

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| O1 | X8 keeps a "C-layout instance view" in `TyCtxt.Instances.cs`, while X18 makes `Layout.Of` the one owner of the zero-sized C-layout rule at any depth. Which serves K10b? | (a) `Layout.Of`'s CLayout result only; X8's view is dropped, and Binding's eager check stays until G2b; (b) the view stays, and `Layout.Of` calls Binding's shared predicate | (a). Under (b), two owners compute "zero-sized" differently (type rule versus size) |
| O2 | F11's review and window | (a) W1 gains `Mir/MirValidator*.cs`; R plus an R4 reviewer (A1–A5 trust the validator); (b) a separate short hold | (a) |
