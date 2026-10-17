# R5 step 0, chapter C (S4): package

Status: synthesis of the two designs of chapter C for the S2–S4 design review and the owner's one look (OQ4, R5-M1). Design 1 =
`deep-dives/q2-codegen-shape.md` ("D1"); design 2 = `C-design-2.md` ("D2"). Working record, not authority.

Base: `claude/r5-package` at `464047ed`; its code is `origin/dev` `a8c2ec93` (D2 cites `041ced79`; the `src` difference is P6, Q6
and Q15, none of which touches a cited site). Paths as in the plan: `B/` = `src/Kimi/Compiler/Backend/` (new), `E/` =
`src/Kimi/Compiler/Emission/`, `M/` = `src/Kimi/Compiler/Mir/`, `Bd/` = `src/Kimi/Compiler/Binding/`. "Verified" = read at the base
for this package; (H) = estimate or unverified inference.

---

## 0. Summary

- **Design B** (PD1): `FunctionCodegen` writes LLVM text from validated MIR, one Generated instance at a time; no physical IR. Design A
  is judged on paper in §5 with checkable evidence (R5-M3).
- **Promote, don't copy** (PD2), with D2's rule: a promoted member takes generator-neutral inputs only (LLVM type text,
  `TypeLayout`/`ValueLowering`, SSA names, labels, a writer). Layout and Abi are shared with legacy, so bp holds; IMPL 21.1.3's
  one-word `Option<ref/T>` stays a STATUS limit until after G8 (PLAN U8).
- **PD12's gate reads one published A2 fact** (`MirDropTable.Incomplete(body)`), not problem codes. Verified: `MirReport` merges
  per (code, subject) with no body. R4-M15a gives A5's arena the same code as A1's. Ask **A-2 becomes required** before C4a.
- **PD23 reversed**: `transferBytes` keeps `memcpy`. Its library declaration says the ranges do not overlap, and both callers copy into
  a fresh allocation. The `IntrinsicId.MoveBytes` comment is corrected in C8a.
