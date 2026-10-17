# R5 step 0, chapter C (S4): code generation, Layout/Abi, DropGlue, selector, coverage rows, debug-info record — design 2

Status: design (second independent design of S4; written without reading any other design of this chapter or the deep dives).
Base: `origin/dev` at `041ced79`. Plan: [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md)
("the plan"), adopted 2026-10-18 (owner answers OQ1–OQ17; DEC R5-M1–R5-M13 in the records unit). PLAN10, A5P, ARCH, DEC, PD/OQ
and unit IDs are used as in the plan. `B/` = `src/Kimi/Compiler/Backend/`, `E/` = `src/Kimi/Compiler/Emission/`, `M/` =
`src/Kimi/Compiler/Mir/`. Citations are `path:line` at the base; (H) marks an estimate or an unverified inference. `audit.md` (S1)
did not exist on `origin/dev` or a pushed S1 branch when this was written.

---

## 0. Decisions in brief

1. **Design B** (PD1): `FunctionCodegen` writes LLVM text from validated MIR, one instance at a time; no physical IR. Design A
   is judged on paper in §4: it keeps two lowering stages and the old IR's concepts alive for no gain that PD2 does not already
   give B.
2. **Promote, don't copy** (PD2), with one rule: a promoted member takes generator-neutral inputs (LLVM type text or
   `TypeLayout`, SSA names, labels, a writer), never `EmissionModule`, `AggregateLayout` or MIR types. Decoding stays per generator.
3. **The FunctionCodegen contract** (§2.2) fixes inputs, outputs, eleven invariants and the prohibitions. Codegen trusts
   validated MIR and the analyses' published tables, and re-derives nothing.
4. **Layout and Abi are shared with the legacy path** (P7, P8). So R5 keeps every legacy layout rule (bp), including the
   tagged `Option<ref/T>`. IMPL 21.1.3's nonnull Option stays a STATUS limit until after G8 (§11, RK5).
5. **PD12's gate reads one published fact, not codes.** A2's table says per body whether it is exact, and if not, the A1/A2
   problem that made it inexact (ask A-2 becomes *required* before C4a). Codegen never scans problem codes.
6. **PD23 is reversed:** `transferBytes` is lowered with `memcpy`. The library declaration's `- safety:` clause says the
   ranges do not overlap (`src/Kimi/Library/StorageOperations.kimi:71-75`). The `IntrinsicId.MoveBytes` comment that says they may
   overlap (`src/Kimi/Compiler/Binding/KimiLibraryKinds.cs:116`) is wrong and is corrected in C8a.
7. **Statics** (PD19, OQ17(b)): one `i8` state per slot, encoded 0/1/2/3 for SPEC 22.4.3's four states. The cycle Abort uses
   `KIMI_E_STATIC_CYCLE` at the `staticEnsure` call's SourceInfo, which is the accessing Place. A StaticInit body that is only
   constant assignments folds to a constant global with no state. C7 splits off C7a, the reason row, because that row changes
   the text of every legacy `.ll`.
8. **Coverage rows** (PD11) are FunctionCodegen's own dispatch tables. A missing row is a located `Unsupported_Kd`, reported
   under sel only. There is no separate gate and no partial module. X2 moves the remaining rows into the capability table.
9. **Successor of `GenerationFailed_Kd`** (G9): codegen contract failures become `InternalInvariant_Kd` (PLAN10 §5.7). Output
   I/O and the publication-time native configuration and supply checks get one Input code, `ArtifactPublicationFailed_Kd`. The
   `NativeConfiguration.Validate` call that publication repeats from preparation is deleted. `DiagnosticPartition.Emission` and
   the `Emission` requirement name stay.
10. **Debug information** (PD24): a design record only (§7). B already gives every named local a slot, so `DebugInfo=true`
   would change no slot code. The DI plan's `EmissionModule` placement moves to FunctionCodegen.

---

## 1. Required meaning

