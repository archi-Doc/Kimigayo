# Q2: Code generation shape, LegacyRuntimeHelpers and structure limits

Read-only study. Checkout `dev` at `5c42b577`; documents read from `origin/dev` (`2aac946b`). `src/Kimi/Compiler/Emission` is
identical on both (`git diff --stat 5c42b577 origin/dev -- src/Kimi/Compiler/Emission` is empty). MIR files were read from
`origin/dev`. Paths are repo-relative. "(H)" marks an inference that was not verified. Statement counts marked "proxy" come
from a throwaway scanner (`scratchpad/members.py`: lines ending in `;` plus control-statement headers per member). They
approximate the ARCH "Statements per member" measure and do not reproduce it exactly.

## 0. Answer in brief

- **Recommendation: design B**, a direct `FunctionCodegen` from validated MIR (PLAN10 §5.7, ARCH target pipeline P11–P12:
  "LLVM text written directly from MIR"). Add one structural rule: **promote, don't copy.** Before the first codegen slice,
  behavior-preserving commits move the syntax-free module-level writers out of `LlvmModuleWriter` into Backend components:
  - the module frame (`LlvmModuleText`);
  - the object and rc/arc runtime (`ObjectRuntime`, kept for good by PLAN10 §5.8);
  - the Array, Dictionary, formatting and string-comparison helpers (`LegacyRuntimeHelpers`, removed at R8);
  - the checked-scalar text kernels.

  The legacy `.ll` stays unchanged, and the old writer calls those components. `FunctionCodegen` writes only function bodies
  and calls the same components. So the two pipelines can differ only in body text, and module-level text agrees by
  construction.
- **Design A works, but it is the wrong investment.**
  - MIR maps onto at most 38 of the 49 opcodes. 11 opcodes have no MIR producer: they encode old-IR concepts such as string
    liveness flags, move-path flags, Phi and old match plans.
  - Operations travel as strings: 4 string fields per instruction, about 20 `Sequence` sub-opcode spellings, and LLVM
    mnemonics keyed by syntax `KotoKind`.
  - SSA and helper names come from old ownership operation ids.
  - A keeps about 3.05k writer lines alive after R6-gen. Those lines need about 9 member splits and an `out string` removal
    before they can live in the Backend. A still ends with a removal stage, which means writing B later.
- **Measurement.** The writer stage (`LlvmIrWriting`) costs 0.016–0.066 ms per compilation. Emission lowering costs
  0.16–2.4 ms, and the whole compilation 21–43 ms (`artifacts/benchmarks/r3-u16c-v14/exit-compiler-pipeline-1/phases.json`).
  If A and B share text kernels, they emit identical instruction text, so opt -O2 output cannot tell them apart. A spike of A
  therefore has no discriminating power on run time or on compile time. The owner may waive the measured A spike: A stays
  on paper (this report, §2), and B's first slice is measured against legacy (§6).
- **LegacyRuntimeHelpers.**
  - The R0 ledger assigns 847 lines across six files, but one of them, `DictionaryIteration`, is a per-instruction
    serializer that cannot move verbatim.
  - The correct scope is about 970 lines of module-level text plus extracted inline kernels (§4).
  - PLAN10's "~1.5k" over-counts. Earlier estimates included object, statics and string-liveness writers, which are permanent
    or not reusable.
- **ARCH conflicts.** Every conflict has a resolution, listed in §5, and none needs an owner exception under B:
  - three are published facts (a Core runtime-location service by `SourceRef`, a Binding declaration key for runtime type
    identity, a `TestCatalog` symbol column);
  - the rest are splits during the move;
  - `WriteFunction` and the per-instruction serializers never move under B, because they die at R6-gen inside the
    baseline-exempt old Emission.

## 1. Facts this report builds on

| Fact | Evidence |
| --- | --- |
| 49 opcodes, 20 operand kinds, 19-field instruction with 4 string fields (`ScalarType`, `ScalarOperator`, `LowerPredicate`, `UpperPredicate`) and `Operation` = "the source ownership operation ID" | `src/Kimi/Compiler/Emission/EmissionModule.cs:10-75`, `:77-107`, `:141-142` |
| MIR vocabularies 5/10/6/7/4/5 (+ `CheckTarget`) and auxiliary enums (`MirBinaryOp` 16, `MirCastKind` 9, `MirAggregateKind` 4, `MirConstKind` 8) | `git show origin/dev:src/Kimi/Compiler/Mir/MirVocabulary.cs` lines 15-148; ARCH "Closed vocabularies" |
| Writer consumes only `EmissionModule`; names values `%v<Operation>`, labels `b<id>`, slots `%p<Place>`; 54 `instruction.Operation` reads | `LlvmModuleWriter.cs:8-11`, `LlvmModuleWriter.Scalars.cs:7-60`; `grep -n "instruction.Operation" LlvmModuleWriter*.cs | wc -l` = 54 |
| `WriteFunction` dispatches 35 `case EmissionOpcode.` arms; `WriteScalar` takes the rest | `LlvmModuleWriter.cs:259-538`; `grep -c "case EmissionOpcode\." LlvmModuleWriter.cs` = 35 |
| LLVM operator mnemonics keyed by syntax `KotoKind` | `Emission/NumericArithmetic.cs:49-61` (`KotoKind.Plus => "sadd"`...) |
| Old attribute surface is small: `noundef nonnull align 8 dereferenceable(24)` on `SharedReference` parameters, `noreturn`, `memcpy`/`memmove` declarations with `noalias nocapture readonly/writeonly`, `align` on every load/store/alloca | `WindowsLowering.cs:30`, `FunctionAbi.cs:58`, `LlvmModuleWriter.cs:20`; `docs/dev/LLVM_OPTIMIZATION.md` §2 rows A01, A02, A07–A10, D01 (実装あり/限定実装) |
| O2 runs `opt -passes=default<O2>`; O0 runs no opt passes | `NativeToolchain.cs:367-376` |
| IR-shape assertions on every `EmitFixture`: function `@__kimi_entry_body`/`@__kimi_f\d+`, no `select i1` except `%zero\d+`/`%minusone\d+` forms, no `store i1` | `tests/xUnitTest/Tests/ScalarEmissionTest.cs:130-142` |
| Function naming `__kimi_f<ordinal>` | `FunctionAbiPool.cs:156` |
| A2 output: verdict rows per Drop and view class, flag points per location and view, flags start clear; `Group(body, block, view)` | `git show origin/dev:src/Kimi/Compiler/Mir/Analysis/MirDrops.cs` lines 22-108 |
| Case views: `Views(callee, subst)`, `Layer`, `Reborrows`, `Adapt` | `Mir/MirCases.cs` (origin/dev) lines 120-157; ARCH canonical "Semantics cases" |
| Structure limits apply to the Backend with no baseline; no `out string` failure text; LINQ forbidden in the Backend from R5 | ARCH "Structure limits", "Code rules" |
| Backend may read MIR, declaration tables and `TyCtxt`, not HIR, borrow state or syntax | ARCH "Dependency rules" |
| PLAN10 §5.7: no physical IR layer; `GenerationFailed_Kd` deleted; alloca + mem2reg; no phi or dominator validation | PLAN10 §5.7 bullets "コード生成" and "物理 IR 層は置かない" |
| R0 ledger: `EmissionModule.cs` → 削除 R6 「物理 IR 層は置かない」; `BodyLowering*` → Backend/Codegen (FunctionCodegen) | `artifacts/verify/reduction-R0/disposition.md` Emission rows |
| A5 plan §9.1 step 0: two codegen designs, both measured | A5 plan §9.1 "R5 step 0" |

## 2. Design A: MIR lowered into `EmissionModule`