- **DropGlue is recursive per Type** (one glue per erased Type calling its direct parts' glues, in SPEC 16.3.2 order). This matches
  legacy and D2, but **not K-design-2**, which gives a glue the leaf parts of the destruction walk as targets (open point O3).
- **Statics**: four `i8` states, `KIMI_E_STATIC_CYCLE` at the access, constant fold. The reason row is split off as **C7a**, because it
  changes every legacy `.ll`. Fact for the owner: P42's generic storage keys need `MirStatic` to carry the container's Type arguments.
  That is a MIR representation change, so R5-M13(b)'s "no representation change" covers the slot state only.
- **Successor of `GenerationFailed_Kd`**: contract failures become `InternalInvariant_Kd`; publication failures get one new Input code,
  `ArtifactPublicationFailed_Kd` (owner item; the reuse options are weighed in §3.10). `DiagnosticPartition.Emission` and the
  `Emission` requirement name stay. G9 splits into G9a/G9b.
- **Coverage rows are the codegen's own dispatch tables** (PD11): one `Uncovered(SourceInfo)`, no partial module, sel only, moved into
  the capability table at X2. S1's UNOWNED items: `WriteLineUtf8` → C9a; the four no-op statements → C0b1 rows.

---

## 1. Required meaning

D2 §1 is adopted unchanged (IMPL 21.1–21.4, SPEC 14.10.3, 16.3.2–16.3.3, 22.4.3–22.4.4, 22.5.1–22.5.2, 23.1.1, 23.2.2, ARCH
dependency rules, R5-M8). These rows are added:

| Rule | Consequence here |
| --- | --- |
| SPEC 22.4.3 example (`docs/spec/22-core-execution-and-foreign-functions.md:403`): the cycle is "reported at `Values`" | The static Abort location is the access's start, the qualifier. It is the `staticEnsure` call's SourceInfo (H: that its span starts at the qualifier; C7b tests it) |
| SPEC 23.1.1 category table (`docs/spec/23-compiler-services.md:12-19`) is closed | An output or publication failure must be one of the six categories: Input |
| IMPL 21.1.3 nonnull Option (`docs/impl/21-layout-runtime-and-code-generation.md:65`) | Not laid out today (`Bd/Binding.Attributes.cs:318`). STATUS records only the import ABI (`docs/STATUS.md:80`), although PLAN U8 says the in-memory form is recorded |
| AGENTS: O2 run time and native allocations never regress | Decides PD23 (§2 row 24) and the static fold's input set (§3.7) |

---

## 2. Decision table

| # | Topic | Design 1 | Design 2 | Chosen | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | Codegen shape | B | B | B | PLAN10 §5.7, ARCH target pipeline; §5 |
| 2 | Sharing | Promote, don't copy (G0–G7) | Same, plus generator-neutral inputs (§2.10) | D2 | The input rule is what keeps a promoted kernel free of `EmissionModule`/`AggregateLayout`, so G8 can delete them |
| 3 | `LegacyRuntimeHelpers` scope | ≈970 module-level lines; `DictionaryIteration` not verbatim (§4) | Same, through P1a–P2b | Agree | Plan P1a–P2b already follows D1 §4 |
| 4 | Facts for Backend legality | F1 location by `SourceRef`, F2 Binding declaration key, F3 test column | P5, P6; no declaration key (PD17) | D2 | PD17 (adopted) removed F2 |
| 5 | `FunctionCodegen` contract | §3.4 (5 invariants) | §2.2 (I1–I11) | D2, with rows 6–8 | D2 is the superset; D1's points are all in it |
| 6 | Semantics view of an instance | Codegen calls `MirCases.Views(body, subst)` once | The collector supplies a view index | **D1** | `MirCases` owns views and codegen is the only reader. K-design-2's item carries no view. Verified: for a closed Subst, `Views` yields one bit, or none if the case is not admitted (`M/MirCases.cs:84-111`). One bit is required, else `InternalInvariant_Kd` |
| 7 | Callee lookup | "collector names" | `(Block, Target)` rows, binary search | **K-design-2's `Target(Item, At, Runtime)`** in `MirLocation` order, forward cursor | The producer owns the shape. Codegen walks blocks and statements in order, so a cursor costs O(1) per call |
| 8 | Instance Types | `TyCtxt.Apply(t, subst)`, cached per instance | Collector-owned `instanceType(T)` | One function, owned by the collector | Collector (K7, K10a) and codegen must agree on one rule. K-design-2 lets each compose `Erase(Apply(t, rep))`: open point O4 |
| 9 | Blocks | `bb<n>` per runtime block | `b<index>` per runtime block | D2 | Legacy spelling. Runtime-reached = `MirReach.Walk(body, 0, checks: false)` (`M/MirReach.cs:48`) |
| 10 | Frame | alloca per non-zero-sized local; bool `i8`; flags `i8` | Same; zero-sized address = one module-level 16-aligned symbol; pointer-passed argument used in place | D2 | IMPL 21.1.1 allows shared zero-sized addresses; 16 is the largest alignment |
| 11 | Text sink | (requests) | Pooled buffer, two assembly phases | D2, with a stronger reason | PD8 keeps `EmitFixture`'s check, which scans from the first `__kimi_f`/`__kimi_entry_body` definition to the end of the module (`tests/xUnitTest/Tests/ScalarEmissionTest.cs:136-138`). Runtime helpers contain `select i1 %zero,` and similar (`E/LlvmModuleWriter.Dictionaries.cs:35`, `.Arrays.cs:32-38`, `.Comparisons.cs:27-39`), so helpers must precede functions. Their requests are known only after codegen |
| 12 | Checked-arithmetic failure block | Shared kernels (G7), today's `WriteArithmeticFailure` | Per check, not shared; P4a promotes the per-check writer | D2 | Verified: one `abort<id>:` block per check, the `%zero` select inside it (`E/LlvmModuleWriter.Scalars.cs:441-490`). The plan's "shared failure block" means the shared writer (wording fix) |
| 13 | O2 parity list | §3.3, 12 rows | §3, 15 rows | D2, plus D1's `poison` row (F02) | §4 |
| 14 | ABI attributes on indirect calls | "reuse `AbiParameter.Attributes`" | Write them wherever a parameter list is written | D2 | Legacy omits them only on value calls (`E/LlvmModuleWriter.Closures.cs:265-290`; direct calls at `E/LlvmModuleWriter.cs:641-642`). The attribute states the callee's guarantee, which is true at every call site. Strict parity would need a flag steering the one writer (a symptom fix). IR text is information (PD9) |
| 15 | Layout | Port `GetCore`, split by responsibility; facts replace syntax reads | Shared `Layout.Of`, rules verbatim, tagged Option kept, open re-check deleted, destruction out | D2 + D1's split | bp; one layout rule. A MIR-only nonnull Option would be a second rule during R5 |
| 16 | Abi | `Abi.Of` via `GetDefinition`; delete `Supports` | Pure core; C convention; no gate in `Abi`; `PreparedSlot` not ported | D2 | Verified: `PreparedSlot` exists only for legacy default evaluators (`E/DefaultGenerationPlan.cs:148`, `E/BodyLowering.Defaults.cs:42`) |
| 17 | DropGlue | Shim of the instances topic; legacy template not moved | Per-Type structural table; drop-declaring struct = destructor only (PD18) | D2, made explicit: **recursive per Type** | Legacy glue is recursive per aggregate Type (`E/LlvmModuleWriter.Aggregates.cs:224, 264, 279`). SPEC 16.3.2's order is per level (Fields in reverse, then the base), which a flat forward leaf list (`M/MirTypes.Destruction.cs:68-122`) cannot express. Open point O3 with K |
| 18 | Selector | PD4 | PD4 + identity and manifest mark | D2 | — |
| 19 | Generation entry | — | `Compilation.Generate(TextWriter)`, typed records; `WriteIr`/`Validate` out-string adapters `[Transitional("R6-gen")]` | D2 | ARCH: no out-string failure text; `Generates(out string?)` (`src/Kimi/Compiler/Core/Compilation.Pipeline.cs:154-158`) stays only behind the adapters |
| 20 | PD12 gate | Must require A2 completeness; `Group == -1` at a runtime Drop → `InternalInvariant_Kd` | A2 publishes `Incomplete(body)`; A-2 required | D2 | §3.8. Both designs need a completeness fact. Only D2 names it |
| 21 | Coverage rows | Rows for the slice, others located `Unsupported_Kd` | The dispatch tables are the rows; `Uncovered`; no partial module; X2 | D2 + S1 UNOWNED 2 | §3.9 |
| 22 | Deviation form | — | One test-side catalog, one `Deviation` per item | D2 | An item, its reason and SPEC clause are declared once (R5-M6) |
| 23 | Statics | `staticEnsure` state machine | Four `i8` states, cycle at access, fold, C7a/C7b, D2 rows | D2 + a per-slot cold `ensure` function; the generic-container static is K's capability row | §3.7 |
| 24 | `transferBytes` (PD23) | — | `memcpy`; correct the `MoveBytes` comment | D2 | Verified: `src/Kimi/Library/StorageOperations.kimi:71-75` ("the two ranges do not overlap"). Its only producer is `Bd/KimiLibraryKinds.cs:208`. Both callers copy into a fresh allocation (`src/Kimi/Library/DictionaryStorage.kimi:103-107`, `:133-151`). SPEC and LIBRARY define no overlap rule. `memmove` would risk the Dictionary kernel's O2 (a constraint) for no case a caller can reach. PLAN10 §5.8's overlap-allowed `Storage.moveBytes` is R8's primitive; R8 changes the declaration and the lowering together |
| 25 | `GenerationFailed_Kd` successor | `InternalInvariant_Kd` (no output case) | `InternalInvariant_Kd` + `ArtifactPublicationFailed_Kd` | D2 (owner item) | §3.10 |
| 26 | Debug information | — | Record only (§7) | D2 | PD24 |
| 27 | G9 | — | G9a (Binding members), G9b (diagnostics successor) | D2 | Two concerns, different files and reviews |
| 28 | `WriteLineUtf8` (S1 UNOWNED 1) | — | — | **C9a** (26 rows) | Its ABI is in the formatting table (`E/WindowsLowering.Formatting.cs:60`) and its runtime in `E/Utf8BufferRuntime.ll.in:25`, both moved by P1d. C0b2 needs only `writeLine(string)` |
| 29 | `EmitFixture` names | Keep | Keep, by construction | Keep (PD8) | `__kimi_f<N>`, `__kimi_entry_body`, `%zero<N>`/`%minusone<N>` from P4a's kernel with a per-function counter |
| 30 | Requirement receiver adjustment | — | — | The collector's target carries F5b's `BaseDepth` | F-design-2 §3.4 hands `BaseDepth` to codegen, but K-design-2's `Target` has no slot for it. Codegen must not call `TyCtxt.Witness` again (one resolution). Open point O5 |
| 31 | Callees and needs of legacy rows | The collector roots the row's library callees (§4.3) | — | The `LegacyRuntimeHelpers` row declares them; the collector reads them | P1c makes the library ABIs declared callees of the rows. K-design-2's own "needs table" would be a second copy. Open point O6 |

---

## 3. Components and contracts

### 3.1 Overview

| Component | Files | Lands in | Lifetime | Contract |
| --- | --- | --- | --- | --- |
| `LlvmModuleText`, `LlvmConstantPool` | `B/LlvmModuleText.cs`, `B/LlvmConstantPool.cs` | P0 | permanent | promoted (§3.4) |
| Runtime ABI table, abort reasons, ABI and layout records | `B/RuntimeAbi.cs`, `B/AbortReasons.cs`, `B/AbiRecords.cs` | P0b | permanent | promoted |
| `LegacyRuntimeHelpers` | `B/LegacyRuntimeHelpers*.cs` | P1a–P2b | `[Transitional("R8")]` | promoted, plus rows (§3.4) |
| `ObjectRuntime` | `B/ObjectRuntime*.cs` | P3b | permanent (PLAN10 §5.8) | promoted |
| `LlvmScalarText` | `B/LlvmScalarText.cs` | P4a, P4b | permanent | promoted |
| `Layout`, `Abi` | `B/Layout.cs`, `B/Abi.cs` | P7a–P7c, P8 | permanent | §3.5 |
| `ModuleCodegen`, `FunctionCodegen` | `B/Codegen/ModuleCodegen.cs`, `FunctionCodegen.cs` (contract entry), `FunctionCodegen.<Concern>.cs` | C0b1+ | permanent | §3.2, §3.3 |
| `DropGlue`, `ObjectDescriptors` (with VTables), `Statics`, `Shims` | `B/Codegen/*.cs` | C4a, C6a, C7b, C5b/C8f/C9b | permanent | §3.6, §3.7; shims D2 §2.9 |
| Selector, `Generate`, gate | `Core/Compilation.Pipeline.cs` (after G1 its generation partial) | C0a, C4a | selector R6-gen; refusal path H5 | §3.8 |
| Deviation catalog, pending ledger | `tests/xUnitTest/Codegen*.cs`, `scripts/codegen-pending.ps1` | E10–E12 | G4 | D2 §2.13 |

The collector, `Capability` and the Generated column are chapter K's (§6.1).

### 3.2 `FunctionCodegen`

D2 §2.2 is adopted, with these edits:

- **Responsibility.** It writes the LLVM definition of one Generated body instance from its validated, analyzed MIR body. It also
  provides the primitives every Backend mono item writes through (slot, load, store, address, call, switch, branch, check, abort),
  so one ABI writer, one attribute writer and one location rule serve all functions.
- **Inputs.**
  1. The accepted program version: `MirProgram.Bodies` and `.Drops`, which the gate has passed.
  2. The collector's item: kind, body ordinal, representative Subst, its targets (`Item`, `At`, `Runtime`) in `MirLocation` order,
     and the Generated mark.
  3. The instance-type function (O4), `TyCtxt.Signature`, and a codegen-owned `MirCases` with the view
     `MirCases.Views(body, representative)` (computed after `Deselect`, `M/MirCases.cs:76, 84`) selected once.
  4. `MirReach` runtime blocks.
  5. `Layout.Of` and `Abi.Of`.
  6. Location text by `SourceRef` (P5) and `KimiLibraryCatalog.RequiresCallerLocation` (`Bd/KimiLibraryCatalog.cs:265`).
  7. ModuleCodegen's request tables.
- **Outputs.** As D2: definition text in the pooled buffer, requests, and typed records (`Unsupported_Kd` per uncovered location
  under sel until X2; `InternalInvariant_Kd` at a broken input contract, after which no module is published).
- **Invariants.** D2's I1–I11, with three changes:
  - **I1**: the text also holds no checkout path, so E4's byte comparison (no path normalization) applies to MIR `.ll` too.
  - **I8**: a call's callee is the target at its `MirLocation`, read with a forward cursor. Intrinsic, Virtual (slot from
    `VTableDef`, F4) and `#LibraryImport` calls have no target. A missing expected target is `InternalInvariant_Kd`.
  - **I6**: a runtime Drop with `Group == -1` in a body whose `Incomplete` is null is `InternalInvariant_Kd`.