| Rule | What generation must do |
| --- | --- |
| IMPL 21.3.1 ¶1 | Monomorphize after §8.13 selection: one body per closed instantiation that the output requires. Checks cover the whole checked set (the collector's job, S2; codegen reads only Generated instances) |
| IMPL 21.3.1 ¶2 | The internal ABI, names and attributes are implementation-defined. O0/O2 never change acceptance, results, effects, Abort codes or locations, cleanup or nontermination. So: no `mustprogress`/`willreturn`; `noreturn` only on divergence |
| IMPL 21.1.1–21.1.3 | Size/alignment/stride; the zero-sized rules (a borrow of a zero-sized value is a nonnull aligned address; distinct zero-sized Places may share one); C mode in written order at `/Zp16`; the scalar table; 16-byte function values with inline environments of 8 bytes or less; the nonnull Option |
| IMPL 21.2 | `#LibraryImport`: `ccc`, no `signext`/`zeroext`/`byval`/`sret`; Unit result is `void` |
| IMPL 21.3.2 | A limit is a Resource diagnostic at the affected declaration, call or token (collector and layout, S2; not codegen) |
| IMPL 21.3.3 | Checked operations Abort with SPEC 22.5.2 codes. A check is omitted only when proven (`MirRvalue.Proven`). i128 `/`, `%` and float conversions are Unsupported (capability check, S2) |
| IMPL 21.4.2–21.4.3, 21.4.7 | Application: `__kimi_start` → body → `ExitProcess(0)`. Library: no entry. The Abort report format. 16-byte heap alignment. `_fltused` in every module |
| SPEC 14.10.3, PLAN10 §5.6.3 | Code reached only through `CheckTarget` is checked, never generated |
| SPEC 16.3.2–16.3.3 | A layer runs its `drop`, then its own Fields in reverse, then its base. Tuple and array elements in decreasing index. An enum destroys the active Case's payloads in reverse. Handle release as specified |
| SPEC 22.4.3–22.4.4 | Four slot states; initialize on the first executed access; the cycle and shutdown codes; one slot per static storage key |
| SPEC 22.5.1–22.5.2 | One Abort location per failure. Inside a Kimi operation it is the invoking user expression. A static Abort reports the Place with its qualifier. The closed code table |
| SPEC 23.1.1, 23.2.2 | `emit`/`build` of an accepted program never fail with a Language or Proof error; they can still fail on a Resource limit, Unsupported, native-library resolution, toolchain, output or launch failure (`docs/spec/23-compiler-services.md:72`). Internal means a compiler defect |
| ARCH "Dependency rules" | The Backend reads MIR, declaration tables and `TyCtxt`; never HIR, borrow-checker state or syntax |
| AGENTS, R5-M8, PLAN10 §18 P1/P2 | O2 run time and native allocation counts never regress; warm generation allocates 0 B (186 sites, PD13) |

---

## 2. Components and contracts

### 2.1 Overview

| Component | Phase | Files | Lands in | Lifetime |
| --- | --- | --- | --- | --- |
| `LlvmModuleText`, `LlvmConstantPool` | P12 | `B/LlvmModuleText.cs`, `B/LlvmConstantPool.cs` | P0 | permanent |
| Runtime ABI table, abort reasons, ABI/layout records (`FunctionAbi`, `AbiParameter`, `TypeLayout`, `ValueLowering`) | P12 | `B/RuntimeAbi.cs`, `B/AbortReasons.cs`, `B/AbiRecords.cs` | P0b | permanent |
| `LegacyRuntimeHelpers` | P12 | `B/LegacyRuntimeHelpers*.cs` | P1a–P2b | `[Transitional("R8")]` |
| `ObjectRuntime` | P12 | `B/ObjectRuntime*.cs` | P3b | permanent (PLAN10 §5.8) |
| `LlvmScalarText` | P12 | `B/LlvmScalarText.cs` | P4a, P4b | permanent |
| `Layout` | P12 | `B/Layout.cs` (+ `.Aggregates.cs` if needed) | P7a–P7c | permanent |
| `Abi` | P12 | `B/Abi.cs` | P8 | permanent |
| `ModuleCodegen`, `FunctionCodegen` | P12 | `B/Codegen/ModuleCodegen.cs`, `B/Codegen/FunctionCodegen.cs` (contract entry) + `FunctionCodegen.<Concern>.cs` | C0b1+ | permanent |
| `DropGlue`, `ObjectDescriptors` (with VTables), `Statics`, `Shims` | P12 | `B/Codegen/DropGlue.cs`, `ObjectDescriptors.cs`, `Statics.cs`, `Shims.cs` | C4a, C6a, C7b, C5b/C8f/C9b | permanent |
| Codegen selector, generation entry and gate | Core | `Core/Compilation.Pipeline.cs` (after G1, `Compilation.Generation.cs`) | C0a, C4a | selector `[Transitional("R6-gen")]`; gate's refusal path until H5 |
| Deviation form, pending ledger | tests, scripts | `tests/xUnitTest/Codegen*.cs`, `scripts/codegen-pending.ps1` | E10–E12 | removed at G4 |

The collector (`B/Instances/`), `Capability` and the Generated column are chapter K (S2). §2.4 states only what codegen needs
from them.

### 2.2 FunctionCodegen (contract entry of `B/Codegen/FunctionCodegen.cs`)

**Responsibility.** It writes the LLVM definition of one Generated Fn instance from its validated, analyzed MIR body, under the
instance's Subst and Semantics view. It also provides the primitives that every Backend mono item writes its body through
(slot, load, store, address, call, switch, branch, check, abort). So one ABI writer, one attribute writer and one location rule
serve all functions.

**Inputs** (read-only during one generation):

| # | Input | Source |
| --- | --- | --- |
| 1 | The accepted program version: `MirProgram.Bodies`, `MirProgram.Drops` (`MirDropTable`), with no construction records or validator findings | `M/MirProgram.cs:40-51`; the gate (§2.11) |
| 2 | The instance row: body ordinal, representative (unerased) Subst, view index, symbol ordinal, Generated mark; its callee table | collector (§2.4) |
| 3 | Types: `TyCtxt.Apply`, the collector's instance-type function, `TyCtxt.Signature`; `MirCases` with the instance view selected (`Type`, `Layer`, `Reborrows`, `Adapt`) | `src/Kimi/Compiler/Binding/TyCtxt.cs:58, 235`; `M/MirCases.cs:43, 114-156` |
| 4 | The runtime-reached blocks: `MirReach.Walk(body, 0, checks: false)` | `M/MirReach.cs:48` |
| 5 | `Layout.Of`, `Abi.Of` (memoized and pure) | §2.5, §2.6 |
| 6 | Abort location text by `SourceRef` (Core, P5); `KimiLibraryCatalog.RequiresCallerLocation` | P5; declaration table |
| 7 | Module request tables (constants, runtime helper rows, declarations), owned by ModuleCodegen | §2.3 |

**Outputs.** (1) Text appended to ModuleCodegen's pooled buffer: one `define internal <ret> @__kimi_f<N>(…) #0 { … }`, or
`@__kimi_entry_body` for the startup body. (2) Requests registered with ModuleCodegen: constants, `LegacyRuntimeHelpers` rows,
`LlvmModuleText` declarations. (3) Typed records: `Unsupported_Kd` per uncovered-row location (sel, until X2), and
`InternalInvariant_Kd` at the subject of a broken input contract, after which the module is not published (no recovery,
AGENTS).

**Invariants.**

| # | Invariant |
| --- | --- |
| I1 | Determinism: the same inputs give byte-identical text. SSA names `%t<n>` use a per-function counter, block labels are `b<MIR index>`, and constants are numbered in first-use order |
| I2 | Frame: one `alloca` per non-zero-sized local in the entry block, aligned by `Layout`. Bool and drop-flag slots are `i8`, and flags are cleared at entry. A zero-sized local has no slot; its address is one module-level 16-aligned symbol (IMPL 21.1.1 allows shared addresses). By-value scalar arguments are stored into their slot. A pointer-passed argument's local *is* the incoming pointer (no copy). Local 0 is the hidden result pointer when `Abi` has a result slot |
| I3 | Blocks: exactly the blocks of input 4 (runtime-reached from block 0), in index order. `CheckTarget` edges are never followed |
| I4 | Values: an operand is loaded from its slot at each use (no cross-statement value cache); mem2reg/SROA promote |
| I5 | Checks: each `BinaryOp`, `UnaryOp`, `Cast` and `Index` check that is not `Proven` is written through `LlvmScalarText`. Its location comes from the one rule `LocationOf(SourceInfo)`: the statement's syntax, or the hidden caller-location parameter inside a body that `RequiresCallerLocation` (SPEC 22.5.1) |
| I6 | Drops: a runtime Drop does exactly what `MirDropTable.Group(body, block, view)` says (`M/Analysis/MirDrops.cs:88-108`). Flags change exactly at the view's `MirFlagPoint`s: after a statement, or after a terminator's own effect and before its branch |
| I7 | Types: every Type in the text is `Layout.Of(instanceType(T))`. No layout or ABI rule is computed anywhere else |
| I8 | Calls: every callee is the collector's target for that block. A target-less Call is followed by `unreachable` |
| I9 | Attributes: only the rows of §3. Never `mustprogress`, `willreturn`, `nounwind`, `nsw`/`nuw` on checked arithmetic, or `inbounds` |
| I10 | Text rules of PD8: bool is never `store i1`; `select i1` appears only in `LlvmScalarText`'s failure block |
| I11 | Allocation: regenerating the same program warm allocates 0 B. All storage is pooled per `Compilation` |

**Prohibitions.** It reads no syntax, HIR, plans, Binding internals or old Analysis/Emission state. It re-derives no acceptance
fact: initialization, drop verdicts, witnesses, applicability or case selection. No `Supports*`, no `out string` failure, no
LINQ, no catch-all, no member over 60 statements. No `Unsupported_Kd` except PD11's rows under sel until X2. It never runs on a
program that the gate refused. An `Opaque` rvalue, a runtime Drop with `Group == -1` in an exact body, a callee outside
Generated and a layout error for an admitted instance are each `InternalInvariant_Kd`.

**Dispatch.** One switch per closed MIR vocabulary (5 statements, 10 rvalues, 6 terminators, 7 projections, 4 operands, 5
callees). For intrinsics and compiler functions there is one row table: `IntrinsicId` (33 rows) and the 53 Body-disposition
`CompilerFunctionKind` legacy rows (`IntrinsicDisposition.Of`, `src/Kimi/Compiler/Binding/KimiLibraryKinds.cs:183-219`). A row is
`(writer kind, data)`, and the default row is the coverage gap (§2.12). An `Adapt` is written as the rvalue that
`MirCases.Adapt` returns under the instance view (`M/MirCases.cs:142-156`). A `PairLayer` step uses `MirCases.Layer`, and a
`PairAcquire` uses `MirCases.Reborrows`.

### 2.3 ModuleCodegen (`B/Codegen/ModuleCodegen.cs`)

**Responsibility.** It assembles one module from the Generated set. It names every mono item, runs FunctionCodegen for each Fn
instance and the mono-item writers (DropGlue, ObjectDescriptors/VTables, Statics, Shims, Entry, TestEntry), and writes the module
through `LlvmModuleText` in legacy order: header, constants, runtime, externals, statics, declarations, requested helpers,
functions, objects, footer (`E/LlvmModuleWriter.cs:62-125`).

**Inputs:** the Generated set and callee tables (K11); the output kind (Application, Library, test build); the startup symbol
column (F2a); the test-case symbol column (P6).

**Outputs:** text written to the caller's `TextWriter`, and the typed records of §2.14.

**Invariants:** the entry form follows IMPL 21.4.2 (`__kimi_start` exported only for an Application; a test build's
TestEntry; every function `internal` in a Library). Names: `__kimi_f<N>` for Fn instances in Generated order; artificial items
use their kind's prefix (`__kimi_drop<N>`, `__kimi_shim<N>`, descriptors and VTables as `ObjectRuntime` names them). The text is
deterministic (I1). The function text is first buffered, so constants and helper requests are known before the header is written:
two phases over one pooled buffer, never two passes over MIR.

**Prohibitions:** it does not order or name items any other way than Generated order, and it does not decide what is generated
(K11 does).

### 2.4 What codegen needs from the collector (the S2 boundary)

- Per instance: the body ordinal, the representative Subst, the **view index** (the single bit of
  `MirCases.Views(body, subst)` for a closed Subst, `M/MirCases.cs:84-111`; more than one bit is `InternalInvariant_Kd`) and the
  symbol ordinal.
- Per instance, the **callee table**: `(Block, Target)` rows ordered by block, one per runtime Call. The target is an
  instance, an intrinsic row, a legacy helper row, a shim item or a virtual slot; a Drop's glue is a target too. Codegen reads
  them by binary search, as `MirDropTable.Group` does. It never resolves a requirement, item or applicability itself.
- The **instance-type function** `instanceType(T) = TyCtxt.Erase(TyCtxt.Apply(MirCases.Type(T), subst))`. It has one owner
  (the collector, whose layout obligations K10a and glue edges K7 need it first). Codegen calls it.
- The Generated mark. A request outside it is `InternalInvariant_Kd` (K11's negative test).

If S2 chooses another shape for these, FunctionCodegen's input 2 changes with it. The rule stays: one resolution, owned by the
collector.

### 2.5 `Layout.Of(erased Type)` (P7a–P7c)

- **Signature:** `Layout.Of(BoundType erased)` → a typed result: the `ValueLowering` (its `TypeLayout`: storage type, size,
  alignment, stride, field offsets by logical field index), or the exceeded limit kind (Size, Count, Depth; P7c). It is pure and
  memoized by interned-Type reference. It is cleared when the Type interning table is reset, and warm hits allocate 0 B.
- **Rules ported verbatim (bp):**
  - Scalars per IMPL 21.1.3 and `E/WindowsLowering.cs:43-46`; bool is `i8` storage with `i1` computation (`:108`); string is
    `%kimi.string {ptr, i64, i8}`.
  - Handles are 8 bytes and function values 16 (`E/AggregateLayout.cs:140, 211, 234`).
  - Managed sequences: Array 3 words, Slice 2, Dictionary 7 (`:273-297`), as constants marked `[R8]` (PLAN10 §5.7).
  - Kimigayo mode uses alignment-descending groups, written order within each group, and the base as a prefix
    (`:382-384, 408-431`). C mode uses written order (`:390-406`).
  - An enum is an `i32` tag at offset 0 with the payload after it (`:476-537`).
  - Zero-sized parts are kept. The limits stay depth 64 (`:47`) and 2^31−1 bytes (`:171-176`).
- **Not in Layout:** destruction. `AggregateLayout`'s callbacks (`CollectionDrop`, `InstantiateDestructor`,
  `IsSealedObjectTarget`, `:66-79`) stay in the old pool until G8; DropGlue (§2.7) replaces them (PLAN10 §5.7: 破棄の方法は
  DropGlue に分ける).
- **The open-struct re-check** (`:300`) is deleted in P7b. Binding rejects C layout on open structs.
- **Shared, so no divergence.** The old pool calls `Layout.Of` (P7a/P7b), so any rule change would change legacy layouts too.
  IMPL 21.1.3's nonnull `Option<ref/T>` is not laid out today (`src/Kimi/Compiler/Binding/Binding.Attributes.cs:318`; STATUS row
  "Raw pointers and FFI", `docs/STATUS.md:80`). It stays that way in R5, and after G8 it becomes one Layout row plus one Abi
  import row (an open issue, not R5 scope).