### 2.1 Vocabulary mapping

| MIR form | A: opcode(s) | B: LLVM text | Fit |
| --- | --- | --- | --- |
| `Assign(place, rvalue)` | value opcode, then `StoreScalar`/`StoreElement`/`StorePointer`/`TransferAggregate`/`StoreStaticString` | value text, then `store` or `memcpy` into the place address | direct |
| `StorageLive`/`StorageDead` | none (old emits no lifetime markers; LLVM_OPTIMIZATION C03 未付与) | none | direct |
| `FakeRead`, `EndBorrow` | none | none | direct |
| `Use(Copy/Move p)` | `LoadScalar`/`LoadPointer`/`LoadElement` or `TransferAggregate`; strings `MoveString` (field-wise 3-field copy, `LlvmModuleWriter.Strings.cs:55-89`) | `load` or `memcpy`; strings field-wise | direct |
| `Ref(kind, p)` | `BorrowAddress` (GEP i8 + offset), `ElementAddress`, `ObjectBorrow` | address text | direct |
| `BinaryOp`/`UnaryOp` | `Scalar` with `ScalarType`/`ScalarOperator` strings and `ArithmeticCheckKind` | operator table keyed by (`MirBinaryOp`, Type) | needs a re-keyed table: the old one is keyed by `KotoKind` |
| `Cast(kind)` | `Convert` (bounds as `LowerPredicate`/`UpperPredicate` strings), `EraseClosure` for `Erasure`; no opcode for `Upcast` (handle copy) | cast table by `MirCastKind` | mostly direct |
| `Aggregate(Tuple/Adt/FixedArray)` | per-field stores into `EmissionSubslot`s; `FillArray` | per-field stores at Layout offsets | direct |
| `Aggregate(Closure)` | `CreateClosure` | closure environment text | direct |
| `Discriminant` | `LoadScalar` at the tag offset | `load` | direct |
| `Len` | constant, or `Sequence` / `LoadPointer` at handle offset 8 | `load` | direct |
| `Opaque` | — (generation refused) | — | n/a |
| `Adapt` | `MirCases.Adapt`, then as `Use`/`Cast`/`Ref` | same | direct |
| `Deref` (ref, uniq, raw) | `LoadPointer` | `load ptr` | direct |
| `Deref` (obj, objref, rc, arc) | `ObjectPayload` (header + 16), `ObjectBorrow` | same text | direct |
| `Field`/`Base`/`ConstIndex`/`Downcast` | `EmissionSubslot` (constant offset) or `BorrowAddress` | GEP i8 offset | direct |
| `Index` | `ElementAddress` with `Check=Bounds`, or `Sequence` `"ArrayAddress"`/`"SliceAddress"` | bounds check, then GEP by stride | direct |
| `PairLayer` | identity or `Deref` per view | same | direct |
| `Goto` | `Branch` (+ `Label`) | `br` | direct |
| `SwitchInt` | **no multiway opcode**: a chain of `Scalar` icmp + `ConditionalBranch` | `switch` | mismatch (O2's simplifycfg recovers it, H) |
| `Call` Fn / Requirement | `Call(FunctionAbi)` after resolution | `call` | direct |
| `Call` Virtual | `VirtualCall` (`Constant` = slot) | vtable load + `call` | direct (needs VTableDef) |
| `Call` Value | `CallValue` | load function + environment, `call` | direct |
| `Call` Intrinsic | `Call` of runtime ABIs; `ObjectTypeTest`; `TestPhaseEnter/Leave`; `Sequence "capacity"`/`"RawSlice"`; `FormattingStack` operand | intrinsic table (33 rows) | direct |
| target-less `Call` | `Call` + `Unreachable` | `call` + `unreachable` | direct |
| `Drop`, A2 Keep | `DestroyAggregate` / destructor call | glue call | direct |
| `Drop`, A2 Flag | old conditional `DestroyAggregate` reads `%liveSlot<Place>` (per-Place liveness, `LlvmModuleWriter.Aggregates.cs:101-112`, load at `:105`); A2 flags are per move path → must use `LoadScalar` of a DropFlag local + `ConditionalBranch` + unconditional `DestroyAggregate` | `load i8` + `br` + glue | old flag opcodes unusable |
| `Drop`, A2 Open | old model `DestroyPart`/`EndPartDestruction`/`StorePathFlag` keyed by old path flags (`LlvmModuleWriter.MovePaths.cs:7-105`) → must use generic opcodes per child row | per-row address + verdict | old opcodes unusable |
| `Return` | `ReturnVoid`/`ReturnScalar` | `ret` | direct |
| `Unreachable` | `Unreachable` | `unreachable` | direct |

### 2.2 Opcodes that encode the old IR (no MIR producer)

The 11 opcodes with no MIR producer:

- `Phi`: MIR has no phi (PLAN10 §5.7, alloca + mem2reg).
- String liveness flags: `DestroyStringIfLive`, `StoreLiveFlag`, `InitializeLiveFlag`.
- Partial-move path flags: `DestroyPart`, `EndPartDestruction`, `StorePathFlag`.
- Old match plans: `StringPattern`, `CompositePattern`, `PatternRead`. MIR desugars patterns into `Discriminant`/`SwitchInt`/`BinaryOp`/calls (`Mir/Build/MirBuilder.Match.cs`).
- `TestSnapshot`: `TestRecordValue` has no MIR producer (intrinsics survey §1.3).

Two more are usable but tied to one form:

- `MoveString`: copies only the string type, field by field.
- `SwapScalars`: implements `replace`/`exchange`/`swap`, which are Body-disposition kinds in MIR.

Another 4 are reached only through shims: `StringEquals`, `StringCompare`, `BuiltinComparison`, `TupleRelation`, behind the
`BuiltinEquals`/`BuiltinCompare` symbol calls (mir-state §2). Some of these opcodes write their own labels. For example
`DestroyStringIfLive` emits `destroy<id>:` and `b<target>:` itself (`LlvmModuleWriter.Strings.cs:28-48`). Block structure is
therefore not owned by `Label`/`Branch` alone, so A's MIR-block-to-label mapping must avoid those opcodes.

### 2.3 Operations as strings, keyed by syntax

- `Sequence` carries about 20 sub-opcode spellings in `ScalarOperator`:
  - in lowering: `Read`, `ArrayRead`, `ArrayStorageRead`, `SliceStorageRead`, `SliceAddress`, `ArrayAddress`, `SliceRange`,
    `SliceResolved`, `RawSlice`, `FixedStorage`, `DictionaryLayout`, `DictionaryEntryAddress`, `DictionaryBorrowStorage`,
    `DictionaryOwnStorage`, `fromEnd` (`grep 'EmissionOpcode\.Sequence' BodyLowering*.cs` gives 14 sites);
  - plus library properties dispatched in the writer: `indices`, `capacity`, `isEmpty`, `start`, `end`
    (`LlvmModuleWriter.Sequences.cs:36-345`, 17 `ScalarOperator` tests).
- Several of these are source-level library properties (`indices`, `isEmpty`, `start`, `end`) that MIR already expresses as
  `Len`/`Aggregate`/calls. A would leave them as dead writer branches.
- Arithmetic mnemonics (`sadd`, `srem`, `fadd`...) come from `NumericArithmetic`, keyed by `KotoKind` (21 refs). The Backend
  may not read syntax, so A needs a new table keyed by `MirBinaryOp`. B needs the same table, so this is a cost shared by
  both designs.

### 2.4 SSA naming and storage

- `Operation` is documented as the old ownership operation id (`EmissionModule.cs:141`). The writer derives several names
  from one id: `%v`, `%objectHeader`, `%checked`, `%overflow`, `%zero`, `%minusone`, closure tables, pattern helpers and
  erasure adapters (`LlvmModuleWriter.cs:268-281`; `Scalars.cs:207-265`).
- A would redefine the field as "a fresh value id per function". That is a contract change of the old representation during
  the freeze. D5 allows only stage-named mechanical follow-ups.
- Storage is likewise keyed by old Place ids:
  - `Slots` (`EmissionSlot(Place, ValueLowering)`);
  - `SlotAddresses` ("Place-indexed physical storage: local slot, logical parameter or hidden result");
  - `Subslots`;
  - `LiveFlags`/`PathFlags` (`EmissionModule.cs:150-170`).
- MIR locals and constant projections can fill these, but every other projection becomes an address instruction.

### 2.5 What A keeps alive, and what that costs under ARCH

- Under A, `EmissionModule` (433 lines) and the writer must survive R6-gen, so they move into the Backend. Their per-function
  part comes to about 3,050 lines: `WriteFunction` 280 plus 2,336 lines of per-instruction serializers.
- Once in the Backend they lose the old Emission's exemption. "Mir and the Backend take no baseline" (ARCH "Structure
  limits").
- Members over 60 statements (proxy) that A would have to split first:

| Member | Lines | Statements (proxy) |
| --- | ---: | ---: |
| `WriteSequence` (`LlvmModuleWriter.Sequences.cs:34`) | 375 | ~231 |
| `WriteScalar` (`LlvmModuleWriter.Scalars.cs:62`) | 301 | ~194 |
| `WriteFunction` (`LlvmModuleWriter.cs:259`) | 280 | ~163 |
| `WriteDictionaryIteration` (`LlvmModuleWriter.DictionaryIteration.cs:7`) | 150 | ~112 |
| `WriteObjects` (`LlvmModuleWriter.Objects.cs:7`) | 231 | ~100 |
| `WritePartDestruction` (`LlvmModuleWriter.MovePaths.cs:7`) | 99 | ~79 (dead under A) |
| `WriteBuiltinComparison` (`LlvmModuleWriter.BuiltinComparisons.cs:7`) | 98 | ~70 |
| `WriteAggregateDestructor` (`LlvmModuleWriter.Aggregates.cs:151`) | 119 | ~64 |
| `WriteAggregate` (`LlvmModuleWriter.Aggregates.cs:51`) | 99 | ~61 |

- `EmissionModule.Complete(out string? failure)` (`EmissionModule.cs:348-365`) breaks the "no failure text through
  `out string`" rule.
- PLAN10 §5.7, the ARCH target pipeline and the R0 ledger all say the physical layer goes. A's "named removal stage" is
  therefore a second codegen rewrite, i.e. B, at a later stage.

### 2.6 Size (H)

- MIR→`EmissionModule` lowering: about 2,500 lines. It needs the same per-form decisions as B (addresses, check kinds, ABI
  operand forms), plus slot, subslot and operand-kind plumbing and string field filling, but no text.
- Kept: about 3,050 writer lines + 433 `EmissionModule`.
- At R6-gen, A deletes about 3.5k fewer lines than B. Later it still has to write B (about 3.3k) to remove the layer.

## 3. Design B: direct `FunctionCodegen`

### 3.1 Shape

One `FunctionCodegen` per instance. An instance is a `MirDefKey` plus an Origin-erased `Subst` from the collector (instances
topic). Steps:

1. **Select the view.** Get it from `MirCases.Views(body, subst)` and select it once.
2. **Instance Types.** Each is `TyCtxt.Apply(local.Type, subst)` from a pooled per-instance cache (PLAN10 §13.3).
   `Layout.Of(Type)` supplies `ValueLowering` (layout topic).
3. **Prologue.**
   - The `define` line comes from `Abi.Of`, through `FunctionAbi.GetDefinition`, reused.
   - One `alloca` per non-zero-sized local: a bool is `alloca i8`, and a zero-sized local gets no slot.
   - Arguments that arrive by value are spilled; slot arguments are used in place.
   - One `alloca i8` + `store i8 0` per drop flag (`MirDropTable.FlagsOf`). Flags start clear.
   - Fixed-size allocas precede calls in the entry block (SPEC 21.2, as `LlvmModuleWriter.cs:309-318` does).
4. **Blocks.** One label `bb<n>` per block reached along runtime edges (`MirReach` without CheckTarget edges). Blocks only
   the checks reach are not emitted (PLAN10 §5.7).
5. **Statements** in order. After each statement location, the `MirFlagPoint`s of the selected view at that location write
   `store i8 1/0`.
6. **Terminators.**
   - The flag points of the terminator's location are written immediately before its branch. They therefore hold on every
     runtime successor edge. Check-only edges are not emitted.
   - `Goto` → `br`; `SwitchInt` → `switch`.
   - `Call` → call text, then the flag points, then `br`. A target-less `Call` is followed by `unreachable`.
   - `Drop` → A2 group by `Group(ordinal, block, view)`:
     - Keep: glue call.
     - Elide: nothing.
     - Flag: `load i8` + `br` + glue.
     - Open: per child row, project with the row's step, then apply its verdict. A component without a row follows the own
       bit.
   - `Return` → load `_0` + `ret`, or `ret void` with a result slot.
7. **Implicit Abort paths.** Non-`Proven` `BinaryOp`/`UnaryOp`/`Cast` and `Index` emit a condition and branch to a failure
   block that calls `__kimi_abort(reason, location, length, os)`, which is noreturn. This is today's
   `WriteArithmeticFailure` (`LlvmModuleWriter.Scalars.cs:441-490`), including the `select` of the division reason that
   `EmitFixture` exempts by name.
8. **Location text.** It comes from `SourceInfo.Syntax` → the Core runtime-location service (§5, F1) →
   `LlvmConstantPool.Intern(..., Location)`. `LlvmConstantPool` is syntax-free and reusable as is (`LlvmConstantPool.cs:7-60`).

At O2, `default<O2>` runs SROA/mem2reg, so allocas cost nothing. At O0 no opt pass runs (`NativeToolchain.cs:369-376`), so O0
code carries extra loads and stores. ARCH allows that when recorded ("O0 run time and compiler processing time may, when
recorded").

### 3.2 Parts and sizes (H)

| Part | Responsibility | Lines | Basis |
| --- | --- | ---: | --- |
| Driver and function frame | instance loop, `define`, allocas, args, labels, `Goto`/`SwitchInt`/`Return`/`Unreachable`, flag allocas | 300 | `WriteFunction` prologue ~100, `Write` 64 |
| Places | 7 projections → address; bounds-checked `Index` for fixed array, Array, Slice; `PairLayer` per view | 250 | `Elements` 66 + Sequence address/read paths ~150 |
| Operands and constants | Copy/Move (load or memcpy), `PairAcquire` (`MirCases.Reborrows`), 8 const kinds (string via pool, Location, FnItem address, TypeToken number) | 150 | `WriteOperand` 48, `StoreStaticString` 30 |
| Rvalues | Use, Ref, Aggregate (Tuple/Adt/FixedArray; enum tag + payload), Discriminant, Len, Adapt | 250 | `WriteAggregate` 99 |
| Arithmetic and casts | operator table by (`MirBinaryOp`, Type) and `MirCastKind`; checks per IMPL 21.3.3 (shared kernels, unit G7) | 300 | `Scalars` 491 + `Conversions` 110, minus the shared kernels |
| Calls and ABI | Fn, Requirement (resolved), Virtual (slot), Value; result slot, prepared slot, hidden location, environment; noreturn | 300 | `WriteCall` 77, Calls lowering |
| Intrinsics and legacy rows | 33 intrinsic rows; Body-disposition kinds through `LegacyRuntimeHelpers` rows | 350 | intrinsics survey §2 |
| Drops and flags | A2 rows, flag points, glue calls | 200 | `MirDrops` contract |
| Closures | environment ≤ 8 bytes inline (A5 §9.1), erasure adapter shim, value calls | 300 | `Closures` 311 |
| Objects and handles | MakeObj/MakeRc/MakeArc/Clone/TypeTest/Upcast, object `Deref`, virtual dispatch; module text from `ObjectRuntime` | 150 | `ObjectGenerationPlan.AddCall` 85 |
| Statics and entry | `staticEnsure` state machine, StaticInit, shutdown order, `Shim(Entry)` | 200 | PLAN10 §5.6.6; old `Statics` 66 is narrow |
| Tests | TestObserve/Message/RequireAbort, phase enter/leave around drops in test builds, `Shim(TestEntry)` through `TestRuntime` | 100 | `Testing` 72, `cs:499-508` |
| Formatting | FormatBuffer/Hint/Finish, stack buffer ≤ 1,024 bytes when bounded and direct, ErasedWriter | 150 | `Interpolation` 159 |
| **Total new** | | **≈3,000–3,300** | PLAN10 §10.2 target "コード生成 3,300" |

Promoted, not new: `LlvmModuleText` about 250, `ObjectRuntime` about 400, `LegacyRuntimeHelpers` about 970, scalar kernels
about 350, `LlvmConstantPool` 118. Layout/Abi are another topic.

Every member stays within 60 statements and every file within 5,000 tokens. A kind is one method reached through one switch,
the canonical-table form. For scale, `LlvmModuleWriter.Scalars.cs` is 492 lines for about 3,340 tokens, so one partial file
per part above fits.

### 3.3 O2 parity checklist (input list for the design)

| Item | Source | B obligation |
| --- | --- | --- |
| `align` on every load, store, alloca and constant | LLVM_OPTIMIZATION A09 (実装あり) | from Layout alignment |
| `noundef nonnull align 8 dereferenceable(24)` on SharedReference parameters | A07, A08, A10; `WindowsLowering.cs:30` | reuse `AbiParameter.Attributes` |
| `memcpy`/`memmove` declarations with `noalias nocapture readonly/writeonly` | A01, A02, A05, A06; `LlvmModuleWriter.cs:20` | reuse the declaration constant (module frame) |
| `noreturn` on Never functions, Abort and Exit; `unreachable` after diverging calls | D01; `FunctionAbi.cs:58` | `Abi.NoReturn` |
| no `willreturn`, no `mustprogress` | D03, D04 ("一律付与は仕様上不可"); tests pin them (tests-harness §1.5) | never emit |
| profile attribute group `#0` (uwtable, cpu, features, `denormal-fp-math`) | `LlvmModuleWriter.cs:15-17` | reuse Footer |
| `unnamed_addr` on pooled constants | E04 | reuse `LlvmConstantPool` |
| `poison` start value for formatting aggregates | F02 (限定実装) | inside the formatting kernels |
| bool stored as `i8` (zext/trunc around `i1`) | `Scalars.cs:94-140`; `EmitFixture` "no `store i1`" | alloca i8 for bool |
| names `__kimi_f<N>`, `__kimi_entry_body`; `%zero<N>`/`%minusone<N>` in the division `select` | `EmitFixture` (`ScalarEmissionTest.cs:133-136`) | keep these names, or reclassify the assertion (tests-harness R5 item) |
| inline closure environment ≤ 8 bytes, interpolation stack ≤ 1,024 bytes, zero-sized values without slots, ABI result slots, per-layout Array helpers, rc/arc release order | A5 plan §9.1 "Generated-code requirements" | each with a pipeline-neutral test |
| `nsw`/`nuw`/`inbounds` not emitted in ordinary code | B01–B03 | keep absent for parity; gains are a later, measured unit |

### 3.4 Contract draft (for the canonical-component row)

`FunctionCodegen` (Backend):
- **Responsibility.** The LLVM text of one instance body.
- **Inputs.** A validated `MirBody`, an instance `Subst` and its view, `MirDropTable` rows and points, the
  `Layout`/`Abi`/collector names, and the runtime-location service.
- **Output.** The definition text and the module-level requests it raised: helpers, constants, externals.
- **Invariants.**
  - One label per runtime-reached block.
  - No check-only block.
  - Every Abort path is noreturn.
  - Flags start clear.
  - The same input gives the same text.
- **Prohibitions.**
  - Reading syntax, HIR or borrow state.
  - Deciding acceptance or capability. The capability table decided those before it ran.
  - Failure text. A broken contract is `InternalInvariant_Kd` (AGENTS).
  - Allocating on a warm rewrite (181 zero-allocation sites measure `WriteIr`/`Validate`/`TryPrepare`, tests-harness §4).

## 4. LegacyRuntimeHelpers

### 4.1 What each candidate is

| Writer / data | Lines | Kind | Consumes today | R8 fate | Movable? |
| --- | ---: | --- | --- | --- | --- |
| `LlvmModuleWriter.Arrays.cs` (`ArrayRuntime` grow/reserve/shrink, per-layout helpers) | 433 | module-level | `module.ArrayHelpers` (`ArrayHelper`: kind, `FunctionAbi`, element `ValueLowering`, `AggregateLayout?` element/Option/Remainder, `RemainderFields`), `NeedsArrayRuntime` | Kimigayo (PLAN10 §5.8 order 1) | yes; members ≤ 44 lines, `WriteArrayHelpers` ~25 statements |
| `LlvmModuleWriter.Dictionaries.cs` (duplicate adapter, storage byte primitives) | 51 | module-level constants | `NeedsDictionaryRuntime`, `NeedsStorageBytes` | Kimigayo | yes |
| `LlvmModuleWriter.DictionaryHelpers.cs` | 114 | module-level | `DictionaryHelper` records + `module.FunctionReferences[Related]` + 7 library ABIs (`DictionaryAppend`, `RequireAbsent`, ...) set by `LlvmEmitter.cs:191-197` | Kimigayo | yes, after the library ABIs become the helper's own "callees" (intrinsics §6.2) |
| `LlvmModuleWriter.DictionaryStorage.cs` | 21 | module-level | `DictionaryInitialize` ABI | Kimigayo | yes |
| `LlvmModuleWriter.Formatting.cs` (writer wrappers, fixed offset 56) | 71 | module-level | `FormattingWrites`, `FormattingConversions` | Kimigayo Text/Writer | yes |
| `LlvmModuleWriter.StoredValues.cs` | 77 | text kernels used by Array/Dictionary helpers | `ValueLowering`, `AggregateLayout?` (only `Id` for `__kimi_drop_aggregate<Id>` and `NeedsDestruction`) | with them | yes, with a drop-name argument in place of `AggregateLayout` |
| `StringComparisons` constant (`LlvmModuleWriter.Comparisons.cs:7-43`) | 37 | module-level constant | `NeedsStringComparison` | Kimigayo `compareBytes` (PLAN10 §5.8) | yes |
| Formatting runtime splice (`LlvmModuleWriter.cs:185-211`) + `WindowsLowering.Formatting.cs` | 27 + 66 | runtime template + ABIs of ~27 Text/Buffer/Window/Writer kinds | `CompilerFunctionKind` range | R8 shrinks the `.ll.in` (disposition `:359-368`) | yes |
| Request records `ArrayHelperKind`…`DictionaryHelper`, `RemainderFields` and the `EmissionModule` request fields | ~75 | data | — | with them | yes, owned by the helper component |
| **Module-level subtotal** | **≈970** | | | | |
| `LlvmModuleWriter.DictionaryIteration.cs` | 157 | **per-instruction** (`WriteDictionaryIteration(..., EmissionInstruction)`, `Sequence` sub-opcodes `DictionaryLayout`/`EntryAddress`/`FixedStorage`/`Borrow`/`OwnStorage`) | `EmissionFunction`, `EmissionInstruction` | Kimigayo | **not verbatim**: split into decoding (stays old) and text kernels (move) first (unit G2) |
| `Sequence` `"FixedStorage"`, `"RawSlice"`, `"capacity"` branches | ~60 | per-instruction | same | Kimigayo / `Capacity` intrinsic (R3-9) | same treatment |

Not LegacyRuntimeHelpers, contrary to earlier estimates (emission-inventory §3.1, §7):

| Writer | Lines | Why not |
| --- | ---: | --- |
| `LlvmModuleWriter.Objects.cs`, `SharedObjects.cs` | 238 + 155 | PLAN10 §5.8 keeps rc/arc/obj runtime processing in code generation for good, so a permanent Backend `ObjectRuntime` |
| `LlvmModuleWriter.Statics.cs` | 66 | narrow `StaticScalarEntry` getter; MIR uses `staticEnsure` with a state machine and a shutdown table (PLAN10 §5.6.6), so it is replaced, not moved |
| `LlvmModuleWriter.Strings.cs` | 90 | per-instruction string liveness (`MoveString`, `DestroyStringIfLive`, live flags), so dead under MIR |
| `CompilerFunctionAdapters.cs` | 247 | a plan that emits Function Item adapters; R0 → "Backend/Shim と intrinsic 表" |
| `WriteArrayFillHelper` (`Aggregates.cs:7-49`) | 43 | fixed-array fill (`FillArray`), not `Array<T>`; permanent codegen runtime |
| `WindowsRuntime.ll.in` splice, Header/Footer/declarations, abort reasons | ~250 + 83 | permanent module frame / OS shim (PLAN10 §5.8) |

### 4.2 Reconciling 847 lines with "~1.5k"

The R0 ledger's six files total 847 lines, but `DictionaryIteration` (157) is per-instruction, which leaves 690 of verbatim
module-level text. Adding `StoredValues`, string comparisons, the formatting splice and ABIs, and the request records gives
about 970. The "~1.48k (H)" in emission-inventory §7 counts `CompilerFunctionAdapters` and `Strings.cs`, which cannot be
helpers, and PLAN10's ~1.5k is not attributed anywhere. The inline Body-disposition kinds are another matter. About 18 kinds
(Storage*, replace/exchange/swap; intrinsics §2.4) are served inline by old lowering, and their writer text sits in
per-instruction serializers. They become `LegacyRuntimeHelpers` rows only through extracted kernels (G2), or they are
rewritten in FunctionCodegen.

### 4.3 Contract a MIR backend calls it with

- **Form.** One class `LegacyRuntimeHelpers` in `src/Kimi/Compiler/Backend/`, with partials per family, marked
  `[Transitional("R8")]`. It needs a DECISIONS entry and an ARCH "Temporary structures" and canonical row (ARCH: "its future
  producer must record the R8 obligation").
- **Rows.** One row per Body-disposition `CompilerFunctionKind` (`Binding/KimiLibraryKinds.cs:172`, "until then the backend
  serves from the legacy runtime helpers"). A row maps (kind, element/key/value `ValueLowering` + drop name) to the
  `FunctionAbi` to call: a runtime function (`__kimi_array_reserve`, `__kimi_text_*`) or a per-layout helper
  (`__kimi_array_append_<elem>`). A row of an inline kind instead writes its kernel at the call site. A new kind adds a row,
  not a case.
- **Requests.** The component owns the request records that `EmissionModule` holds today (`ArrayHelpers`,
  `DictionaryHelpers`, `Needs*` flags, the `Dictionary*` ABIs, `FormattingWrites`/`Conversions`). The old `EmissionModule`
  holds one instance and the new module writer another.
- **Callees.** These are the Kimigayo library instances its text calls: the 7 `DictionaryStorage` functions and
  `clearEntries`/`findEntry` (intrinsics §6.2). The collector roots them whenever a row is used, or the MIR pipeline links to
  symbols it never generated.
- **Writing.** `Write(TextWriter, LlvmConstantPool)` writes the requested runtime text and helpers once, in today's order
  (`LlvmModuleWriter.cs:82-117`).
- **Inputs.** `AggregateLayout` becomes an element descriptor `(ValueLowering Value, string? Drop, bool Text)` plus the Option
  tag/payload offsets. That makes the component independent of the old pool and of the R5 Layout decision. The old path
  passes `"__kimi_drop_aggregate" + Id`, so the text stays identical.
- **Embedded resources.** The `.ll.in` files stay in `Compiler/Emission/` or get a `LogicalName`. Resource names
  `Kimi.Compiler.Emission.<name>` are read at `LlvmModuleWriter.cs:187-195, 731`, and `Kernel32ImportsTest.cs:29,46` and
  `generate-ryu.ps1:74` read them by path (critic §3.8).

## 5. ARCH conflicts and resolutions

| # | Conflict | Evidence | Resolution under B | Kind |
| --- | --- | --- | --- | --- |
| S1 | `WriteFunction` ~163 statements (proxy) | `LlvmModuleWriter.cs:259-538` | none: it stays in the old Emission, which has a baseline ("Members and files outside the old Analysis/Emission paths"), and dies at R6-gen. B never moves it | — |
| S2 | `WriteObjects` ~100 statements, 231 lines | `LlvmModuleWriter.Objects.cs:7-237` | behavior-preserving split before the move: (a) the fixed object runtime raw string as a `const` (1 statement); (b) `WriteObjectDescriptor` (type key, metadata, descriptor, vtable, base map constants); (c) `WriteObjectCreation` (the `__kimi_make_object<N>` body). Move with `SharedObjects` to Backend `ObjectRuntime` (G3). Legacy `.ll` equal | split |
| S3 | `AggregateLayoutPool.GetCore` ~124 statements, 275 lines; reads syntax (`AdtDef.Declaration(type)!.AttributeChain` for `#Layout("C")`, `declaration.Modifier` for Open, `Dictionary<FunctionKoto,int>` destructors, `GenericStoragePlan.IsGeneric(body)`) | `AggregateLayout.cs:200-474` (member), `:54` (destructor registry), `:259` (`AttributeChain`), `:300` (`ModifierKind.Open`), `:307` (`IsGeneric`) | split by responsibility (handles, sequence handles, field layout, destruction decision, offsets) while porting to `Layout.Of`. Published facts replace the syntax reads: a C-layout and Open column on `AdtDef` (Binding decides it today in `Binding.Attributes.cs:34` `HasCLayout`), `AdtDef.DestructorSymbol` (exists, `AdtDef.cs:132`), `TyCtxt.Sealed` (exists) for the `IsSealedObjectTarget` callback. Layout-topic unit | split + fact |
| S4 | `WriteAggregateDestructor` ~64 statements | `LlvmModuleWriter.Aggregates.cs:151-269` | not moved: DropGlue(Ty) is a shim of the instances topic. Reuse only if that design takes the text; then split it during the move | split if reused |
| S5 | `WriteDictionaryIteration` ~112 statements, per-instruction | `DictionaryIteration.cs:7-156` | G2: decoding stays in the old writer, the per-form kernels (layout, entry address, fixed storage, borrow/own storage) move to `LegacyRuntimeHelpers`, each < 60 statements | split |
| F1 | `SourceLocationTable.TryGet(Koto node, ...)` reads syntax (`node.CodeContext.SourceDocument`, `node.Span.Start`) | `SourceLocationTable.cs:21-61`; callers `BodyLowering.cs:544`, `LlvmEmitter.cs:168` | published fact owned by Core: the runtime location text of a `SourceRef` (SPEC 22.5.1–22.5.2), e.g. `Compilation.RuntimeLocation(SourceRef, projectDirectory)`, with the table moved to Core and keyed by `SourceRef`. Core owns syntax identity (ARCH canonical "Syntax identity"); `SyntaxOf` is not a location service (critic §3.7). Old callers pass `SourceRef.Of(node)`; text identical (G4) | fact |
| F2 | Runtime type identity key `ObjectGenerationPlan.WriteKey` walks declaration parents (`WriteContainers`, `Koto.Parent`), reads `CodeContext.Kotonoha`, `Project.ProjectFile.PackageId/Version`, and for closures the source path and `Declaration.Span.Start` | `ObjectGenerationPlan.cs:246-307` | published Binding fact: the declaration key of a `BindingSymbol` (`package@version:module:containers:name`, plus `path:offset` for a closure), computed once per symbol. The Backend composes `Semantics:Kind:<declaration key>:Length<components>` from the Type alone. Old `WriteKey` calls it; key text identical (G5). It crosses phases, so it gets the full process unless S0's approved design fixes its contract (DECISIONS Q6 (1)) | fact |
| F3 | `TestRuntime.Create(TestCatalog, IReadOnlyDictionary<FunctionKoto, FunctionAbi>)`; `TestCase.Function` is a `FunctionKoto` | `src/Kimi/Testing/TestRuntime.cs:13-36`, `TestCatalog.cs:14` | `TestCatalog` publishes a `BindingSymbol` column (or `MirDefKey(Test)`), and `Create(TestCatalog, ReadOnlySpan<string> names)` takes the entry names in case order. The old path maps its own `FunctionKoto`→ABI names; text identical (G6) | fact |
| F4 | `FunctionAbiPool` keyed by `FunctionKoto`, reads `BoundSymbol`/parameter `BoundType`; `FunctionAbi.Supports` is a capability gate | `FunctionAbiPool.cs:103,182`; `FunctionAbi.cs:27` | Abi topic: `Abi.Of(instance signature)`; delete `Supports` (ARCH "No boolean `Supports*`") | fact (other topic) |
| F5 | LINQ in movable files | `WindowsLowering.Abort.cs:61` (`string.Any`) | rewrite during the move (ARCH "At R5 ... forbid System.Linq.Enumerable in ... Backend") | edit |
| F6 | `LlvmModuleWriter.SymbolFromName` is used by `NativeToolchain.cs:448`; `ExternalName` by `LlvmEmitter.cs:332` | as cited | move with the module frame (G0); re-point the toolchain | edit |
| E | Owner exceptions | — | **none needed under B.** Under A, S1, S5 and the other 7 members of §2.5 would each need a split or an owner exception before the writer could outlive R6-gen | — |

## 6. A fair "measured" comparison, and the recommendation

What measurement can tell A and B apart:

1. **Run time (O2).** It cannot. If B reuses the instruction text kernels (unit G7), A's writer and B produce the same
   instruction text for the same MIR. Per-function `opt -O2` metrics then differ only by naming. What matters is MIR codegen
   against legacy, and B's first slice measures that.
2. **Compile time.** A adds one record materialization per instruction. The cost of record-to-text writing today is the
   `LlvmIrWriting` stage: 0.016–0.066 ms against 0.16–2.4 ms of `EmissionLowering` and 21–43 ms per compilation, about 0.04–0.23 %
   of the total. That comes from the R3 pipeline series, functions 1/32/128 with and without branches
   (`artifacts/benchmarks/r3-u16c-v14/exit-compiler-pipeline-1/phases.json`). A's extra pass is of that order (H), well
   inside matched-run variation (ARCH P4).
3. **Code size, structure and stage count.** These do discriminate, and they come from paper with good accuracy, because A's
   retained code already exists: §2.5 and §2.6 against §3.2.

So a fair step 0 without building both:

- **(a)** A on paper: the mapping table (§2.1), the unusable opcodes (§2.2), the strings and syntax keys (§2.3), the retained
  lines and limit splits (§2.5), and the removal stage it implies.
- **(b)** B's first slice (units C0 + C1) built for real under the codegen selector and measured against legacy on the
  scalar fixture families:
  - stdout, stderr and exit at O0 and O2;
  - per-function `opt -O2` instruction and basic-block counts and an instruction hash in the spirit of the O2 corpus's
    `sameCode`;
  - warm codegen time and allocations on the same pipeline inputs as `EmissionLowering` + `LlvmIrWriting`.
- **(c)** Optionally, if the owner wants a number for A: a throwaway A spike of the same slice (about 400–600 lines) timed on
  the same inputs. Its only output is item 2's number.

**Recommendation.** Take B. The owner can waive the measured A spike (c) and the "both measured" wording of A5 plan §9.1 step 0:

- PLAN10 §5.7 already decided "物理 IR 層は置かない" on 2026-10-10. Its reason was that the physical/writer split existed
  only because the physical IR had feature-specific opcodes. This study confirms that reason still holds: 11 of 49 opcodes
  and about 20 sub-opcode strings encode the old IR or syntax-level forms.
- The decision is recorded in the ARCH target pipeline and in the R0 ledger.
- AGENTS asks a departure from a decision to name the reason that no longer holds, and A5 §9.1 names none.
- Item 1 makes a run-time spike uninformative, and item 2 makes a compile-time spike nearly so.

The AGENTS "two independently written designs" for the codegen contract can be met by S0's B design plus an independently
written review of §2 (A on paper). The owner decides whether that satisfies step 0.

## 7. Owner decisions

1. **Waive A's spike.** Is the measured A spike waived, with A evaluated on paper and B's first slice measured against legacy
   (§6)? Trade-off: a waiver saves one throwaway unit (about 500 lines) and loses only a compile-time number of ~0.1 % scale.
   Not waiving reopens nothing, but delays C1.
2. **Shared-kernel rule.** Do the old writer and the MIR codegen share promoted Backend components during shadow (G0–G3,
   G7: one copy of each text rule), or does the new codegen copy them (old writer untouched until R6-gen)? Trade-off: sharing
   avoids a second copy of the checked-arithmetic and helper rules (AGENTS symptom list). The cost is that every kernel change
   during R5 must keep the legacy `.ll` byte-identical.
3. **IR-shape assertions.** Are `EmitFixture`'s names (`__kimi_f<N>`, `%zero<N>`/`%minusone<N>`) a contract the MIR codegen
   keeps, or are they reclassified as incidental? This is already a tests-harness R5 item. B can keep them at no cost.
4. **O0 run-time regression.** Is it accepted as recorded? Alloca-per-local with no opt pass at O0 adds loads and stores.
   Fixture `.timeout` sidecars could trip (H).
5. **Review weight.** Is the codegen core one group under S0's design (Q6 (1): one design and one review per slice)? A shared
   group review (Q6 (2)) would hold every slice out of dev until the end, which is long for this many slices.

## 8. Risks

| Risk | Effect | Mitigation |
| --- | --- | --- |
| A2 incomplete for a body (arena limit, or the program skipped on findings; `Group` returns -1 for both "check-only" and "not analysed", mir-state §1.5) | codegen would emit no drop where one is due | codegen treats -1 at a runtime-reached `Drop` as `InternalInvariant_Kd`; the generation gate must also require A2 completeness (`Compilation.Pipeline.cs` `Generates` checks only records and findings) |
| Per-view flags: a flag point applies only in its views | wrong drop under a multi-case body | per-instance view selection once; tests over `tests/mir/cases.kimi`-style bodies |
| O0 slowdown hits fixture timeouts | false native failures under the selector | record O0 times per slice; raise no timeout without a recorded reason |
| `TestRecordValue`, `TestPhaseEnter/Leave` have no MIR producer | test-failure output (frame kind 7 Values) differs per pipeline | intrinsics/builder unit before C10; per-pipeline expectation form (critic §4.3) |
| Shared kernels drift | legacy `.ll` changes during R5 | every G and C unit re-runs the legacy `.ll` comparison (VERIFICATION "Behavior-preserving commits") |
| Warm zero-allocation (181 sites) | failures under the selector until pooled | pooled storage from C0; C11 runs the full set |
| `MirBuilder.Calls.cs` restructure-before-touch rule; R4 hold on `Mir/Build`, `MirValidator`, `MirTypes` (PLAN §3) | codegen units that need a MIR helper wait | codegen reads MIR only; a missing MIR fact (e.g. the StaticInit body a `staticEnsure` names, third consumer) is one `MirProgram` member under a hold |

## 9. Corrections to the surveys and plans

1. **DictionaryIteration in the R0 ledger.** The R0 ledger (`disposition.md`, row `LlvmModuleWriter.DictionaryIteration.cs`)
   and emission-inventory §7 count it in `LegacyRuntimeHelpers`' 847 lines. It is a per-instruction serializer
   (`WriteDictionaryIteration(TextWriter, ..., EmissionInstruction)`, `DictionaryIteration.cs:7`; `Sequence` sub-opcodes).
   It cannot move verbatim; a decode/kernel split comes first.
2. **Helpers that are not helpers.** Emission-inventory §3.1 lists `Objects`, `SharedObjects` and `Statics` among module-level
   helpers a FunctionCodegen keeps, and §7 counts `Strings.cs` and `CompilerFunctionAdapters` toward ~1.48k. Under PLAN10
   §5.8, objects and rc/arc are permanent runtime, not legacy. `Statics` is replaced by `staticEnsure` (§5.6.6). `Strings.cs`
   is per-instruction string liveness. `CompilerFunctionAdapters` is a plan.
3. **Members over 60 statements.** Critic §3.7 lists `WriteObjects` ~86, `GetCore` ~98 and `WriteFunction` ~145. With a
   different proxy the counts are ~100, ~124 and ~163, and six more members exceed 60: `WriteSequence` ~231, `WriteScalar`
   ~194, `WriteDictionaryIteration` ~112, `WritePartDestruction` ~79, `WriteBuiltinComparison` ~70, `WriteAggregateDestructor` ~64,
   `WriteAggregate` ~61. All of these are proxies (H).
4. **The ~9,100 uncoupled lines.** A5 plan §9.1 says of them "Even these consume `EmissionModule`". Only the writer (4,601)
   and `EmissionModule` do. The toolchain, the runtimes, `FunctionAbi` and the constant pool do not
   (`EmissionArtifacts.cs:27,68` is the toolchain's only generator call).
5. **Writer cost was never measured.** No survey records that the writer stage is negligible (`LlvmIrWriting` ≤ 0.07 ms,
   `phases.json` above). That fact removes compile time as an A/B discriminator.

## 10. Units

Stage labels: R5 restructuring (G) runs at R5 start under D5's "R5 IR helper move and shared-parts organization" (PLAN10
§12.1). Codegen slices (C) run in R5 shadow under the codegen selector. Verification rows follow AGENTS "Verification and
Delivery":

- **Compiler**: unit checks of the related tests, plus a Session at the end of the working session.
- Behavior-preserving units also need the VERIFICATION "Behavior-preserving commits" evidence. The legacy `.ll` under
  `fixtures-<Config>` must equal the previous run's, which needs the tests-harness R5 `.ll` comparison tool.
- Native fixtures need explicit `-Fixtures 'Family*.ll'`.

| id | title | stage | change (owner, files) | prerequisites | deliverable | verification | net size | diff. | review weight | shared MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- |
| S0 | Codegen shape design (step 0) | R5 step 0 | design doc on instruction (`draft/Sketches/R5 Step 0/codegen-design.md`); DEC entry draft for `FunctionCodegen`, `LlvmModuleText`, `ObjectRuntime`, `LegacyRuntimeHelpers`, the facts F1–F3 | this report; layout and instances designs | B contract (§3.4), parity list (§3.3), A on paper (§2), measurement plan (§6), owner decisions §7 | doc review only | +0 code | 3 | full process (new components, cross-phase contract with A2/collector) | no |
| G0 | Promote the module frame | R5 | Backend `LlvmModuleText`: Header, Footer, memory/overflow declarations, runtime template load and splice, `ExternalName`/`SymbolFromName`, `WriteExternals`, number formatting, moved from `LlvmModuleWriter.cs`; old writer and `NativeToolchain` call it | S0 | Backend directory with its first component + ARCH row | build; `Kernel32ImportsTest`, `EmissionArtifactsTest`, a scalar sample; legacy `.ll` equal; Session | +270/−250 | 1 | one design + one review (contract fixed by S0, Q6 (1)) | no |
| G1 | Introduce `LegacyRuntimeHelpers` | R5 | Backend `LegacyRuntimeHelpers*.cs` `[Transitional("R8")]`: Array runtime and helpers, Dictionary adapter/storage/helpers, formatting wrappers and splice, `StoredValues`, string comparisons; request records leave `EmissionModule`; element descriptor replaces `AggregateLayout`; `BodyLowering` request sites follow mechanically | G0 | the one owner of legacy helper requests and text; DEC + ARCH "Temporary structures" row | `DynamicArrayCostTest`, `DictionaryLibraryTest`, `Utf8ConsoleTest`, `KimiLibraryShapeTest`; native `Array*.ll`, `Dictionary*.ll`, `Utf*.ll` at O0/O2; legacy `.ll` equal | +1,000/−970 | 2 | one design + one review | no |
| G2 | Extract inline storage kernels | R5 | split `WriteDictionaryIteration` and the `Sequence` `FixedStorage`/`RawSlice`/`capacity` branches into decoding (old) + kernels (`LegacyRuntimeHelpers` rows) | G1 | kernels callable with plain names/offsets; every member < 60 statements | `Dictionary*`, `Slice*`, `Fixed*`, `Raw*` focused tests and fixtures; legacy `.ll` equal | +220/−170 | 3 | one review | no |
| G3 | Promote the object runtime | R5 | split `WriteObjects` (S2); move with `SharedObjects` and `LlvmConstantPool` to Backend `ObjectRuntime`; `ObjectCreation` keeps a constant index | G0 | permanent `ObjectRuntime`; `WriteObjects` split | `SharedObjectRuntimeTest`, `SharedArcRuntimeTest`, `Object*`/`Shared*`/`Dynamic*` fixtures; legacy `.ll` equal | +420/−400 | 2 | one review | no |
| G4 | Runtime location by `SourceRef` | R5 | Core: `SourceLocationTable` keyed by `SourceRef` (F1), callers in `BodyLowering.cs:544`, `LlvmEmitter.cs:168` | S0 | Backend-legal location service | `AbortEmissionTest`, location stderr fixtures (`Overflow*`); legacy `.ll` equal | +60/−50 | 2 | one design + one review (fixed by S0); else full (moves a responsibility to Core) | no |
| G5 | Declaration key for runtime type identity | R5 | Binding: declaration key per `BindingSymbol` (F2); `ObjectGenerationPlan.WriteKey` composes it | S0 | syntax-free type-key composition | `Object*`, `Dynamic*`, `Inherited*` tests and fixtures (type tests); semantic snapshot; legacy `.ll` equal | +70/−50 | 2 | full process (crosses phases) unless S0 fixes the contract, then one design + one review | no |
| G6 | Test catalog symbol column | R5 | Testing: `TestCase` symbol column; `TestRuntime.Create(catalog, names)` (F3) | S0 | Backend can write `Shim(TestEntry)` without `FunctionKoto` | `Testing*` tests; `test-testing.ps1` selected; legacy `.ll` equal | +20/−12 | 1 | one review | no |
| G7 | Shared scalar text kernels | R5 | Backend `LlvmScalarText`: checked arithmetic, division/remainder/shift checks, conversions with bounds, arithmetic failure block; old `WriteScalar`/`WriteConversion` decode and call them (splits `WriteScalar` as a side effect) | G0; owner decision §7.2 | one copy of the checked-scalar rules | `ScalarEmissionTest`, `Overflow*`, `Wrapping*`, `Bitwise*`, `BitConversionTest`; legacy `.ll` equal | +380/−340 | 2 | one review | no |
| C0 | Codegen driver, selector and frame | R5 | Backend `ModuleCodegen.cs`, `FunctionCodegen.cs` (contract), selector wiring in `Core/Compilation.Pipeline.cs` (`WriteIr`/`Validate`) and `EmissionArtifacts.Publish`; prologue, allocas, labels, `Goto`/`SwitchInt`/`Return`/`Unreachable`, Use/Const of scalars, `writeLine` legacy row, `Shim(Entry)`; capability rows for the slice (others located `Unsupported_Kd`) | G0, G1, G4; collector minimal (non-generic roots), Layout/Abi for scalars; selector design | first MIR-generated program (hello, scalar locals) | focused tests with the selector; `Minimal*`/`Scalar*` fixtures at O0/O2; warm zero-allocation on its inputs; legacy unchanged; Session | +500/−10 | 3 | one design + one review (S0); first consumer of the selector (A5 §9.1) | no (`Compilation.Pipeline.cs` is Core; coordinate with R4) |
| C1 | Scalars and checked arithmetic | R5 | `FunctionCodegen.Arithmetic.cs`, `.Casts.cs`: operator table by `MirBinaryOp`/`MirCastKind`, Abort paths, bool as `i8`, Proven omission | C0, G7 | scalar fixtures equal across pipelines; step-0 measurement (§6 b) | `Scalar*`, `Overflow*`, `Wrapping*`, `Bitwise*`, `Char*`, `Float*` fixtures O0/O2; per-function O2 metrics vs legacy | +350 | 2 | one design + one review | no |
| C2 | Places and aggregates | R5 | `.Places.cs`, `.Rvalues.cs`: 7 projections incl. bounds-checked `Index`, `PairLayer` per view, Aggregate Tuple/Adt/FixedArray, Discriminant, Len, memcpy, zero-sized values | C1; Layout for aggregates | aggregates and matches generated | `Aggregate*`, `Struct*`, `Fixed*`, `Element*`, `Borrowed*`, `Composite*`/match fixtures | +500 | 3 | one design + one review | no |
| C3 | Calls and ABI | R5 | `.Calls.cs`: Fn and Requirement callees (collector names), result and prepared slots, hidden location, noreturn, `__kimi_f<N>` naming | C2; collector with Substs; Abi; Requirement resolution fact | generic and non-generic calls | `Function*`, `Result*`, `Never*`, `Generic*`, `Requirement*`, `Associated*` fixtures | +350 | 3 | one design + one review | possibly (`MirProgram.Callee` for Requirement/Shim): hold if touched |
| C4 | Drops and flags | R5 | `.Drops.cs`: A2 rows and flag points per view, DropGlue calls, string destruction; -1 group at a runtime-reached Drop → `InternalInvariant_Kd` | C3; DropGlue shim (instances topic) | owned values released exactly once | `String*`, `Owned*`, `Copy*`, `Ownership*`, destructor and partial-move fixtures; native allocation audits | +250 | 4 | one design + one review | no |
| C5 | Closures | R5 | `.Closures.cs`: environment (≤ 8 bytes inline), `Cast(Erasure)` adapter shim, Value callee calls, FnItem constants | C4; closure shims | closures and erased calls | `Closure*`, `Callable*`, `Concrete*`, `Callback*` fixtures; inline-environment test | +300 | 3 | one design + one review | no |
| C6 | Objects and handles | R5 | `.Objects.cs`: MakeObj/Rc/Arc, Clone, TypeTest, Upcast, object `Deref`, Virtual calls; module text via `ObjectRuntime` | C4, G3, G5; VTableDef and override applicability facts | objects and virtual dispatch | `Object*`, `Shared*`, `Dynamic*`, `Virtual*`, `Inherited*` fixtures; rc/arc order tests | +250 | 3 | one design + one review | no |
| C7 | Statics and entry | R5 | `.Statics.cs`: `staticEnsure` state machine, StaticInit calls, shutdown order, cycle/shutdown Aborts | C3; StaticInit item (collector) | static programs beyond scalar immutables (rest `Unsupported_Kd` per PLAN10 §5.6.6) | `Static*` fixtures; `tests/mir/statics.kimi` sources natively | +250 | 3 | one design + one review | yes if the StaticInit lookup becomes a `MirProgram` member (third consumer) |
| C8 | Library rows | R5 | `.Intrinsics.cs` + `LegacyRuntimeHelpers` rows: Array (8), Storage* (15), replace/exchange/swap, Text/Buffer/Window/Writer, Raw and storage intrinsics; callees rooted | C4, G1, G2 | Body-disposition kinds served | `Array*`, `Dictionary*`, `Slice*`, `Raw*`, `Compiler*` fixtures; `DynamicArrayCostTest` | +350 | 3 | one design + one review | no |
| C9 | Formatting | R5 | `.Formatting.cs`: FormatBuffer/Hint/Finish, stack ≤ 1,024 bytes, ErasedWriter, BuiltinFormat shim | C8 | interpolation and writers | `Utf*` fixtures; `Utf8ConsoleTest` (33-byte stack, no heap) | +200 | 3 | one design + one review | no |
| C10 | Tests | R5 | `.Testing.cs`: TestObserve/Message/RequireAbort, phase enter/leave around drops, `Shim(TestEntry)` through `TestRuntime` | C4, G6; builder producers for phases (intrinsics topic) | `kimi test` artifacts from MIR | `Testing*` tests; `test-testing.ps1` O0/O2 | +120 | 2 | one design + one review | no |
| C11 | Warm allocation under the selector | R5 | pooling fixes across `FunctionCodegen` | C0–C10 | 181 zero-allocation measurements pass under the selector | `Purpose=Allocation` set with the selector; Session | +50/−20 (H) | 2 | one review | no |
| D1 | Delete the physical IR | R6-gen | delete `EmissionModule.cs`, per-instruction serializers and the rest of `LlvmModuleWriter*`; drop the old-path adapters of G1–G6 | R6-gen switch unit | Backend is the only generator | Session; all-fixture sweep O0/O2; `ArchitectureRulesTest` zero-reference rows | −3,600 | 2 | one review | no |
| D2 | Remove old adapters of Core/Testing facts | R6-check | remove the `Koto`/`FunctionKoto` overloads left for the old Analysis users of F1/F3, if any | R6-check | no syntax-keyed location or test lookup | Session | −30 | 1 | one review | no |

Size totals (H): G units about +2,440/−2,240, mostly moves. C units about +3,520 of new codegen. D1 about −3,600 at R6-gen
(on top of R6-gen's deletion of `BodyLowering*` and the plans, which other topics own).