- **Prohibitions.** As D2: no syntax, HIR, plans or Binding internals; no re-derived acceptance fact; no `Supports*`, out-string,
  LINQ, catch-all or member over 60 statements. It never runs on a refused program, never composes a Subst and never resolves a
  callee.
- **Dispatch.** One switch per closed MIR vocabulary. Intrinsics and compiler functions go through one row table: 33 `IntrinsicId`
  rows and the Body-disposition `CompilerFunctionKind` rows (`Bd/KimiLibraryKinds.cs:183-219`). The default arm is `Uncovered`.

### 3.3 `ModuleCodegen`

D2 §2.3 is adopted:

- It names items in Generated order (`__kimi_f<N>` for body instances, kind prefixes for artificial items), runs FunctionCodegen
  and the mono-item writers, and writes Entry or TestEntry around the Generated roots. K-design-2 keeps Entry and TestEntry out
  of the item table, which is consistent with this.
- It assembles the module through `LlvmModuleText` in legacy order. The reason is §2 row 11.
- Function text is buffered once, so requests are known before the header is written: two phases over one buffer, never two
  passes over MIR.
- It decides neither what is generated (K11) nor any order other than Generated order.

### 3.4 Promoted components (P0–P4b; bp each)

| Component | Responsibility | Inputs | Output | Invariant | Prohibition |
| --- | --- | --- | --- | --- | --- |
| `LlvmModuleText` | Module frame: header, footer `#0`, memory and overflow declarations, runtime template splice, external names, number formatting | writer, request flags | text | legacy `.ll` byte-identical while shared | no per-generator branch |
| Runtime ABI and reasons | `FunctionAbi` table of runtime symbols; the closed SPEC 22.5.2 reason table | — | records | contiguous indices (`E/WindowsLowering.Abort.cs:53-80`) | LINQ (`.Any` at `:61` rewritten in P0b) |
| `LegacyRuntimeHelpers` | Array, Dictionary, formatting and string-comparison runtime text; one row per Body-disposition kind; each row declares its library callees and type-driven needs (§2 row 31) | element descriptor (`ValueLowering`, drop name, text flag, Option offsets) | helper text, ABIs | a new kind adds a row; written once, in today's order | no `AggregateLayout`, `EmissionModule` or MIR type |
| `ObjectRuntime` | obj/rc/arc runtime, descriptors, creation | descriptor rows | text | release order per SHARED_OBJECT_RUNTIME | — |
| `LlvmScalarText` | Checked arithmetic, division, shift and conversion checks; the per-check failure block | type text, SSA names, labels, reason, location operands | text | `select i1` only in its failure block (PD8) | no opcode or MIR type |