### 2.6 `Abi.Of(signature instance)` (P8)

- **Input:** the erased instance parameter Types in MIR slot order (receiver first, SPEC 7.3; `M/MirBody.cs:112-132`), the
  result Type, the flags (closure body with erased or concrete environment, constructor or destructor result slot, caller
  location, Never), and the convention (Internal, or C for `#LibraryImport`).
- **Output:** a `FunctionAbi` without a name. ModuleCodegen names it; legacy embeds `__kimi_f<ordinal>`
  (`E/FunctionAbiPool.cs:156`).
- **Memo:** keyed by `(convention, flags, interned signature Type)`.
- **Rules:** `FunctionAbiPool.Get`'s pure core (`E/FunctionAbiPool.cs:103-160`):
  - Order: `[environment] [ret] a0…an [context] [location, location_length]`.
  - Kinds: strings are `SharedReference`, slot Types are `OwnedSlot` pointers, everything else is `Value`. Zero-sized parameters
    are omitted (`:171-176`).
  - Aggregates are passed by pointer, and there is a result slot when `HasResultSlot` holds.
- **C convention:** pointers and references are `ptr`, primitives use their argument type, Unit is `void`, no attributes
  (`E/LlvmEmitter.cs:315-410`).
- **Not ported:** `PreparedSlot` (legacy default evaluators, `E/DefaultGenerationPlan.cs:137-152`). A MIR `DefaultTemplate`
  body takes ordinary `ref` parameters (PLAN10 §5.6.2).
- **No gate in `Abi`:** the `FunctionAbi.Supports` gate (`E/FunctionAbi.cs:27-31`) stays with the old callers until G7/G8. A form
  that `Abi` cannot lower is a coverage row (sel) or, after X2, a capability row.

### 2.7 DropGlue (PD18; C4a, C6b)

`DropGlue(T)` is a mono item per erased Type that `MirTypes.MayNeedDrop` admits (`M/MirTypes.Destruction.cs:30-34`). The
collector adds it through a `MirTypes.Destruction` visitor (K7). Its body follows SPEC 16.3.2 and the walk's own case split
(`M/MirTypes.Destruction.cs:68-122`):

| T | Glue body |
| --- | --- |
| struct that declares `drop` | One call of its destructor instance (`AdtDef.DestructorSymbol`). Nothing else: the MIR destructor body already drops its own Fields last to first, then `_1.base1` (R4-M6b) |
| other struct | Own Fields in reverse logical order, each whose Type `MayNeedDrop`, then the base's glue |
| Tuple, fixed array | Elements in decreasing index |
| enum | Switch on the tag; the active Case's payload elements in reverse |
| string | The runtime's string release (`LlvmModuleText` runtime) |
| Array, Dictionary | The `LegacyRuntimeHelpers` release row with the element glue pointer (until R8) |
| `obj`/`rc`/`arc` | `ObjectRuntime` release. A Sealed payload uses its glue directly, otherwise descriptor finalize (C6b, SHARED_OBJECT_RUNTIME) |
| Function value | The environment table's drop entry, if non-null |
| abstract, InlineStorage | Abstract is impossible in a closed instance (`InternalInvariant_Kd`). InlineStorage has no glue (R4-M6b) |

The legacy template `__kimi_drop_aggregate` calls the destructor *and* drops the Fields and base
(`E/LlvmModuleWriter.Aggregates.cs:192-203, 247-266`). That is correct for old destructor bodies, but it would destroy twice
with MIR destructor bodies. So it is never reused (PD18). Exactly-once destruction is checked by C4s's matrix through E2's
fail-closed audits.

### 2.8 Statics (PD19, OQ17(b); C7a, C7b)

| Item | Design |
| --- | --- |
| Slot | Per `MirStatic.Symbol` (`M/MirBody.cs:201`): `@__kimi_static<N>` (value, Layout-aligned) and `@__kimi_static<N>.state` (`i8`) |
| States | 0 Not started, 1 Initializing, 2 Initialized, 3 Finished: SPEC 22.4.3's four states as named constants in `Statics.cs`. Legacy has three, with `switch … label %cycle [0 → init, 2 → ready]` (`E/LlvmModuleWriter.Statics.cs:23`) |
| `staticEnsure` (`M/Build/MirBuilder.Places.cs:144-156`) | `load i8`; if 2, continue (the hot path, first test). If 0: store 1, call the StaticInit instance into the slot, store 2. Otherwise: Abort `KIMI_E_STATIC_CYCLE`. In R5 that arm is state 1 only, since nothing writes 3. P42 splits it with `KIMI_E_STATIC_SHUTDOWN` and its row, and writes 3 at shutdown, with no representation change |
| Location | The `staticEnsure` call's SourceInfo, which is its access node (SPEC 22.5.1: the Place with its qualifier; (H) that the node's span starts at the qualifier). Inside a Kimi-internal static access, `LocationOf` gives the caller location (I5) |
| Reason row (C7a) | `KIMI_E_STATIC_CYCLE` appended to the shared reason table (17 rows today, `E/WindowsLowering.Abort.cs:10-26`). `ExpandAbortReasons` writes every row into every module (`:53-80`), so this one row changes the text of **every** legacy `.ll` (`[17 x …]` → `[18 x …]`, one constant added) while native behavior is unchanged. C7a lands it alone as the declared legacy-text change (plan §5.3 item 7), with an E4 run that shows only that hunk |
| Constant fold | A StaticInit body whose runtime blocks hold only storage markers and `_0 = Use(Const)`, ending in `Return`, becomes `@__kimi_static<N> = internal unnamed_addr constant <T> <c>`. It has no state, and its `staticEnsure` writes nothing. One function owns the rule, `Statics.ConstantOf(body)`. O2 parity: legacy folds literal reads into every use and emits no global (`src/Kimi/Compiler/StaticScalar.cs:38-58`); a load from a constant global folds at O2 |
| D2 rows | A generic storage key (`MirStatic` carries no enclosing Type arguments, so the key of SPEC 22.4.4 is not representable) and every static that is not an immutable scalar: mutable, aggregate or drop-needing. Reason: the deciding old analysis admits only `StaticScalar` (`src/Kimi/Compiler/StaticScalar.cs:10-29`), so these cannot be exercised under sel before H5. They would be unverified support. X2 moves them into the capability table. Note for P42: the state is final, but generic keys need `MirStatic` to carry the container's Type arguments, which is a MIR change |
| Threads | (H) Structured worker offload (SPEC 22.3.6) is not implemented, so the machine is single-threaded. The `i8` state admits a later `cmpxchg` without a representation change |