### 3.5 `Layout.Of` and `Abi.Of`

**`Layout.Of(BoundType)`** (D2 §2.5):

- **Responsibility.** Size, alignment, stride and field offsets of a closed Type. It ignores Origins; MIR callers pass erased Types,
  and legacy callers until K3 pass unerased ones with equal results.
- **Rules.** Ported verbatim, all verified: scalars and handles; Array 3, Slice 2 and Dictionary 7 words
  (`E/AggregateLayout.cs:297`); C written order and Kimigayo alignment groups; `i32` enum tag; depth 64 (`:47`); 2^31−1
  (`:171-176`).
- **Outputs.** A typed limit result (P7c) that K10a reads.
- **Prohibitions.** No destruction: the callbacks stay in the old pool until G8, and DropGlue replaces them. No syntax read:
  `IsCLayout` (F1) replaces the attribute walk (`:255-265`), and the `Open` re-check (`:300`) is deleted. No nonnull Option before
  U8.

**`Abi.Of(convention, flags, signature)`** (D2 §2.6):

- **Rules.** `FunctionAbiPool`'s pure core plus the `#LibraryImport` C branch. It returns no name, because ModuleCodegen names.
- **Exclusions.** No `Supports` gate (the old one stays with its callers until G8). `PreparedSlot` is not ported (row 16).

### 3.6 `DropGlue` (C4a; C6b for handles)

- **Responsibility.** One internal function per erased Type that `MirTypes.MayNeedDrop` admits, destroying a value of that Type in
  SPEC 16.3.2–16.3.3 order.
- **Body.**
  - A drop-declaring struct calls only its destructor instance (PD18, R4-M6b).
  - Another struct destroys each Field whose Type may need drop, last to first, then its base.
  - Tuples and fixed arrays destroy elements in decreasing index (a fixed array in one loop over the element glue).
  - An enum switches on its tag and destroys the active Case's payloads in reverse.
  - A string uses the runtime release; Array and Dictionary use the `LegacyRuntimeHelpers` row with the element glue's name.
  - obj/rc/arc release through `ObjectRuntime`: a Sealed payload calls its glue directly, any other runs the descriptor finalizer.
  - A function value calls its environment table's drop entry. InlineStorage has no glue. An abstract part is
    `InternalInvariant_Kd`.
- **Targets (from K7).** One per direct part whose glue is an item: each Field, component, base and Case payload Type that
  `MayNeedDrop` admits, in declaration order; or the destructor instance; or the Sealed payload's glue. Codegen consumes them in
  SPEC order.
- **Invariant.** Exactly-once destruction, which C4s's matrix checks through E2's fail-closed audits.
- **Prohibition.** Never reuses `__kimi_drop_aggregate`, which also destroys the Fields after the destructor
  (`E/LlvmModuleWriter.Aggregates.cs:192-203`).

### 3.7 `Statics` (C7a, C7b)

- **Slot.** Per `MirStatic.Symbol`: `@__kimi_static<N>`, Layout-aligned, and `@__kimi_static<N>.state`, an `i8`. States 0 Not
  started, 1 Initializing, 2 Initialized and 3 Finished are named constants (SPEC 22.4.3, R5-M13(b)). Legacy has three
  (`E/LlvmModuleWriter.Statics.cs:23`).
- **Access (refinement of D2).** At each `staticEnsure`, the hot test is inline: `load i8`, `icmp eq 2`, `br`. The cold path calls
  a per-slot `__kimi_static_ensure<N>(location, length)`. That function stores 1, calls the StaticInit target into the slot and
  stores 2, or Aborts with `KIMI_E_STATIC_CYCLE` at the passed location for any other state (state 1 only in R5).
  - Reason: one copy of the state machine per slot, as legacy's getter. The access location is an argument (SPEC 22.5.1), using
    the caller location inside a `RequiresCallerLocation` body. O0 pays one load and compare.
  - P42 splits off state 3 with `KIMI_E_STATIC_SHUTDOWN`.
- **Fold.** `Statics.ConstantOf(body)`: a StaticInit whose runtime blocks hold only storage markers and `_0 = Use(Const)` becomes an
  `internal unnamed_addr constant`, with no state. C7b's test covers legacy's folded set (`src/Kimi/Compiler/StaticScalar.cs:38-58`:
  bool and signed integer literals). Verified: a signed literal is one `HirOp.Literal` with a folded value
  (`Bd/Binding.Hir.cs:105-109, 272`), so it builds as `_0 = Use(Const)`. (H) The same holds for a parenthesized literal, which legacy
  unwraps.
- **C7a.** The reason row `KIMI_E_STATIC_CYCLE` (18th row).
  - Verified: the runtime with the expanded table is written into every module (`E/LlvmModuleWriter.cs:71`, `:733`), and
    `{{abort_reason_count}}` appears in the runtime (`E/WindowsRuntime.ll.in:119, 128`). So the row changes every legacy `.ll` and
    no native behavior.
  - This is the declared change of plan §5.3 item 7. Writing the code through the message-abort path instead would be a second
    representation of SPEC codes.
- **Remaining static shapes.** Mutable, aggregate and drop-needing statics are D2 rows: coverage rows until X2, then capability
  rows. They are unreachable under sel, since old acceptance admits only `StaticScalar` (R5-M13(b)).
- **Generic-container static.** It is already K's capability row (K-design-2 K-D6), so it is not a C row.
- **Fact for the owner.** `MirStatic(Symbol, Type, Mutable, RawHolding)` (`M/MirBody.cs:201`) is deduplicated by symbol
  (`M/MirBody.cs:442-451`), and its Type is the declared `symbol.Type` (`M/Build/MirBuilder.Places.cs:158-161`). So SPEC 22.4.4's
  key (declaration plus enclosing Type arguments) is not representable. P42 needs a MIR change for generic keys; R5-M13(b)'s "no
  representation change" holds for the slot state only.

### 3.8 Selector, entry and gate (C0a, C4a)

- **Selector, entry and adapters.** As D2 §2.11:
  - `KIMI_CODEGEN` is read once and `[Transitional("R6-gen")]`. Before G2 it is valid only with `KIMI_PIPELINE=mir`, and other
    combinations throw.
  - `CompilerIdentity` and the manifest mark it.
  - One `Compilation.Generate(TextWriter)` with typed records serves `EmissionArtifacts.Publish`, `Project.Emit` and `kimi test`.
    `kimi test` today publishes only a string (`src/Kimi/Unit/Command/TestCommand.cs:131-135`).
  - The out-string `WriteIr`/`Validate` adapters stay until G9b.
- **Gate, step 1 (C0a).** Generation needs `MirRecords == 0` (construction, validator and, from K4, collector records).
- **Gate, step 2 (C4a).** For each Generated item's body, read A2's per-body fact `MirDropTable.Incomplete(body) → (Code, Subject)?`.
  - The fact is the first problem A1 or A2 recorded in that body, or A1's arena record.
  - When non-null, it refuses with a typed record carrying that code at that subject, in the Emission partition with the
    `Emission` requirement (R5-M4).
  - It applies wherever MIR codegen runs: sel from C4a, the default pipeline with `KIMI_CODEGEN=mir` from G2, and the default from
    G3. It goes at H5.
- **Why a fact, not codes (verified).**
  - `MirReport.Add` merges per (code, subject) and `MirProblem` carries no body (`M/Analysis/MirReport.cs:22, 35-47`). The inline
    default fold puts one SourceRef into several bodies, so a subject does not name a body.
  - A1's arena record is `OwnershipStorageLimit_Kd` at `body.Source` (`M/Analysis/MirInit.cs:58`). A5's arena reports the same
    code at the same subject (R4-M15a; A5P §4 "Limits"). A code list would therefore refuse an A5-skipped body whose A2 table is
    exact. That is a valid program the old path accepts, refused.
  - A code list in Core would also be a second copy of who reports what.
  - Without the fact, a runtime `Group == -1` cannot be told apart as an arena skip or a broken contract (`M/Analysis/MirDrops.cs:88`).
    Treating every one as a refusal would hide an internal fault.
- **Consequence.** Ask A-2 is required (§6.3). Its codegen use ends at H5; after that the fact backs only the `Group == -1` invariant.

### 3.9 Coverage rows (PD11; C0b1 … X2)

- **Rows.** D2 §2.12: the rows are FunctionCodegen's dispatch arms and table rows plus the mono-item kinds. An unwritten one calls
  `Uncovered(SourceInfo)`, which records `Unsupported_Kd` once per location, under sel only.
- **Reporting.** Generation runs on to report every gap (SPEC 23.1.2), then discards the text.
- **Metric and removal.** M2 is counted from the same tables; there is no second list. At X2 the remaining rows, all decided D2
  rows, move into K's `Capability` and `Uncovered` is deleted.
- **No-op rows (S1 UNOWNED 2).** C0b1 lists `StorageLive`, `StorageDead`, `FakeRead` and `EndBorrow` (`M/MirVocabulary.cs:18-21`) as
  written rows that emit nothing. Flag changes at their locations come from A2's `MirFlagPoint`s (I6), not from the statement.

### 3.10 Successor of `GenerationFailed_Kd` (G9b)

D2 §2.14's cause table is adopted. Codegen, Layout and Abi contract failures become `InternalInvariant_Kd`: the 446 `Fail(` strings
(`tests/xUnitTest/Tests/ArchitectureRulesTest.cs:16`) go with G7/G8. Publication failures and the publication-only configuration and
supply checks (`E/EmissionArtifacts.cs:34-185`) get one code, and the repeated `NativeConfiguration.Validate` (`:98`, already run
at preparation through `src/Kimi/SolutionAndProject/DependencyConfiguration.cs:77`) is deleted.

Options for the publication code (verified against `src/Kimi/Diagnostics/DiagnosticCode.tinyhand:770-860`):