### 2.9 Shims and other mono items (PD3)

- **The items:**
  - Type-driven: DropGlue, VTable, ObjectDescriptor.
  - Call-forwarding: closure erasure adapter, item adapter, built-in comparison, format and arithmetic shims, property
    witnesses.
  - Entry and TestEntry.
- Each is a Backend mono item keyed by `(kind, erased Type or instance)`. Its writer composes FunctionCodegen primitives over
  `Layout`, `Abi` and the facts the collector resolved (`TyCtxt.Comparison` leaves F6, `TyCtxt.Witness` F5b, `VTableDef` F4).
- **No MIR body.** Reasons: shims are indexed by Type, not definition (`MirDefKey` has a symbol, `M/MirProgram.cs:19`). They
  are needed only for Generated instances. A4 already takes their effects from `TyCtxt.Witnesses` and the published rows
  (R4-M28), not from bodies. Building them in MIR would add a second, Type-driven builder, validator rows and check-time cost.
- **The legacy closure table:** `{ptr adapter, ptr null, ptr drop}`, with the environment on the heap when it needs destruction
  or exceeds 8 bytes (`E/LlvmModuleWriter.Closures.cs:7-20`, `:10`).
  - B keeps the table shape. An environment of 8 bytes or less stays inline even when it needs destruction. Its drop entry
    receives the address of the environment word (IMPL 21.1.3; deviation class (a) with E12, C5a).

### 2.10 Promoted module components (PD2)

- **Rule.** A syntax-free module-level writer or record moves once into its Backend component, and both generators call it. A
  promoted member's inputs are generator-neutral: LLVM type text, `TypeLayout`/`ValueLowering`, SSA value names, labels and the
  writer.
- Old per-instruction decoding stays in `LlvmModuleWriter` and is deleted at G8. The new codegen never writes a second version
  of a promoted rule.
- Members are split by responsibility while moving, so they end within ARCH's limits.
- Each move is bp (diagnostic snapshot, semantic snapshot and legacy `.ll` byte-identical).
- **Not promoted:**
  - `BodyLowering*` (G7).
  - The serializers keyed by `EmissionOpcode` (G8).
  - `SourceLocationTable`, which moves to Core keyed by `SourceRef` (P5, chapter F).
- **Correction to the plan's wording (P4a, PD8):** the checked-arithmetic failure block is not shared. Each check writes its own
  `br`, then an `abort<id>:` block with a call of `__kimi_abort` and `unreachable`. The `%zero`/`%minusone` `select` sits inside
  that block (`E/LlvmModuleWriter.Scalars.cs:441-490`). P4a promotes that per-check writer. B names its blocks by MIR location,
  not by the old operation id (`:443`).

### 2.11 Selector, generation entry and gate (PD4, PD12; C0a, C4a)

- **Selector.** `KIMI_CODEGEN` ∈ {unset, `legacy`, `mir`} is read once by `CodegenOf(value, pipeline)`, modeled on `PipelineOf`
  (`src/Kimi/Compiler/Core/Compilation.Pipeline.cs:129-134`; the line numbers below are in this file). Any other value throws
  (a configuration error, never a fallback). Until G2, `mir`
  requires `KIMI_PIPELINE=mir`, and every other combination throws. The value is part of the effective configuration:
  `CompilerIdentity` (`:46`) and the ArtifactId manifest mark it (for example `[mir]` → `[mir, codegen=mir]`).
- **One entry.** `Compilation.Generate(TextWriter)` returns whether a module was written. Its typed records go into one reused
  list (`Problem(Code, SourceRef)`, a partition and requirement per §2.14), and no failure string is returned. These callers
  route through it in C0a: `EmissionArtifacts.Publish` (`E/EmissionArtifacts.cs:20-90`, which today calls
  `Emission.TryPrepare` directly), `Project.Emit` and `kimi test` (`src/Kimi/Unit/Command/TestCommand.cs:131-135`, which today
  publishes no record).
- **The out-string adapters.** The public `WriteIr(TextWriter, out string?)` and `Validate(out string?)` (`:115-122`) become
  `[Transitional("R6-gen")]` adapters for their test callers. Under mir their failure text is rendered from the first record
  by the diagnostics adapter. G9 deletes them, and the one test helper `CompilationTestHelper.WriteIr` moves to records.
- **Gate, step 1 (C0a).** Generate only when the deciding analysis accepted the program (under sel the old one) and
  `MirRecords == 0` (construction records, validator findings and, from K4, collector records; `Generates`, `:154-159`). The
  refusal is the existing records, published.