| Option | For | Against |
| --- | --- | --- |
| **(a) New `ArtifactPublicationFailed_Kd` (Input)** — recommended | Names the owning operation (`EmissionArtifacts.Publish`); `GenerationFailed_Kd` deleted as PLAN10 §5.7 says; net 0 codes | A new code (owner item) |
| (b) Reuse `ProjectPreparationFailed_Kd` (Input, free-text note) | No new code | Misnames output I/O as "preparation". Moving the configuration checks into preparation would change `check` outcomes (Blocked), a separate behavior change |
| (c) Keep `GenerationFailed_Kd`, recategorized Internal → Input | No new code | Departs from PLAN10 §5.7's deletion; "generation" misnames publication; a published code changes category |

`DiagnosticPartition.Emission` (`src/Kimi/Diagnostics/DiagnosticPartition.cs:28-29`) and the `Emission` requirement
(`DiagnosticRequirement.cs:37`) remain. A rename is left to R9 with the toolchain move. Toolchain failures stay plain text (an open
issue, R9).

### 3.11 Debug-information record (PD24)

D2 §7 is adopted as the record. No DI is generated in R5. What B already provides:

- I2 gives every named local a slot, so `DebugInfo=true` changes no slot code.
- `SourceInfo.StatementStart` exists (`M/MirDump.cs:141`), but control headers, match arms and `=>` lack it. The builder sets them
  in KD2's first unit.
- `MirScope` (`M/MirBody.cs:186`) gives scopes.
- ModuleCodegen owns debug names, unique per module.
- The runtime components list their artificial functions.
- Variable states need an A1 fact (KD7).
- This replaces the Debug Adapter plan's §6.1 placement in `EmissionModule`.

### 3.12 Memory and time

D2 §2.15: memoized `Layout` and `Abi`, a pooled text buffer and per-function scratch, 0 B warm (186 sites, PD13); codegen time at
most legacy `EmissionLowering + LlvmIrWriting` (recorded by E14/X1, not gated). O0 spills cost one store and one load per scalar
argument (recorded).

---

## 4. O2 parity list

- **Rows.** D2 §3 is adopted (the source is `docs/dev/LLVM_OPTIMIZATION.md` §2, rows 実装あり/限定実装):
  - `align` on alloca, load, store and globals, and on aggregate `memcpy`.
  - `noundef nonnull align 8 dereferenceable(24)` on `SharedReference` parameters at definitions and at every call (§2 row 14).
  - `noalias nocapture` only on the `memcpy`/`memmove` declarations.
  - `noreturn` from `Abi`, plus `unreachable` after diverging calls.
  - `unnamed_addr` on pooled constants and folded statics.
  - The `#0` group.
  - Bool stored as `i8`.
  - The per-check failure block.
  - `memcpy` for `transferBytes`; `memmove` for Array insert, remove and window advance.
- **Added from D1.** The `poison` start value of formatting aggregates (F02), inside the promoted formatting kernels.
- **Never written** (I9): `mustprogress`, `willreturn` (D03/D04, IMPL 21.3.1), `nounwind`, `nsw`/`nuw` on checked operations,
  `inbounds`, `lifetime.*`, `range`, TBAA.
  - The 13 test files asserting `mustprogress`/`willreturn` absent are class R. E15 carries them pipeline-neutrally.
  - `nsw`/`inbounds` absence is class I (R5-M12).
- **Gate.** No new optimization attribute in R5. Gains belong to the LLVM_OPTIMIZATION track after R6. E13 and the corpus A/B are the
  gate (R5-M8).

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
  ConditionalBranch and DestroyAggregate at subslots); case views (`Adapt`, `PairLayer`, `PairAcquire`, resolved before lowering).

### 5.2 Criteria

| Criterion | A | B | Evidence |
| --- | --- | --- | --- |
| Run time (O2) | Same instruction text if the kernels are shared | Same | PD2; P4a/P4b kernels shared by both |
| Compile time | One more record pass | Direct | Writer stage 0.016–0.066 ms = 0.04–0.23 % of 22–43 ms; EmissionLowering 0.4–8.4 % (`artifacts/benchmarks/r3-u16c-v14/exit-compiler-pipeline-1/phases.json`, re-read for this package). Neither difference is measurable |
| Representation | 19-field record with 4 string fields; `Operation` = old ownership id (54 writer reads); arithmetic keyed by `KotoKind` | MIR forms directly; operator table by `MirBinaryOp` (needed by both) | `E/EmissionModule.cs:141`; `grep -c instruction.Operation E/LlvmModuleWriter*.cs` = 54; `E/NumericArithmetic.cs:45-61` |
| Freeze (D5) | Redefines `Operation` as a per-function value id: a contract change of frozen code | Touches old code only through bp promotions | ARCH:172 |
| Retained code | `EmissionModule` 433 + `LlvmModuleWriter*` 4,601 past R6-gen. In the Backend (no baseline) about 9 members over 60 statements (D1 §2.5 proxy, H) and `Complete(out string?)` must be fixed; or a second rewrite (B) later | G8 deletes both | `wc -l`; ARCH "Structure limits" |
| New lines in R5 (H) | Lowering ≈2,500–3,500 + 500–800 for missing forms | ≈4,100 + ≈3,450 promoted | D1 §2.6; D2 §10 |
| Risk | MIR semantics forced into writers that assume `BodyLowering`'s invariants (live flags, operation ids) | New text, so new bugs are possible; gated by native behavior, C4s and E13 | — |
| Where A is better | First scalar slices reuse ≈2,300 tested serializer lines; `TryPrepare` structure tests keep working (but they are class S and deleted at G5, PD14); the DI plan's placement fits | B's first slice is the frame (C0b1) | — |
| Owner texts | Departs from PLAN10 §5.7 ("物理 IR 層は置かない"), the ARCH target pipeline and the R0 ledger, unless a removal stage is named | As approved | — |

**Verdict: B.** A's material advantage, reuse of module-level and kernel text, is captured by PD2. Its costs are permanent or force a
second rewrite. B's first slice is measured against legacy (E13 at C1a, R5-M3).

---

## 6. Interfaces

### 6.1 Chapter K (S2)

| Direction | Item | Status against K-design-2 / q3 |
| --- | --- | --- |
| C provides | `Layout.Of` typed limit results (P7c) for K10a | matches |
| C provides | DropGlue item shape: recursive per Type, targets = direct parts (§3.6) | **mismatch**: K-design-2 §2.4 gives a glue one target per leaf part of `MirTypes.Destruction` (O3) |
| C provides | Legacy rows declare their library callees (F2c) and type-driven needs | **mismatch**: K-design-2 keeps its own needs table in `InstanceCollector.Items.cs` (O6) |
| C provides | Entry and TestEntry written around Generated roots; decided D2 rows into `Capability` at X2 | matches (K-D6, 2.9) |
| C needs | Items in Generated order with kind, body ordinal, representative Subst or Type, Generated mark; a request outside it is `InternalInvariant_Kd` | matches (K-D2, K-D8) |
| C needs | Targets `(Item, At, Runtime)` in `MirLocation` order for Fn, Requirement, Value, `staticEnsure`, Drop, object intrinsics, `Cast(Erasure)`, `Aggregate(Closure)`, `Const(FnItem)`; none for Intrinsic, Virtual or `#LibraryImport` calls | matches (K-design-2 §2.4); D2's `(Block, Target)` binary search is dropped |
| C needs | The receiver adjustment (`BaseDepth`) on a requirement target | **missing** in K's `Target` (O5) |
| C needs | One instance-type function | **mismatch**: K-design-2 has codegen compose `Erase(Apply(t, rep))` (O4) |
| C needs | Builtin shim targets in `TyCtxt.Comparison`'s leaf pre-order (C8f) | to state in K6 |
| C needs | A view index | **not needed**: codegen calls `MirCases.Views` (§2 row 6). q3's column is unnecessary |

### 6.2 Chapter F (S3)

| Needed | From | Consumer |
| --- | --- | --- |
| `Compilation.Locations.Text(SourceRef)` | P5 (F-design-2 §3.10) | C1a+. Which site an Abort reports is C's rule (`LocationOf`, I5), as F-design-2 states |
| `AdtDef.IsCLayout` | F1 | P7b |
| Startup symbol and module | F2a | C0b2 |
| Import symbol column | F2b | P8 |
| Dictionary-storage symbols | F2c, via the legacy rows' callees | C8e |
| `VTableDef` slot order | F4 | C6c |
| `TyCtxt.Witness` incl. `BaseDepth`, via K's target | F5b | C3b |
| `TyCtxt.Comparison` tree | F6 | C8f |
| `TestRecordValue`, `$abort` SourceInfo, `MirProgram.StaticInit` | F8, F9, F10 | C10a, C3b, K7 |
| Test-case symbol column | P6 (landed) | C10a; its open finding (count check) lands with C10a |

There is no mismatch. F-design-2's risk 9 (inherited tuple-leaf witnesses) becomes an (a) deviation if a fixture shows it.

### 6.3 Asks to the R4 stream

| Ask | Change |
| --- | --- |
| **A-2** (Optional → **Required before C4a**) | `MirDropTable.Incomplete(body) → (DiagnosticCode, SourceRef)?`: the first problem A1 adds for the body (`MirInit.Transfer.Add`, `M/Analysis/MirInit.cs:264`), A1's arena record (`:58`) or A2's `MovedPlace_Kd` (`M/Analysis/MirDrops.cs:293`), closed with the body (`MirDropTable.Close`). ≈ +20 lines in R4-held files, best in the A-9 merge. R4 writes its DEC entry. Per-view precision is not needed |
| A-9 | Unchanged (before K7, C4a) |

---

## 7. Unit mapping (changes to plan §4 only)

| Unit | Change | Review |
| --- | --- | --- |
| P4a | Wording: the per-check failure writer (not a shared block) | R |
| P7a | Also records the tagged handle and borrow `Option` layout in STATUS's "Option and Result" row (the existing limit; PLAN U8) | D+R (S4) |
| P8 | `PreparedSlot` not ported; no gate in `Abi` | D+R (S4) |
| E12 | One `CodegenDeviations` catalog (D2 §2.13) | with its first consumer |
| E15 | Carries the class-R absent-attribute requirements (§4) | G:C-frame |
| C0a | Adds `Compilation.Generate`, typed records, the transitional out-string adapters and the `kimi test` record path | G:C-frame |
| C0b1 | Adds the four no-op statement rows (S1 UNOWNED 2) and the zero-sized address symbol | G:C-frame |
| C0b2 | Entry mono item (PD3; not "`Shim(Entry)`"); `writeLine(string)` only | G:C-frame |
| C3a | Reads K's targets by cursor; "prepared slots" dropped | G:call |
| C3b | Requirement targets with `BaseDepth` projection (O5) | G:call |
| C4a | DropGlue recursive per Type (O3); gate step 2 needs A-2 | G:C-drop |
| C5a | ABI attributes on value calls through the one writer | G:closure |
| **C7a (new)** | `KIMI_E_STATIC_CYCLE` row; the declared legacy-text change; evidence: E4's report plus a throwaway check that every changed fixture differs only by the table hunk and count (O7). ≈ +3 code | R |
| C7b | C7 without the row; per-slot ensure function; fold; D2 static rows; the cycle test through E12 (`static-cycle`, class (a)) | D+R (S4) |
| C8a | `transferBytes` → `memcpy`; the `MoveBytes` comment corrected (`Bd/KimiLibraryKinds.cs:116`) | G:library-1 |
| C9a | 26 rows (+ `WriteLineUtf8`) | G:format |
| G9 → **G9a**, **G9b** | G9a: Emission-only Binding members (as plan). G9b: §3.10, the repeated validation and the out-string adapters deleted, DIAGNOSTICS §4.2 row, ratchet 446 → 0 | G:R6-gen-del |

Other C, P and E units, and all review groups, are as the plan states. Order: C7a may land any time after P0b. C7b needs C3a, K7 and
E12.

---