- **Gate, step 2 (C4a; departs from the plan's mechanism).** For every Generated instance's body, read **one published A2
  fact**: `MirDropTable.Incomplete(body) → (Code, Subject)?`. It is the first A1/A2 problem of that body, or A1's arena record
  `OwnershipStorageLimit_Kd` at the body (`M/Analysis/MirInit.cs:58`), or none when the table is exact. A non-null value
  refuses generation with a typed Emission-partition record carrying that code at that subject (R5-M4).
- **Why a fact and not codes.**
  - The codes that A1/A2 report (`MirInit.cs:167, 215, 222, 257`; `MirDrops.cs:293`) are also reported by other passes:
    `OwnershipStorageLimit_Kd` is A5's arena code too (R4-M15a), and `MovedPlace_Kd` will be A5's. A code list in Core would be
    a second copy of who reports what.
  - Attributing a problem's subject to a body fails too: `MirReport.Add` merges equal (code, subject) across bodies
    (`M/Analysis/MirReport.cs:35-47`). So only A1/A2, which know the body, can publish it.
  - So **ask A-2 becomes required before C4a** (an R4 unit in `MirInit`, `MirDrops` and `MirDropTable`; it fits the A-9 merge).
  - Fallback if R4 declines: refuse the whole program on any problem with A1/A2's codes, as a `[Transitional]` gate removed when
    A-2 lands. It over-refuses A5 arena-limited bodies.
- **`InternalInvariant_Kd`:** a runtime Drop with `Group == -1` in a body whose table is exact (PD12).
- **Lifetime of the refusal path:** from G2 (G0's entry) the refusal path applies on the default pipeline too. It goes at H5,
  when MIR decides acceptance.

### 2.12 Coverage rows (PD11)

- **The rows are the dispatch.** The rows are FunctionCodegen's dispatch tables and arms (§2.2) plus the mono-item kinds. An
  arm or row not yet written calls `Uncovered(SourceInfo)`, which records `Unsupported_Kd` at the location: once per
  location, merged like every `Problem`, and under sel only.
- Generation continues through the module, so independent gaps are all reported (SPEC 23.1.2). The text is then discarded:
  no partial module is ever written.
- **No `Supports*` predicate** and no second table: the metric M2 is counted by one test helper from the same row tables (for
  intrinsics, compiler functions and mono-item kinds) and from the arms that call `Uncovered`.
- **At X2:** every remaining row is a decided D2 row (PD27, one owner list). It moves into the capability table (an allowed
  reporter, ARCH:45-47; R4-M8), so it is reported at `check`. `Uncovered` is deleted (≈ −50).

### 2.13 Deviation form and pending ledger (PD10; E10–E12)

- **The deviation form.** `Codegen.Expect<T>(T target, T legacy, Deviation d)` returns `legacy` unless `KIMI_CODEGEN=mir`.
  `Deviation(Id, Class ∈ {A, D, E}, Item, Clause, Reason)` is declared **once per decided item** as a field of one test-side
  catalog (`CodegenDeviations`), so one item shared by tests and fixtures has one record (R5-M6: item, reason and SPEC clause).
- `WriteFixture` and `NativeAllocationAudit.WriteFixture` take an optional `Deviation` and legacy outcome. They write
  `<name>.deviation` (id, class, item, clause, reason, legacy and target outcome) in both runs. E4 accepts a difference only
  where both outcomes match.
- A well-formedness test: every sidecar names a catalog entry, and every entry is used. M7 counts the entries.
- **First entries:**
  - `static-cycle` (a; SPEC 22.4.3, 22.5.1, 22.5.2; legacy reports `KIMI_E_ABORT` at the declaration,
    `tests/xUnitTest/Tests/StaticInitializationTest.cs:119-127`).
  - `inline-env` (a; IMPL 21.1.3; one allocation fewer).
  - `caller-location` (a; SPEC 22.5.1).
- **Pending ledger:** E10/E11 as the plan says (per-group files, an exact-equality gate that only shrinks). Both are removed
  at G4.

### 2.14 Generation records after `GenerationFailed_Kd` (successor; G9)

Today every generation failure is `GenerationFailed_Kd` (Internal, unlocated, free-text note). It has one report site,
`E/LlvmEmitter.cs:77` via `Project.Emit` (`src/Kimi/SolutionAndProject/Project.cs:544-556`), defined at
`src/Kimi/Diagnostics/DiagnosticCode.tinyhand:770-772`. The cause decides the successor:

| Cause | Today | Successor | Category | Partition / requirement |
| --- | --- | --- | --- | --- |
| Codegen, Layout or Abi contract failure | 446 `Fail("…")` strings (`ArchitectureRulesTest.cs:16`) → `GenerationFailed_Kd` | `InternalInvariant_Kd` at the instance's or statement's subject, no artifact (PLAN10 §5.7, §6.3) | Internal | Emission / Emission |
| Unimplemented form | the same strings | `Unsupported_Kd` located: coverage rows (sel, until X2), then the capability check at `check` | Unsupported | Emission (rows); ControlFlow (capability, with MIR records) |
| Generation limit | `GenerationResourceLimit_Kd` at the project file | the same code, located at the requesting call or by-value use (K4, K10a) | Resource | ControlFlow (PD7) |
| PD12 refusal (until H5) | — | the A1/A2 code at its subject | the code's | Emission / Emission |
| Output I/O at publication | `ex.Message` → `GenerationFailed_Kd` (`E/EmissionArtifacts.cs:62-86`) | **`ArtifactPublicationFailed_Kd`** (new) with typed facts (cause Output, path, OS error), at the project file | Input | Emission / Emission |
| `NativeConfiguration.Validate` repeated at publication | `E/EmissionArtifacts.cs:98-101`, already run at preparation (`src/Kimi/SolutionAndProject/DependencyConfiguration.cs:77` → `ProjectPreparationFailed_Kd`, Input) | deleted (one owner) | — | — |
| Publication-only configuration checks | adopted backend record (`:36`), archive hash (`:51`), `kimi_backend` kind and Sha256 (`:114`, `:119`), paths and `.lib` inputs (`:58`, `:182-185`) | `ArtifactPublicationFailed_Kd`, cause NativeConfiguration, at the project file | Input | Emission / Emission |
| Import without a supply for the target | `:169` | `ArtifactPublicationFailed_Kd`, cause NativeSupply, at the `#LibraryImport` (SPEC 20.4 resolution). (H) It may overlap Binding's `MissingNativeRequirement_Kd` (`src/Kimi/Diagnostics/DiagnosticCode.tinyhand:558-560`); G9b checks this and deletes whichever duplicates the other | Input | Emission / Emission |
| Dependency-module supplies not linked | `:152` | `Unsupported_Kd` at the `#LibraryImport` (a valid form not implemented; STATUS row) | Unsupported | Emission |
| Toolchain missing, opt/llc/lld-link failures | plain text, no code (`Project.cs:229-231`, `NativeToolchain.cs:367-493`) | unchanged in R5/R6 (outside generation; R9 moves the toolchain to `src/Kimi/Build`); an open issue | — | — |

- **Net codes:** −1 (`GenerationFailed_Kd`) +1 (`ArtifactPublicationFailed_Kd`).
- **Why a new code rather than recategorizing `GenerationFailed_Kd`:** its name would misdescribe output failures, and its
  Internal category is wrong for them (SPEC 23.1.1: Internal is a compiler defect).
- **`DiagnosticPartition.Emission`** (`src/Kimi/Diagnostics/DiagnosticPartition.cs:30`) and **the `Emission` requirement**
  (`DiagnosticRequirement.cs:37`, `DiagnosticRequirement.tinyhand:647-648`) **remain**.
  - Generation stays a later phase with its own result unit (DIAGNOSTICS §4.2 "Later phases").
  - The requirement name is a stable published name (PLAN10 §7 keeps stable names).
  - Its contract text is updated at G9: "records of generation after acceptance: codegen faults, artifact publication failures;
    until H5 PD12 refusals; until X2 under sel coverage rows".
  - A rename (`Generation`) is left to R9, with the toolchain move.

### 2.15 Memory and time

| Structure | Memory | Time | Warm |
| --- | --- | --- | --- |
| `Layout` memo | one entry per distinct erased Type used | one dictionary lookup | 0 B (hits) |
| `Abi` memo | one `FunctionAbi` per distinct (convention, flags, signature) | one lookup | 0 B |
| ModuleCodegen text buffer | about the module's size, pooled per `Compilation` (H: up to a few MB) | appends | 0 B once grown |
| FunctionCodegen scratch | per-local slot/type arrays sized to the widest body; the shared `MirReach` | O(statements + terminators) per instance, plus O(log rows) per Drop and call | 0 B |
| Request tables (constants, helpers, declarations, names) | arrays and sets cleared per generation | O(1) per request | 0 B |

- **Expected compile time:** MIR codegen ≤ legacy `EmissionLowering + LlvmIrWriting`, since there is no `EmissionModule` pass.
  This is recorded by E14 and X1 (PLAN10-P3), not gated (P3).
- **O0:** spilled scalar arguments add one store and one load per argument. That is allowed and recorded (P1).

---

## 3. O2 parity list

- **Source of the rows:** `docs/dev/LLVM_OPTIMIZATION.md` §2, the rows marked 実装あり/限定実装, checked against the generator.
- **The rule:** B writes each row the legacy generator writes, for the same physical value class, from one owner. It adds no
  optimization attribute in R5: a new row belongs to the LLVM_OPTIMIZATION track after R6, under its §5 procedure.
- **Measurement:** E13's per-function metrics and the corpus kernels A/B. A kernel regression beyond the A=B variance blocks
  its family (R5-M8).

| Row (ID) | Legacy rule and site | B owner |
| --- | --- | --- |
| `align N` on alloca, load, store (A09) | the slot's `Layout.Alignment` (`E/LlvmModuleWriter.cs:310-341`, `E/LlvmModuleWriter.Scalars.cs:117, 163, 411-416`). Statics and test-phase accesses carry none (`E/LlvmModuleWriter.Statics.cs:15-47`) | FunctionCodegen from `Layout`. B also aligns static and test-phase accesses: one rule, and a correct alignment (IR-text information, PD9) |
| `align` on globals (A09) | string constants 1, closure tables and descriptors 8, `_fltused` 4 (`E/LlvmConstantPool.cs:115`, `E/LlvmModuleWriter.Objects.cs:107-170`, `E/LlvmModuleWriter.cs:53`) | `LlvmConstantPool`, `ObjectRuntime`, `LlvmModuleText` (promoted) |
| `ptr align N` on aggregate `memcpy` (A09) | `E/LlvmModuleWriter.Aggregates.cs:71-84` | the promoted transfer writer |
| `noundef nonnull align 8 dereferenceable(24)` (A07, A08, A10) | `SharedReference` parameters, that is string parameters (`E/WindowsLowering.cs:30`, `E/FunctionAbiPool.cs:174`), on definitions and direct calls (`E/FunctionAbi.cs:55`, `E/LlvmModuleWriter.cs:642`); missing on indirect calls (`E/LlvmModuleWriter.Closures.cs:279`) and C declarations | `Abi`'s `AbiParameter` kind, written wherever a parameter list is written. That adds the call-site form of the same guarantee on indirect calls. C declarations keep none (IMPL 21.2) |
| `noalias nocapture writeonly`/`readonly` (A01, A02, A05, A06) | only on the `memcpy`/`memmove` declarations (`E/LlvmModuleWriter.cs:21`). Never on calls or ordinary parameters (`tests/xUnitTest/Tests/ReferenceEmissionTest.cs:153-155`) | `LlvmModuleText` declarations, unchanged |
| `noreturn` (D01) | definitions whose ABI is `NoReturn` (Never results; `__kimi_start`, `__kimi_exit`, `__kimi_abort*`) (`E/FunctionAbi.cs:58`, `E/WindowsLowering.cs:51-53, 81`). Calls get `unreachable` after them, never the attribute | `Abi` flag; FunctionCodegen I8 |
| `unnamed_addr` (E04) | pooled constants (`E/LlvmConstantPool.cs:102`) | `LlvmConstantPool`; folded statics (§2.8) |
| `#0` group and module flag | `uwtable(async)`, `target-cpu`, `target-features`, `denormal-fp-math=ieee,ieee`; `PIC Level 2`; on every `define` (`E/LlvmModuleWriter.cs:15-17`) | `LlvmModuleText` footer |
| bool as `i8` (PD8) | stored `i8`, computed `i1`, zext/trunc (`E/LlvmModuleWriter.Scalars.cs:96-136`) | `Layout` (`ValueLowering`) |
| Checked-arithmetic failure block | per check (`E/LlvmModuleWriter.Scalars.cs:441-490`) | `LlvmScalarText` (P4a) |
| **Absent:** `mustprogress`, `willreturn` (D03, D04) | never written; asserted absent (`tests/xUnitTest/Tests/FunctionEmissionTest.cs:58-59` and about ten more) | never written (I9). These assertions are class R (IMPL 21.3.1, nontermination) and become pipeline-neutral E15 requirements |
| **Absent:** `nounwind`, `nsw`/`nuw` on checked operations, `inbounds`, `lifetime.*`, `range`, TBAA | never written (`tests/xUnitTest/Tests/ScalarEmissionTest.cs:89-90`; `E/LlvmModuleWriter.Scalars.cs:183-184`) | never written (I9) |
| `transferBytes` | `memcpy` (`E/LlvmModuleWriter.Dictionaries.cs:41-45`) | `memcpy` (§0 item 6); `memmove` stays for Array insert/remove and window advance, as legacy (`E/LlvmModuleWriter.Arrays.cs:225, 280`) |

---

## 4. Design A on paper (MIR lowered into `EmissionModule`) against B

This section can be checked from the cited files alone. Opcodes are from `E/EmissionModule.cs:10-75` (49 members).

**Opcodes with a MIR counterpart (35):**

| Opcodes | MIR counterpart |
| --- | --- |
| Label, Branch, ConditionalBranch | blocks, Goto, SwitchInt on bool |
| ReturnVoid, ReturnScalar | Return |
| Unreachable | Unreachable |
| Call | Fn callee |
| VirtualCall | Virtual callee |
| CallValue | Value callee |
| Scalar | BinaryOp, UnaryOp |
| Convert | Cast |
| LoadScalar, StoreScalar | scalar Copy and Assign |
| LoadElement, StoreElement, ElementAddress | Index, ConstIndex |
| BorrowAddress | Ref |
| LoadPointer, StorePointer, PointerOffset | raw Deref and raw intrinsics |
| ObjectPayload, ObjectBorrow | handle Deref, object-borrow Adapt |
| ObjectTypeTest | TypeTest |
| TransferAggregate | aggregate Copy/Move |
| CreateClosure | Aggregate(Closure) |
| EraseClosure | Cast(Erasure) |
| FillArray | Aggregate(FixedArray) |
| Sequence | Len, Index and library rows |
| StringEquals, StringCompare, BuiltinComparison | string rows and shims |
| DestroyAggregate | Drop Keep |
| TestSnapshot, TestPhaseEnter, TestPhaseLeave | test intrinsics |

**Opcodes that encode old-IR concepts (14):**

| Opcodes | Old-IR concept |
| --- | --- |
| Phi | SSA joins; MIR is slot-based |
| StoreLiveFlag, InitializeLiveFlag, DestroyStringIfLive | the old live-flag protocol |
| StorePathFlag, DestroyPart, EndPartDestruction | the old partial-destruction protocol, against A2's `MirFlagPoint` and Open rows |
| StoreStaticString, MoveString | string-specific moves |
| StringPattern, CompositePattern, PatternRead | old pattern tests, against MIR's decision chains (PLAN10 §5.6.3) |
| TupleRelation | operator-mode tuple relations, which F7 expands in MIR |
| SwapScalars | the old swap |

The plan counts 11 and 38. The difference is SwapScalars, TupleRelation and the two string moves, which have partial
counterparts.

**Other old-IR ties:**

- `EmissionInstruction` carries four string fields (`:142`). `ScalarOperator` holds raw LLVM opcode text or about 26 named
  sub-operations: Sequence alone has about 22 (`E/LlvmModuleWriter.Sequences.cs:36-345`), and BuiltinComparison has 4.
- Its `Operation` is the old ownership operation id, which names labels and closure tables (`E/LlvmModuleWriter.Scalars.cs:443`,
  `E/LlvmModuleWriter.Closures.cs:15`).
- Its `Aggregate` is an old-pool `AggregateLayout`, with destruction callbacks.

| Criterion | A: MIR → `EmissionModule` → existing writer | B: MIR → `FunctionCodegen` → text |
| --- | --- | --- |
| Consistency (one representation per concept) | Two lowering stages. MIR's drop flags, Open rows, case views and Adapt have no opcode, so either new opcodes or old protocols are emulated. String sub-operations reintroduce stringly typed dispatch. Departs from PLAN10 §5.7 (物理 IR 層は置かない, owner-approved) unless a removal stage is named | One stage. Each MIR form has one writer. PLAN10 §5.7 as approved |
| Where per-form knowledge lives | In the MIR → EmissionModule lowering. For a new form the writer adds nothing, so the second stage is pure transcription | In FunctionCodegen |
| Size (H) | Lowering ≈ 3,000–3,500 lines, plus ≈ 500–800 for missing opcodes or writers. Keeps `EmissionModule` (433), `LlvmModuleWriter*` (4,601) and `AggregateLayoutPool` alive. Then either kept for good, against PLAN10, or a second rewrite (≈ 3,000+) | ≈ 4,100 new lines (§10), with ≈ 3,450 promoted and shared. G8 deletes `EmissionModule` and the serializers |
| Compile time | One more pass and per-instruction records. The writer stage is 0.016–0.066 ms of a 21–43 ms compilation (PD1), so neither difference is measurable | Direct |
| Generated code | Reuses the serializers' attributes at once | The same attributes through the promoted writers (§3). After PD2, A's reuse advantage is gone |
| Correctness risk | MIR semantics forced into opcodes whose writers assume `BodyLowering`'s invariants (live flags, operation ids, `CheckInputs`' nine checks, `E/LlvmEmitter.cs:520-600`) | Written from MIR's contract directly. Exactly-once destruction tested by C4s |
| First slice | Faster for scalar families | The frame first (C0b1). Then scalar families reuse `LlvmScalarText` |
| Tests | Could keep `TryPrepare` structure tests. But those are class S and deleted anyway (PD14, G5) | E15 matcher. Structure tests deleted at G5 |
| Debug information | Fits the DI plan's `EmissionModule` placement | Needs PD24's re-placement (§7): a record, no code in R5 |

**Verdict: B.** A's only material advantage, reuse of the module-level text, is captured by PD2. Its costs are permanent
(two stages, old concepts) or require a second rewrite. OQ3/R5-M3 waived the spike, and E13 at C1a measures B's first slice
against legacy.

---

## 5. Review of the plan decisions this chapter covers

| PD | Verdict | Reason, refinement or cost |
| --- | --- | --- |
| PD1 | Agree | §4 |
| PD2 | Agree, refined | The generator-neutral input rule (§2.10). P4a's "shared failure block" corrected to per-check blocks |
| PD3 † | Agree | §2.9. Shims are Type-indexed, needed only for Generated instances, and their effects come from published facts. Compatible with R8, where library-first bodies replace shims instead of making them MIR shims |
| PD4 | Agree, refined | Identity mark and manifest (§2.11). From G2, `KIMI_CODEGEN=mir` alone is valid. G4 removes it |
| PD8 | Agree | All three checks hold by construction (I10; `__kimi_f<N>`/`__kimi_entry_body` naming, ModuleCodegen). Fact correction as in PD2. The regex needs at least one `define internal (void\|i1…i64) @__kimi_(entry_body\|f\d+)(` (`tests/xUnitTest/Tests/ScalarEmissionTest.cs:136`). That holds for any program with a body |
| PD9 | Agree | IR text and instance sets are information |
| PD10 | Agree, refined | One deviation catalog, so an item is declared once (§2.13) |
| PD11 † | Agree, refined | The rows are the dispatch (no gate, no second table). No partial module. Under sel only. X2 moves rows into the capability table |
| PD12 † | Agree on meaning; mechanism changed | A2 publishes per-body exactness and its cause (A-2 required before C4a). There is no code list in Core (§2.11) |
| PD13 | Agree | I11; pooled per `Compilation` |
| PD14 | Agree | G5/G6 |
| PD18 | Agree | §2.7. The legacy template is right for old destructors and wrong for MIR ones |
| PD19 | Agree, refined | Four-state encoding and the else-arm split for P42. C7a/C7b split, because the reason row changes all legacy text. D2 rows justified by verifiability. P42 needs `MirStatic` to carry the key's Type arguments (§2.8) |
| PD21 | Agree | No new `IntrinsicId`, `CompilerFunctionKind` or `BindingFailure` entries. A reason row is runtime data, not a vocabulary entry. C8a corrects only the comment of `MoveBytes` |
| PD22 | Agree | MIR is built once for product and test builds, so test phases are a test-build rendering of A2's runtime drops, drop trees and `staticEnsure` initialization, written by codegen (C10b) |
| PD23 | **Reject the premise** | The deciding contract is the library declaration (`src/Kimi/Library/StorageOperations.kimi:71-75`: "the two ranges do not overlap"), not the `IntrinsicId` comment. `memcpy` keeps legacy parity and the Dictionary kernel's O2. If R8's `Storage.moveBytes` (PLAN10 §5.8, overlap allowed) replaces it, R8 changes the declaration and the lowering together |
| PD24 | Agree | §7 |
| PD26 | Agree | Stage 5 is layout and stage 6 MIR codegen under sel (E14) |
| PD27 | Agree | X2's owner list |

---

## 6. Departures from owner-approved texts

| Text | Departure | Reason | Cost |
| --- | --- | --- | --- |
| PLAN10 §5.4 (`BodyKind … Shim(kind)`), §5.7 MonoItem `Shim(kind)`; A5P §9.1's start note "MirBodyKind has no Shim yet" | No `MirBodyKind.Shim`. Shims are Backend mono items written through FunctionCodegen primitives (PD3) | §2.9 | Shims do not appear in MIR dumps (they do in `.ll`), and A1–A5 do not run on them (they rely on published facts, as today) |
| ARCH:45-47 (Unsupported only from Binding, MIR construction and the capability check); PLAN10 §6.2 rules 3 and 7 | Coverage rows report `Unsupported_Kd` from codegen, under sel, until X2 (PD11) | A whitelist that closes "not yet generated" with a metric; no silent wrong code | ≈ 50 lines, deleted at X2 |
| R4-M1 (MIR problems unpublished until R6 unit a) | PD12's refusal publishes A1/A2 codes at generation, in the Emission partition, from C4a (sel) and G2 (default) until H5 (R5-M4 approved the form; C4a writes the superseding entry) | Never generate from an inexact drop table | Needs A-2 |
| Kimi Debug Adapter Plan §6.1 (DI tables in `EmissionModule`; 方針決定済み) | DI produced by FunctionCodegen from MIR `SourceInfo`, `MirLocal.Name` and `MirScope` (PD24) | `EmissionModule` is deleted at G8 | A record now; KD2 is implemented on B |
| PLAN10 §5.8 (`Storage.moveBytes`, overlap allowed) | `transferBytes` keeps its non-overlap contract and `memcpy` in R5 | The landed declaration is the contract | none |

`GenerationFailed_Kd` → `InternalInvariant_Kd` follows PLAN10 §5.7 and §6.3. The Input code for output failures (§2.14) is an
addition for a cause PLAN10 did not cover.

---

## 7. Debug-information design record (PD24)

No debug information is generated in R5. KD1 and later are implemented after the plan, on B (PLAN10 §17.3, §18 P8; A5P OD-P8).
This record fixes what B provides, so KD2 needs no Emission work.

| DI plan item (2026-10-09 plan §6–§7) | B's source | Gap / owner |
| --- | --- | --- |
| Statement starts (§6.2) | `SourceInfo.StatementStart`, set today for block items, constructor steps and body results (`M/Build/MirBuilder.cs:268, 290, 327`) | **Gaps:** control headers (if/while condition, for iteration acquire, match subject), each match arm, and the statement after `=>`. The builder sets them in KD2's first unit (a later W-window); codegen only reads the flag |
| Continuation positions (a different column) | The `SourceInfo.Syntax` of the outer node | none |
| Cleanup positions | Drop-tree terminators carry their registration scope's SourceInfo (`M/Build/MirBuilder.Drops.cs:177-189`). A drop-tree suffix that several exits share gets the scope-end location, never a statement start | Builder, KD2 |
| Scopes | `MirScope(Outer, Kind, Source)` (`M/MirBody.cs:186`). Variables are scoped to the `DISubprogram` (DI plan). `Inlined` scopes map to `inlinedAt` at the call | KD-Q: inline-default display |
| Debug names (§6.1) | The `name` is the canonical declaration display plus instance Type arguments plus `#n` if duplicate, unique per module; `linkageName` is `__kimi_f<N>` (ModuleCodegen owns names) | KD2 |
| Slots (§6.3) | I2 gives every non-zero-sized local, scalar arguments included, a slot. A pointer-passed argument's address is its incoming pointer (one indirection) | **None:** `DebugInfo=true` changes no slot code (DI plan §6.5 item 1 becomes empty) |
| Artificial runtime functions (§6.1) | `LlvmModuleText`, `LegacyRuntimeHelpers` and `ObjectRuntime` each list their function names. Mono items (glue, shims, Entry) are artificial too | KD2: `/_/generated/runtime`, line 1 |
| Variable states (§6.3, KD-Q2) | Must come from a fact A1 publishes per statement start. The Backend may not replay analyses (ARCH) | KD7 asks A1's owner |
| Columns (UTF-16) | Core location table by `SourceRef` (P5) | none |
| `DebugInfo=false` cost | No DI tables are collected and no allocation is made (a flag in FunctionCodegen) | none |

---

## 8. Unit mapping and review groups

Review groups as the plan names them (S2 confirms the list). Only changes and design pointers are stated.

| Unit | Part of this design | Change to the plan | Review |
| --- | --- | --- | --- |
| P0, P0b | §2.10, §2.1 file names | none | R |
| P1a–P1d, P2a, P2b | `LegacyRuntimeHelpers` (PD2) | none | R |
| P3a, P3b | `ObjectRuntime` | none | R |
| P4a | `LlvmScalarText`: the per-check failure block | wording: not a shared block | R |
| P4b | conversions | none | R |
| P5, P6 | inputs 6 and ModuleCodegen (chapter F owns P5) | none | D+R (S3), R |
| P7a, P7b, P7c | §2.5 | none | D+R (S4) |
| P8 | §2.6 | `PreparedSlot` not ported | D+R (S4) |
| E10, E11 | pending ledger | none | G:C-frame, R |
| E12 | §2.13 catalog | one `Deviation` per item | with its first consumer |
| E13, E14, E15 | metrics, benchmark, matcher | E15 also carries the class-R "absent attribute" assertions (§3) | R, R, G:C-frame |
| C0a | §2.11: selector, `Generate`, typed records, gate step 1, Transitional out-string adapters | adds the adapters and the `kimi test` record path | G:C-frame |
| C0b1 | §2.2 frame (I2's zero-sized address), dispatch, `Uncovered` | none | G:C-frame |
| C0b2 | ModuleCodegen; Entry mono item (PD3, not "`Shim(Entry)`") | wording | G:C-frame |
| C1a–C3b | families over §2.2 | C3a reads the collector's callee table (§2.4) | G:C-scalar, G:C-aggregate, G:call |
| C4s, C4a–C4c | §2.7, I6, gate step 2 | C4a needs ask **A-2 (required)** | G:C-drop |
| C5a, C5b | §2.9 closures and adapters | none | G:closure |
| C6a–C6c | descriptors, handle glue, VTables | none | G:object |
| **C7a (new)** | the `KIMI_E_STATIC_CYCLE` row in the shared table; the declared legacy-text change | split from C7 | R |
| C7b | §2.8 | as C7 without the row | D+R (S4) |
| C8a | raw, failure rows; `transferBytes` → `memcpy`; correct the `MoveBytes` comment | PD23 reversed | G:library-1 |
| C8b–C8f, C9a–C9c, C10a, C10b | families | none | G:library-1/2, G:format, G:test |
| X1 | sweep | none | — |
| X2 | §2.12: rows into the capability table | none | R |
| G3, G4 | selector default; scaffolding removal (selector, ledger, catalog, `.deviation`, coverage remnants) | none | G:R6-gen-switch, G:R6-gen-del |
| G8 | delete `EmissionModule`, serializers, `AggregateLayoutPool`'s destruction half, `FunctionAbiPool`'s `FunctionKoto` keying and `Supports` gate | none | G:R6-gen-del |
| G9 → **G9a, G9b** | G9a: Emission-only Binding members (as the plan). G9b: §2.14's successor table, deleting the repeated configuration validation and the out-string adapters | split (G9b changes diagnostics contracts and touches Core and Emission artifacts) | G:R6-gen-del |
| G10 | zero-reference rows | none | G:R6-gen-del |

---

## 9. Alternatives considered

| Concern | Fix inside the owning component | Change of shared structure | Chosen |
| --- | --- | --- | --- |
| Codegen architecture | A: lower MIR into the existing `EmissionModule` | B: direct `FunctionCodegen`, promoted shared writers | B (§4) |
| Drop glue | Reuse `__kimi_drop_aggregate` with a "destructor only" switch (a bool steering a function: a symptom fix) | DropGlue from `Layout` + `MayNeedDrop` in SPEC order | DropGlue |
| Gate on inexact drop tables | A code list in Core, or a codegen-local `Group == -1` test (misses A1 problems in analyzed bodies) | A2 publishes per-body exactness (A-2) | A-2; the code list only as a Transitional fallback |
| Not-yet-generated forms | `InternalInvariant_Kd` or an exception (a valid program as a compiler fault; hides later gaps) | Capability-table rows from the start (pollutes the permanent table; check-time) | Coverage rows as the dispatch (PD11), moved into the capability table at X2 |
| Shims | `MirBodyKind.Shim(kind)` with a Type-driven builder (needs a Type-keyed `MirDefKey`, validator rows, check-time cost) | Backend mono items over FunctionCodegen primitives | mono items (PD3) |
| Layout | Fork a MIR-only Layout to follow IMPL 21.1.3's nonnull Option now (two layout rules during R5) | One shared Layout with the legacy rules | shared; nonnull Option after G8 |
| Static states | Port legacy's three states | SPEC's four states | four (OQ17(b)) |
| Text sink | Stream each function straight to the `TextWriter` (needs requests known up front: a second MIR walk) | One pooled buffer and two assembly phases | buffer |
| `GenerationFailed_Kd` successor | Recategorize and rename it | `InternalInvariant_Kd` + one Input code + delete the re-checks | the latter (§2.14) |
| `transferBytes` | `memmove` (PD23; safe superset, O2 risk) | follow the declared contract | `memcpy` |

---

## 10. Deletions and expected size and speed

| Stage | Deleted (lines, `wc -l` at base) | Notes |
| --- | --- | --- |
| P units (moves) | ≈ −3,150 from `E/` | ≈ +3,450 into `B/`, bp (plan §6.1) |
| G7 | `BodyLowering*` 10,468; `LlvmEmitter*` 670; `GenericStoragePlan*`, `ObjectGenerationPlan`, `VirtualGenerationPlan`, `DefaultGenerationPlan`, `CompilerFunctionAdapters`, `NumericArithmetic`, `StaticScalarEntry` | instance machinery with its callers (S2/OQ6) |
| G8 | `EmissionModule` 433; the rest of `LlvmModuleWriter*` (4,601 before promotion); `AggregateLayout` 538 → `Layout`; `FunctionAbi*` 262 → `Abi`; the `WindowsLowering*` remnant | `LegacyRuntimeHelpers` (R8), the toolchain files (R9) and the `.ll.in` resources stay |
| G9b | `GenerationFailed_Kd`; the 446 `Fail("…")` strings (ratchet → 0); the repeated `NativeConfiguration.Validate`; the out-string adapters | +1 Input code |
| X2, G4 | `Uncovered` (≈ −50); selector, ledger, catalog, `.deviation` (≈ −400) | |
| New in R5 | FunctionCodegen and items ≈ +4,100 (H): frame 250, arithmetic 200, casts 150, places 250, rvalues 250, calls 310, drops 380, closures 300, objects 430, statics 160, library rows 720, format 400, test 160, ModuleCodegen 150 | members ≤ 60 statements, no LINQ |

- **Net at G11:** the plan's ≈ −18,300 for R6-gen holds (H); this design changes no total beyond ±100.
- **Speed:** compile time as in §2.15. Generated code: O2 parity rows (§3). The O0 cost of argument slots is recorded.

---

## 11. Risks, open questions and owner decisions

| # | Item | Kind |
| --- | --- | --- |
| RK1 | Ask A-2 becomes required before C4a (`MirDropTable.Incomplete(body)`, written by A1/A2). If R4 declines, the Transitional code-list fallback over-refuses A5 arena-limited bodies | R4 ask; design review |
| RK2 | The collector interface (§2.4: view index, callee table, instance-type function) must match chapter K's design | review across S2/S4 |
| RK3 | PD23 reversed (`memcpy`). The reviewer should confirm that the declaration, not the intrinsic comment, is the contract | design review |
| RK4 | C7a changes every legacy `.ll` (the reason table is emitted whole). E4 must report "only this hunk" for that commit | E4 option, or a manual hunk check |
| RK5 | The shared Layout keeps the tagged `Option<ref/T>`. IMPL 21.1.3's nonnull Option stays a STATUS limit until after G8 | open issue for PLAN |
| RK6 | ABI attributes on indirect calls are an addition beyond legacy (the call-site form of the definition's guarantee). The reviewer may ask for strict parity instead | design review |
| RK7 | P42 needs `MirStatic` to carry the container's Type arguments for generic keys. OQ17(b)'s "no representation change" holds for the slot state only | record for P42 |
| RK8 | Static D2 rows are unreachable under sel before H5, so they are untested until then. X2 places them in the capability table, which H5 exercises | accepted |
| RK9 | Thread-safe static initialization (worker offload) is not designed (H: no offload yet) | open issue |
| RK10 | Toolchain and tool failures stay plain text without a code through R6 | open issue (R9) |
| Q1 | `ArtifactPublicationFailed_Kd`: a new Input code (net 0 with `GenerationFailed_Kd` deleted). Accept, or reuse an existing code? | **owner, with the package** |
| Q2 | Keep `DiagnosticPartition.Emission` and the `Emission` requirement name, renaming only at R9? | owner, with the package |
| Q3 | PD3 and PD11 (§6) are the parts that depart from owner-approved texts; the summary for the owner names them (R5-M1) | owner look (OQ4) |
| Q4 | Should the "absent attribute" assertions (`mustprogress`/`willreturn`) become E15 requirement tests on both pipelines (class R), and the `nsw`/`inbounds` ones class I? | design review (OQ16 rules) |