## 8. Departures from owner-approved texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| PLAN10 §5.4/§5.7, A5P §9.1 | PD3: no `MirBodyKind.Shim`; shims are Backend mono items | Already highlighted (R5-M1) |
| ARCH:45-47 | PD11: codegen reports `Unsupported_Kd` under sel until X2 | Already highlighted (R5-M1) |
| Plan §3.4 A-2 "(Optional)"; PD12's mechanism | A-2 required; the gate reads A2's per-body fact | §3.8: codes cannot be attributed to bodies, and A5 shares the arena code |
| Plan PD23 (`memmove`) | `memcpy` | PD23's premise ("ranges may overlap") is the intrinsic comment. The library declaration says the opposite, and the O2 constraint decides |
| R5-M13(b) "P42 needs no representation change" | True for the slot state only; generic storage keys need `MirStatic` to carry the container's Type arguments | §3.7, verified |
| PLAN10 §5.7 (`GenerationFailed_Kd` deleted) | Deleted, plus one new Input code for publication failures | A cause PLAN10 did not cover (§3.10) |
| Kimi Debug Adapter Plan §6.1 | DI from FunctionCodegen, not `EmissionModule` | PD24 (adopted) |
| Plan wording (PD8 "shared failure block", C0b2 "`Shim(Entry)`", C3a "prepared slots") | Corrected | Facts (§2 rows 12, 16; PD3) |

---

## 9. Owner items

1. **`ArtifactPublicationFailed_Kd`**, a new Input diagnostic code (net 0 with `GenerationFailed_Kd` deleted), or option (b) or (c)
   of §3.10.
2. Keep `DiagnosticPartition.Emission` and the `Emission` requirement name through R6; rename at R9 or never.
3. Acknowledge §8's departures, especially PD23's reversal and the R5-M13(b) fact. No new MIR vocabulary entry, `IntrinsicId`,
   `CompilerFunctionKind` or `BindingFailure` (PD21). The reason row is runtime data, already declared in plan §5.3 item 7.

---

## 10. What becomes unnecessary; size and speed

- **R5.** No `EmissionModule` pass under sel. No second copy of checked-arithmetic, helper, ABI or layout rules (PD2). P units move
  ≈ −3,150 from `E/` and ≈ +3,450 into `B/` (plan §6.1).
- **G7/G8.** `BodyLowering*`, `LlvmEmitter*`, the plans, `EmissionModule` (433), the rest of `LlvmModuleWriter*` (4,601 before
  promotion), the destruction half of `AggregateLayout` and the `FunctionKoto` keying and `Supports` gate of `FunctionAbiPool`.
- **G9b.** `GenerationFailed_Kd`, 446 `Fail(` strings, the repeated `Validate` and the adapters.
- **X2/G4.** `Uncovered` (≈ −50); the selector, ledger, catalog and `.deviation` (≈ −400).
- **New.** ≈ +4,100 (D2 §10, H) against the plan's ≈ +4,200. No total in plan §6.1 changes beyond ±100.
- **Speed.** Compile time at most legacy's generation stages, recorded. Generated code is O2-parity by §4, measured by E13 and the
  corpus. The O0 cost is recorded.

---

## 11. Risks

| # | Risk | Response |
| --- | --- | --- |
| R1 | R4 declines or delays A-2 | C4a waits; the drop chain already waits for the MIR-lane merge and A-9 (O1) |
| R2 | K keeps leaf-flattened glue targets or codegen-composed Types | Settle O3 and O4 in the S2–S4 design review; otherwise C4a and C2b carry the mismatch |
| R3 | Recursive glue costs calls at O0 | Legacy has the same shape; O2 inlines (H). O0 times are recorded |
| R4 | C7a touches every legacy `.ll`; E4 reports a mass difference | The throwaway hunk check in C7a's evidence (O7) |
| R5 | The static fold misses a form legacy folds (H: a parenthesized literal), so a static read regresses at O2 | C7b's test over `StaticScalar.TryGet`'s set (§3.7) |
| R6 | Attributes on value calls change O2 output | Information under PD9; the corpus A/B gates; strict parity stays possible (O8) |
| R7 | Static D2 rows are untested until H5 (unreachable under sel) | Accepted; X2 puts them in `Capability`, which H5 exercises |
| R8 | The tagged `Option<ref/T>` persists to U8 | STATUS row (P7a); one Layout row and one Abi row after G8 |
| R9 | Thread-safe static initialization is not designed (no offload yet, H) | Open issue; the `i8` state admits `cmpxchg` later |
| R10 | Three inline depth bounds (walk 32, storage 64, layout 64) | K's R6 open issue, decided with R4 and P7c |

---

## 12. Open points for the planning session

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| O1 | Send A-2 as required now? If R4 declines? | (a) required; C4a waits; (b) `[Transitional]` program-wide code list (over-refuses A5-skipped bodies; a second copy of reporters) | (a) |
| O2 | Successor code for publication failures | (a) new `ArtifactPublicationFailed_Kd`; (b) reuse `ProjectPreparationFailed_Kd`; (c) recategorize `GenerationFailed_Kd` | (a), to the owner |
| O3 | DropGlue shape (with K) | (a) recursive per Type, direct-part targets; (b) leaf targets of the destruction walk | (a): SPEC order per level; legacy shape |
| O4 | Owner of the instance-type function (with K) | (a) the collector publishes `TypeOf(item, t)`; (b) codegen and collector each compose `Erase(Apply(t, rep))` | (a): one rule |
| O5 | Requirement receiver adjustment (with K, F) | (a) K's target carries `BaseDepth`; (b) codegen calls `TyCtxt.Witness` again | (a) |
| O6 | Owner of legacy rows' callees and needs (with K) | (a) the `LegacyRuntimeHelpers` row declares, the collector reads; (b) a collector table | (a) |
| O7 | C7a's evidence | (a) E4 report plus a throwaway hunk check; (b) an E4 allowed-hunk option | (a): a one-off needs no tool mechanism |
| O8 | ABI attributes on value calls | (a) one writer, attributes everywhere; (b) strict legacy parity | (a) |
| O9 | STATUS gap for the in-memory nonnull Option | (a) P7a records it; (b) S5 records it | (a) |
