# R5 Step 0 Audit (S1)

Purpose: one row per item that R5/R6 must give a new owner: its old site, its new owner and the unit of
[the plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md) §4 that closes it (OQ1). A record, not a decision.

Base: `origin/dev` = `041ced79`; every `path:line` is at the base. Paths: `C/` = `src/Kimi/Compiler/`; a bare file name is in `C/Emission/`;
`BL.` = `BodyLowering.`, `WL.` = `WindowsLowering.`; `Bd/` = `C/Binding/`, `M/` = `C/Mir/`, `A/` = `C/Analysis/`; `B/` = the new Backend.

Method (grep at the base; comment lines skipped):

- Groups 1–2: the enums in `Bd/KimiLibraryKinds.cs:26-104, 108-148` and `IntrinsicDisposition.Of` (`:183-219`); old sites from
  `grep -n 'CompilerFunctionKind\.' C/Emission/*.cs` and the dispatch in `BL.Calls.cs:135-235`; producers from
  `grep -rn 'IntrinsicId\.<Id>\b' src/Kimi --include=*.cs`.
- Group 3: `M/MirVocabulary.cs`; producers from `grep -rno 'Mir(Callee|Const|Cast|Aggregate)Kind\.[A-Za-z]*' C/Mir/Build`.
- Group 4: the five files PD6 names (`wc -l`: 2,054 lines), their driver `LlvmEmitter*.cs` and `A/OwnershipAnalysis.Instances.cs`.
- Group 5: references in `C/Emission/*.cs` to members of `partial class Binding` through `c.Binding.`, `compilation.Binding.`,
  `….CodeContext.Compilation.Binding.`, `this.binding`/`this.instanceBinding`, Binding-typed locals `binding` (`GenericStoragePlan.cs:261, 403,
  509`, `GenericStoragePlan.Destructors.cs:28`, `BL.Updates.cs:115`, `DefaultGenerationPlan.cs:36`) and static `Binding.M`; each occurrence is
  one site (two on one line count twice).

Counts:

| Group | Plan (base `5c42b577`) | Measured | Difference |
| --- | --- | --- | --- |
| 1 `CompilerFunctionKind` | 75: 53 Body, 18 Intrinsic, 4 Shim | 75: 53 Body, 18 Intrinsic (13 Ids + 5 `Fail` codes), 4 Shim | none |
| 2 `IntrinsicId` | 33 | 33: 23 with a producer, 10 without | none |
| 3 MIR forms | not counted | 26: 5 callee, 8 const, 9 cast, 4 aggregate (3b: the other vocabularies) | — |
| 4 Old collector responsibilities | 11, not listed | 18 rows; the five files are 2,054 lines, as planned | split by new owner; not mappable one to one |
| 5 Emission→Binding | 42 members, 82 sites | 42 members, 82 sites (43 and 83 with the nested type `Binding.VirtualSlot`) | none |
| 5 by class | 29 P, 36 D, 6 N, 7 T (sum 78) | 26 P, 33 D, 16 N, 7 T (sum 82) | N includes K3's `Erase` (6 sites) and F4 (3) |

Group 2 producers: 14 Ids through the table, 9 only through the builder. Without a producer: the 7 R8 Ids (PD21), `TestRecordValue` (F8 adds
one) and `TestPhaseEnter`/`TestPhaseLeave` (codegen writes them, PD22).

UNOWNED (findings for the planning session):

1. `CompilerFunctionKind.WriteLineUtf8` (`writeLine(text: Text.Utf8Slice)`, `WL.Formatting.cs:60`): C0b2 names only the `writeLine(string)` row,
   and C9a's "25 fixed-symbol rows" are exactly `TextFixed`..`WriterStatus` without `TextWriter`, which excludes it. Candidate: C9a (26 rows) or C0b2.
2. (3b, beyond the plan's list) `MirStatementKind.StorageLive`, `StorageDead`, `FakeRead`, `EndBorrow`: no unit names them. They generate
   nothing (legacy writes no lifetime markers), but under PD11 an unlisted form is `Unsupported_Kd`, so C0b1's coverage rows must list them.

Not classified with certainty: `PrepareTypeStorage` (group 5, a post-Bind preparation counted as D; S3 may list it among the mutations it removes);
`InstantiateForwardedCall` (P for Fn callees through the existing `TyCtxt.Compose`, N for requirement callees through F5b; counted N).

## 1. `CompilerFunctionKind` (75)

Class: Body = a Fn callee that a backend row (a legacy runtime helper or `FunctionCodegen`) serves until R8 writes the body;
Intrinsic = `IntrinsicId`; Shim = a built-in witness generated per instance (a Backend mono item, PD3). Kind line numbers are
`Bd/KimiLibraryKinds.cs`.

| Kind | Class | Old sites | New owner | Unit | Note |
| --- | --- | --- | --- | --- | --- |
| WriteLine :29 | Body | `BL.Calls.cs:193`; `WL.cs:59, 99`; `BL.Interpolation.cs:123` | legacy row over the runtime ABI table (P0b) | C0b2 | `writeLine(string)` |
| Abort :30 | Intrinsic Abort | `BL.Calls.cs:193, 430`; `WL.cs:81, 99` | Abort row; reasons P0b, location text P5 | C8a | caller location C3b; `$abort` SourceInfo F9 |
| Replace :31 | Body | `BL.Updates.cs:9, 91` (via `BL.cs:318`); `CompilerFunctionAdapters.cs:88, 182` | `FunctionCodegen` row | C8b | |
| Exchange :32 | Body | as Replace; `BL.Strings.cs:276` | `FunctionCodegen` row | C8b | |
| Swap :33 | Body | as Exchange | `FunctionCodegen` row | C8b | |
| MakeObj :34 | Intrinsic MakeObj | `ObjectGenerationPlan.cs:157-241`; `BL.Calls.cs:158`; `CompilerFunctionAdapters.cs:214` | `B/Codegen/ObjectDescriptors.cs` over `ObjectRuntime` (P3b) | C6a | edge K8 |
| MakeRc :35 | Intrinsic MakeRc | as MakeObj | as MakeObj | C6a | edge K8 |
| MakeArc :36 | Intrinsic MakeArc | as MakeObj | as MakeObj | C6a | edge K8 |
| Clone :37 | Intrinsic Clone | `BL.Calls.cs:172, 194-201`; `WL.cs:61-62`; `CompilerFunctionAdapters.cs:80` | handle operation over `ObjectRuntime` | C6a | |
| TestTempDirectory :38 | Intrinsic TestTempDirectory | `BL.Calls.cs:193`; `WL.cs:82, 99` | test intrinsic row | C10a | |
| ArrayReserve :39 | Body | `BL.Arrays.cs:207` (dispatch `BL.Calls.cs:150`); `BL.CompilerEntries.cs:22` | legacy row over `LegacyRuntimeHelpers` arrays (P1b) | C8d | one of "8 operations" |
| ArrayAppend :40 | Body | `BL.Arrays.cs:222`; `BL.CompilerEntries.cs:32` | as ArrayReserve | C8d | |
| ArrayInsert :41 | Body | `BL.Arrays.cs:223`; `BL.CompilerEntries.cs:33` | as ArrayReserve | C8d | |
| ArrayPop :42 | Body | `BL.Arrays.cs:247`; `BL.CompilerEntries.cs:47` | as ArrayReserve | C8d | |
| ArrayRemove :43 | Body | `BL.Arrays.cs:256`; `BL.CompilerEntries.cs:56` | as ArrayReserve | C8d | |
| ArrayClear :44 | Body | `BL.Arrays.cs:302` (default arm); `BL.CompilerEntries.cs:70` | as ArrayReserve | C8d | |
| ArrayShrinkToFit :45 | Body | `BL.Arrays.cs:208`; `BL.CompilerEntries.cs:23` | as ArrayReserve | C8d | |
| ArraySwap :46 | Body | `BL.Arrays.cs:290`; `BL.CompilerEntries.cs:65` | as ArrayReserve | C8d | |
| TextFixed :47 | Body | `WL.Formatting.cs:21`; `BL.Calls.cs:398-400, 467`; `CompilerFunctionAdapters.cs:228` | legacy row over formatting helpers (P1d) | C9a | runtime splice `LlvmModuleWriter.cs:200` |
| TextHeap :48 | Body | `WL.Formatting.cs:22` | as TextFixed | C9a | |
| TextWriter :49 | Intrinsic ErasedWriter | `WL.Formatting.cs:23`; `BL.Calls.cs:358-362, 392`; `BL.Formatting.cs:26`; `GenericStoragePlan.cs:194`; `CompilerFunctionAdapters.cs:238` | ErasedWriter dispatch | C9b | |
| TextUtf8 :50 | Body | `WL.Formatting.cs:30` | as TextFixed | C9a | |
| TextValidateUtf8 :51 | Body | `WL.Formatting.cs:31` | as TextFixed | C9a | |
| TextToString :52 | Body | `WL.Formatting.cs:27`; `BL.Calls.cs:228`; `GenericStoragePlan.cs:237` | built-in row; user-witness wrapper | C9a, C9b | |
| TextTryFormat :53 | Body | `WL.Formatting.cs:28`; `BL.Calls.cs:398`; `GenericStoragePlan.cs:237, 560` | built-in row; user-witness wrapper | C9a, C9b | |
| TextRelease :54 | Body | `WL.Formatting.cs:29` | as TextFixed | C9a | |
| FixedBufferBytes :55 | Body | `WL.Formatting.cs:32` | as TextFixed | C9a | |
| FixedBufferText :56 | Body | `WL.Formatting.cs:37` | as TextFixed | C9a | |
| FixedBufferValidate :57 | Body | `WL.Formatting.cs:42` | as TextFixed | C9a | |
| FixedBufferClear :58 | Body | `WL.Formatting.cs:47` | as TextFixed | C9a | |
| FixedBufferReserve :59 | Body | `WL.Formatting.cs:52` | as TextFixed | C9a | |
| FixedBufferIntoText :60 | Body | `WL.Formatting.cs:54` | as TextFixed | C9a | |
| HeapBufferBytes :61 | Body | `WL.Formatting.cs:32` | as TextFixed | C9a | |
| HeapBufferText :62 | Body | `WL.Formatting.cs:37` | as TextFixed | C9a | |
| HeapBufferValidate :63 | Body | `WL.Formatting.cs:42` | as TextFixed | C9a | |
| HeapBufferClear :64 | Body | `WL.Formatting.cs:47` | as TextFixed | C9a | |
| HeapBufferReserve :65 | Body | `WL.Formatting.cs:53` | as TextFixed | C9a | |
| HeapBufferIntoString :66 | Body | `WL.Formatting.cs:55` | as TextFixed | C9a | |
| WindowPush :67 | Body | `WL.Formatting.cs:56` | as TextFixed | C9a | |
| WindowAppend :68 | Body | `WL.Formatting.cs:57` | as TextFixed | C9a | |
| WindowLimit :69 | Body | `WL.Formatting.cs:58` | as TextFixed | C9a | |
| WindowCommit :70 | Body | `WL.Formatting.cs:59` | as TextFixed | C9a | |
| WriterWrite :71 | Body | `WL.Formatting.cs:25`; `BL.Calls.cs:222-231`; `BL.Formatting.cs:77`; `BL.Interpolation.cs:16`; `CompilerFunctionAdapters.cs:220` | built-in row; user-witness wrapper; literal fast path | C9a, C9b, C9c | |
| WriterStatus :72 | Body | `WL.Formatting.cs:24` | as TextFixed | C9a | |
| WriteLineUtf8 :73 | Body | `WL.Formatting.cs:60` | legacy row (P1d moves the table) | UNOWNED | see the list above |
| BuiltinFormat :74 | Shim | `WL.Formatting.cs:26`; `BL.Calls.cs:169, 380` | BuiltinFormat shim | C9b | |
| BuiltinEquals :75 | Shim | `BL.BuiltinComparisons.cs:9-18` (dispatch `BL.Calls.cs:348`); `GenericStoragePlan.Comparisons.cs:17-26` | composite comparison shim over F6 | C8f | edge K6 |
| BuiltinCompare :76 | Shim | as BuiltinEquals | as BuiltinEquals | C8f | edge K6 |
| BuiltinArithmetic :77 | Shim | `BL.BuiltinArithmetic.cs:15` (dispatch `BL.Calls.cs:353`); `NumericArithmetic.cs`; `CompilerFunctionAdapters.cs:89` | arithmetic shim | C8f | |
| StorageBorrowShared :78 | Body | `BL.Arrays.cs:390-460` (dispatch `BL.Calls.cs:164`) | legacy row over P1b/P2b kernels | C8d | one of "8 storage-boundary kinds" |
| StorageBorrowExclusive :79 | Body | `BL.Arrays.cs:390-460` | as StorageBorrowShared | C8d | |
| StorageOwn :80 | Body | `BL.Arrays.cs:329, 429` | as StorageBorrowShared | C8d | |
| StorageBorrowDictionary :81 | Body | `BL.Arrays.cs:375`; `BL.DictionaryStorage.cs:51` | legacy row over P1c/P2a kernels | C8e | one of "7 storage kinds" |
| StorageBorrowDictionaryExclusive :82 | Body | `BL.Arrays.cs:375`; `BL.DictionaryStorage.cs:54` | as StorageBorrowDictionary | C8e | |
| StorageOwnDictionary :83 | Body | `BL.Arrays.cs:385`; `BL.DictionaryStorage.cs:152` | as StorageBorrowDictionary | C8e | |
| StorageKeyAt :84 | Body | `BL.Arrays.cs:385`; `BL.DictionaryStorage.cs:177, 200` | as StorageBorrowDictionary | C8e | |
| StorageValueAt :85 | Body | `BL.Arrays.cs:385`; `BL.DictionaryStorage.cs:149` | as StorageBorrowDictionary | C8e | |
| StorageBorrowFixedShared :86 | Body | `BL.Arrays.cs:380`; `BL.DictionaryStorage.cs:106` | legacy row over P2b kernels | C8d | |
| StorageBorrowFixedExclusive :87 | Body | as StorageBorrowFixedShared | as StorageBorrowFixedShared | C8d | |
| StorageOwnFixed :88 | Body | `BL.Arrays.cs:335`; `BL.FixedStorage.cs:12` | as StorageBorrowFixedShared | C8d | |
| StorageDictionaryLayout :89 | Body | `BL.Arrays.cs:340`; `BL.DictionaryStorage.cs:13` | as StorageBorrowDictionary | C8e | |
| StorageMissingDictionaryKey :90 | Intrinsic Fail | `BL.Arrays.cs:355-372` (reason `:365`); `WL.Abort.cs:24` | Fail row, reason table (P0b) | C8a | `KIMI_E_MISSING_KEY` |
| StorageArgumentOutOfRange :91 | Intrinsic Fail | `BL.Arrays.cs:368`; `WL.Abort.cs:22` | as above | C8a | `KIMI_E_ARG_RANGE` |
| StoragePlaceDictionaryEntry :92 | Body | `BL.Arrays.cs:345`; `BL.Dictionaries.cs:159` | as StorageBorrowDictionary | C8e | `placeEntry` |
| StorageCountOverflow :93 | Intrinsic Fail | `BL.Arrays.cs:366`; `WL.Abort.cs:16` | as above | C8a | `KIMI_E_INT_OVERFLOW` |
| StorageAllocationSizeExceeded :94 | Intrinsic Fail | `BL.Arrays.cs:367`; `WL.Abort.cs:11` | as above | C8a | `KIMI_E_ALLOC_SIZE` |
| StorageTryAllocateBytes :95 | Intrinsic TryAllocateBytes | `BL.Arrays.cs:350`; `BL.Dictionaries.cs:107` | raw memory row | C8a | |
| StorageTransferBytes :96 | Intrinsic MoveBytes | `BL.Arrays.cs:350`; `BL.Dictionaries.cs:107-110` | `llvm.memmove` (PD23) | C8a | legacy `memcpy` |
| StorageIndexBounds :97 | Intrinsic Fail | `BL.Arrays.cs:364`; `WL.Abort.cs:21` | as above | C8a | `KIMI_E_INDEX_BOUNDS` |
| StorageBorrowUniqSlice :98 | Body | `BL.Arrays.cs:406, 412` | as StorageBorrowShared | C8d | |
| StorageSetArrayLength :99 | Body | `BL.Arrays.cs:330, 464` | as StorageBorrowShared | C8d | |
| RawAllocate :100 | Intrinsic RawAllocate | `BL.Raw.cs:28, 90` (dispatch `BL.Calls.cs:159`); `WL.cs:69`; `CompilerFunctionAdapters.cs:79, 130` | raw memory row over the runtime ABI table | C8a | |
| RawRelease :101 | Intrinsic RawRelease | `BL.Raw.cs:15` | as RawAllocate | C8a | |
| RawInitialize :102 | Intrinsic RawInitialize | `BL.Raw.cs:61, 103` | as RawAllocate | C8a | |
| RawSlice :103 | Intrinsic RawSlice | `BL.Raw.cs:40, 66` | as RawAllocate | C8a | |

## 2. `IntrinsicId` (33)

Class: Table = produced through `IntrinsicDisposition.Of` (`Bd/KimiLibraryKinds.cs:200-217`, consumed at `M/Build/MirBuilder.Calls.cs:119, 178`);
Builder = produced directly by the MIR builder; None = no producer. Id line numbers are `Bd/KimiLibraryKinds.cs`; descriptors `M/MirDescriptors.cs:141-174`.

| Id | Class | Producer | Old sites | New owner | Unit | Note |
| --- | --- | --- | --- | --- | --- | --- |
| RawAllocate :111 | Table | `:209` | `BL.Raw.cs:28, 90`; `WL.cs:69` | raw memory row | C8a | |
| RawRelease :112 | Table | `:210` | `BL.Raw.cs:15` | raw memory row | C8a | |
| RawInitialize :113 | Table | `:211` | `BL.Raw.cs:61, 103` | raw memory row | C8a | |
| RawSlice :114 | Table, Builder | `:212`; `M/Build/MirBuilder.Calls.cs:143` (Unit backing) | `BL.Raw.cs:40, 66` | raw memory row | C8a | |
| TryAllocateBytes :115 | Table | `:207` | `BL.Dictionaries.cs:107` | raw memory row | C8a | |
| MoveBytes :116 | Table | `:208` | `BL.Dictionaries.cs:110` | `llvm.memmove` | C8a | PD23 |
| Stride :117 | None | — | — | none | — | R8 (PD21) |
| DestroyValues :118 | None | — | — | none | — | R8 (PD21) |
| ReinterpretLoan :119 | None | — | — | none | — | R8 (PD21) |
| Abort :120 | Table | `:200` | `BL.Calls.cs:193, 430`; `WL.cs:81` | Abort row; location text P5 | C8a | C3b, F9 |
| Fail :121 | Table, Builder | `:213-217`; `M/Build/MirBuilder.Calls.cs:142, 186` | `BL.Arrays.cs:355-372`; `WL.Abort.cs:32-51` | 7 MIR codes → reasons (P0b) | C8a | measured 7 codes, as C8a says |
| WriteStdout :122 | None | — | `WL.cs:59` (runtime `__kimi_write_line`) | none | — | R8 (PD21) |
| FormatFloat :123 | None | — | `Utf8FloatRuntime.ll.in:25` | none; legacy runtime text through C9a/C9b rows | — | R8 (PD21) |
| ErasedWriter :124 | Table | `:206` | `BL.Formatting.cs:26`; `BL.Calls.cs:358-362` | ErasedWriter dispatch | C9b | |
| ErasedReserve :125 | None | — | `BL.Formatting.cs:26` (inside the writer dispatch) | none; C9b reaches user `reserve` through `TyCtxt.Witness` | — | R8 (PD21) |
| FormatInteger128 :126 | None | — | `Utf8FormatRuntime.ll.in:294` | none | — | R8 (PD21) |
| MakeObj :127 | Table | `:201` | `ObjectGenerationPlan.cs:157-241` | `B/Codegen/ObjectDescriptors.cs` + `ObjectRuntime` | C6a | edge K8 |
| MakeRc :128 | Table | `:202` | as MakeObj | as MakeObj | C6a | edge K8 |
| MakeArc :129 | Table | `:203` | as MakeObj | as MakeObj | C6a | edge K8 |
| Clone :130 | Table | `:204` | `BL.Calls.cs:194-201`; `WL.cs:61-62` | `ObjectRuntime` | C6a | |
| TypeTest :131 | Builder | `M/Build/MirBuilder.Calls.cs:207` | `BL.Objects.cs:9`; `ObjectGenerationPlan.cs:101-105` | descriptor token test (PD17) | C6a | edge K8 |
| TestObserve :132 | Builder | `M/Build/MirBuilder.Control.cs:248` | `BL.Testing.cs:25-31` | test row; Location → `TestCatalog.SiteOf` | C10a | |
| TestMessage :133 | Builder | `M/Build/MirBuilder.Control.cs:213` | `BL.Testing.cs:48-54` | test row | C10a | |
| TestRequireAbort :134 | Builder | `M/Build/MirBuilder.Control.cs:224` | `BL.Testing.cs:56-57` | test row | C10a | |
| TestRecordValue :135 | None (F8 adds it) | — | `BL.Testing.cs:42`; `LlvmModuleWriter.Testing.cs:7` | test row | F8, C10a | PD22 |
| TestPhaseEnter :136 | None (codegen writes it) | — | `BL.Graph.cs:302`; `LlvmModuleWriter.cs:499` | test phases | C10b | PD22 |
| TestPhaseLeave :137 | None (codegen writes it) | — | `BL.Graph.cs:313`; `LlvmModuleWriter.cs:505` | test phases | C10b | PD22 |
| TestTempDirectory :138 | Table | `:205` | `WL.cs:82, 99` | test row | C10a | |
| StaticEnsure :139 | Builder | `M/Build/MirBuilder.Places.cs:154` (inline fold: `M/Build/MirBuilder.Defaults.cs:215`) | `LlvmEmitter.cs:147-180`; `BL.cs:252-263`; `LlvmModuleWriter.Statics.cs:7` | `B/Codegen/Statics.cs` slot state machine | C7 | edge K7 (F10) |
| Capacity :141 | Builder | `M/Build/MirBuilder.Operators.cs:86` | `BL.Sequences.cs:301` | capacity row | C8d | `[Transitional("R8")]` |
| FormatBuffer :143 | Builder | `M/Build/MirBuilder.Formatting.cs:62` | `BL.Interpolation.cs:20`; `BL.Calls.cs:466-467` | interpolation rows | C9c | `[Transitional("R8")]` |
| FormatHint :145 | Builder | `M/Build/MirBuilder.Formatting.cs:76, 121` | `BL.Interpolation.cs:37` | interpolation rows | C9c | `[Transitional("R8")]` |
| FormatFinish :147 | Builder | `M/Build/MirBuilder.Formatting.cs:101` | `BL.Interpolation.cs:20` | interpolation rows | C9c | `[Transitional("R8")]` |

## 3. MIR callee, constant, cast and aggregate forms (26)

Producers are in `M/Build/` (`MirBuilder.` omitted). Collector edges per PD6 are named beside the codegen unit.

| Form | Producer | Old sites | New owner | Unit | Note |
| --- | --- | --- | --- | --- | --- |
| Callee Fn | `Calls.cs:124, 178`; `Defaults.cs:135`; `Expansions.cs:442` | `BL.Calls.cs:135-235` | `FunctionCodegen.Calls`, callee `__kimi_f<N>` from the collector | C3a; K1, K4 | Getter, Setter, DefaultTemplate, destructor bodies too |
| Callee Requirement | `Calls.cs:121` | `BL.Calls.cs:124-133, 155-157`; `GenericStoragePlan.cs:519` | call of the instance K6 resolves | C3b; K6 (F5b) | |
| Callee Virtual | `Calls.cs:122` | `BL.Calls.cs:205-220`; `VirtualGenerationPlan.cs:126` | slot load from `VTable(T)` | C6c; K8 (F4) | |
| Callee Value | `Calls.cs:123` | `BL.Closures.cs:261` | `FunctionCodegen.Closures` value call | C5a; K5 | the inline fold copies it (`M/Build/MirBuilder.Defaults.cs:234`) |
| Callee Intrinsic | `Calls.cs:119, 178, 186` and group 2 | per Id (group 2) | intrinsic rows | per Id (group 2) | 23 Ids produced |
| Const Integer | `Calls.cs:28`; `Values.cs:375`; `Expansions.cs:78` (and others) | `BL.Graph.cs:62` | scalar constant | C0b1 | |
| Const Float | `Operators.cs:142`; `Values.cs:375` | `BL.Graph.cs:62` | scalar constant (IEEE bits) | C0b1, C1a | |
| Const Bool | `Values.cs:370`; `Match.cs:41, 125`; `Formatting.cs:123` | `BL.Graph.cs:62` | scalar constant; `i8` in memory (PD8) | C0b1 | |
| Const Unit | `Values.cs:373, 425` | — | no value (zero-sized) | C0b1 | |
| Const String | `Calls.cs:190`; `Values.cs:372`; `Match.cs:124` | `BL.Strings.cs:369, 395`; `LlvmConstantPool.cs` | constant through `LlvmConstantPool` (P0) | C0b2, C8c | also `Fail`'s code operand (C8a) |
| Const FnItem | `Values.cs:22` | `BL.Closures.cs:64-77` | function item constant | C5a; K5 | |
| Const Location | `Control.cs:199` | `BL.Testing.cs:31`; `BL.cs:542` | `TestCatalog.SiteOf` (P6) | C10a | only producer is `$require`'s observe |
| Const TypeToken | `Calls.cs:123, 205`; `Operators.cs:196`; `Formatting.cs:119` | `BL.Objects.cs:9`; `ObjectGenerationPlan.cs:310` | TypeTest: descriptor token (PD17); Value callee signature; Adapt source; format Types | C6a, C5a, C2b, C9c | |
| Cast Integer | `Operators.cs:169`; `Places.cs:243` | `BL.Conversions.cs:173`; `BL.Values.cs:301` | `FunctionCodegen.Casts` over `LlvmScalarText` (P4b) | C1b | |
| Cast Floating | `Operators.cs:169` | as Integer | as Integer | C1b | |
| Cast Numeric | `Operators.cs:170` | as Integer | as Integer | C1b | |
| Cast Wrap | `Operators.cs:165`; `Places.cs:243` | as Integer | as Integer | C1b | |
| Cast Bits | `Operators.cs:166` | as Integer | as Integer | C1b | |
| Cast Pointer | `Operators.cs:168` via `M/MirTypes.cs:58` | `BL.Values.cs:302` | as Integer | C1b | |
| Cast IntToPointer | `Operators.cs:168` via `M/MirTypes.cs:58` | `BL.Values.cs:302` | as Integer | C1b | |
| Cast Upcast | `Operators.cs:167` | `BL.Aggregates.cs:484` | object handle view change | C6a | |
| Cast Erasure | `Values.cs:188` | `BL.Aggregates.cs:340`; `BL.Closures.cs:107` | erasure adapter shim through `FunctionCodegen` primitives (PD3) | C5b; K5 | |
| Aggregate Tuple | `Values.cs:300` | `BL.Aggregates.cs:338` | `FunctionCodegen.Rvalues` | C2b | |
| Aggregate Adt | `Operators.cs:102`; `Values.cs:296` | `BL.Aggregates.cs:338` | struct fields; enum tag and payload | C2b | |
| Aggregate FixedArray | `Values.cs:76, 300`; `Expansions.cs:404, 430` | `BL.Aggregates.cs:338, 443`; `BL.Dictionaries.cs:61` | fixed array; empty Array and Dictionary literal | C2b; C8d, C8e | |
| Aggregate Closure | `Values.cs:324` | `BL.Closures.cs:196` | environment, inline when 8 bytes or less (PD19) | C5a; K5 | |

### 3b. Other MIR vocabularies (beyond the plan's list; PD11's coverage rows cover them too)

| Form | New owner | Unit | Note |
| --- | --- | --- | --- |
| Statement Assign | per rvalue | C0b1… | |
| Statement StorageLive, StorageDead | no output (one alloca per local) | UNOWNED | C0b1 by implication; must be a listed row |
| Statement FakeRead, EndBorrow | no output (`M/MirVocabulary.cs:20-21`) | UNOWNED | as above |
| Rvalue Use | scalar or memory copy | C0b1, C2b | |
| Rvalue Ref (Shared, Uniq, TwoPhaseUniq, Raw) | address | C2b | |
| Rvalue BinaryOp (16), UnaryOp (2) | `FunctionCodegen.Arithmetic` over `LlvmScalarText` (P4a) | C1a | strings C8c; tuple operators expanded by the builder (F7) |
| Rvalue Cast, Aggregate | table 3 | C1b, C2b, C5a, C5b, C6a | |
| Rvalue Discriminant, Len | `FunctionCodegen.Rvalues` | C2b | |
| Rvalue Adapt | `MirCases.Adapt` per selected view | C2b | |
| Rvalue Opaque | never generated: only in a program with a Binding error or a MIR `Unsupported_Kd`, which the gate refuses | C0a (PD12) | |
| Terminator Goto, SwitchInt, Return, Unreachable | `br`/`switch`/`ret`/`unreachable` | C0b1 | |
| Terminator Call | `FunctionCodegen.Calls` | C3a | callee rows above |
| Terminator Drop | `B/Codegen/DropGlue.cs`: Keep/Elide, Flag, Open; PD12 refusal | C4a, C4b, C4c | |
| Projections (7) | `FunctionCodegen.Places` | C2a | object Deref C6a |
| Operand Copy, Move, PairAcquire, Const | load/copy per view | C0b1, C2b | |
| Local Return, Arg, Var, Temp; DropFlag | one alloca per local; flag allocas | C0b1; C4b | |
| Local roles (5) | no output | — | |
| Scopes (5) | debug-information record only (PD24); Deferred: test phases | S4; C10b | |
| Body Fn | frame | C0b1, C3a | roots K1 |
| Body Closure | closure body | C5a | edge K5 |
| Body Getter, Setter | accessor bodies | C3a | edges K1 |
| Body StaticInit | initializer | C7 | edge K7 |
| Body DefaultTemplate | recursive default | C3a | edge K1 |
| Body Test | called by `Shim(TestEntry)` | C10a | roots K11 |

## 4. Old instance-collector responsibilities (18 rows)

| Responsibility | Class | Old sites | New owner | Unit | Note |
| --- | --- | --- | --- | --- | --- |
| Every nongeneric body is generated; signatures in body order | Roots | `LlvmEmitter.cs:127-140, 221-237, 510-512`; `GenericStoragePlan.cs:67-68` | `B/Instances/InstanceCollector` roots (`TyCtxt.Instantiates`); Generated mark pass | K1, K11 (F3) | old path has no reachability |
| Output roots: startup, Library, tests | Roots | `LlvmEmitter.cs:122-125, 253-274` | Generated roots; `Shim(Entry)`, `Shim(TestEntry)` | K11; C0b2 (F2a), C10a (P6) | |
| Generic call instances: templates, per-substitution entries, forwarded calls, worklist | Edges | `GenericStoragePlan.cs:104-164, 324-341, 353-551`; `LlvmEmitter.cs:417-484` | collector edges: closed (K1), generic through `TyCtxt.Compose` (K4) | K1, K4 | |
| Instance identity modulo Origins | Identity | `GenericStoragePlan.cs:432-443`; `DefaultGenerationPlan.cs:76-77` | `Instantiation(MirDefKey, TyCtxt.Erase(Subst))`, erase late (PD5) | K3, K4 | linear scan deleted |
| Generation limits: growing key 16, depth 128, set 1,024 | Limits | `GenericStoragePlan.cs:15-19, 362-379, 396-400, 445-452`; `LlvmEmitter.cs:278-295` | one `Admit`, located `GenerationResourceLimit_Kd`; `Apply` null (PD30) | K4; K13b, K13c | |
| Instance layout obligations: inline depth/size, C-layout instance shape | Limits | `AggregateLayout.cs:47, 171-175, 300, 336` | `Layout.Of` typed limits (P7c); C-layout zero-sized rule as a `TyCtxt` view | K10a, K10b | |
| Explicit specialization selection | Edges | `GenericStoragePlan.cs:455-460, 474-479` | edge to `TyCtxt.Implementation(f, s, 0)` | K4 | |
| Function items and closures under the enclosing substitution | Edges | `GenericStoragePlan.cs:256-320` | `Const(FnItem)`, `Aggregate(Closure)`, Value edges via `SubstOfItem`/`SubstOfClosure`; contexts as Subst | K5 (K2) | |
| Compiler-function item adapters | Items | `CompilerFunctionAdapters.cs:50-208`; `BL.Closures.cs:64-77` | ItemAdapter items and shim (PD3) | K5, C5b; C8d (array items) | |
| Formatting callbacks and user-witness wrappers | Shims | `GenericStoragePlan.cs:181-245, 553-570` | requirement edges through `TyCtxt.Witness`; user-witness wrappers, BuiltinFormat shim | K6 (F5b), C9b | |
| Composite comparison helpers | Shims | `GenericStoragePlan.Comparisons.cs:17-196` | built-in shim items; shims over `TyCtxt.Comparison` | K6, C8f (F6) | |
| Destructor instances, requested source calls, Sealed payloads | Edges | `GenericStoragePlan.Destructors.cs:14-87`; `LlvmEmitter.cs:35-37, 101-114, 239-251` | Drop → `DropGlue(Ty)`, destructors by `AdtDef.DestructorSymbol` + `Synthesized`; Sealed on the representative | K7, C6b; C8e (Dictionary storage, F2c) | |
| Object descriptors and runtime type tokens | Records | `ObjectGenerationPlan.cs:71-241, 246-345` | `ObjectDescriptor(Ty)` items; token = ordinal of the erased payload Type (PD17) | K8, C6a | `WriteKey` deleted |
| Virtual tables, slot entries, erasure addresses | Records | `VirtualGenerationPlan.cs:32-88, 147-293, 301-355` | `VTableDef`/`TyCtxt.Applies`; `VTable(Ty)` items | F4, K8, C6c | |
| Inapplicable virtual bodies are not generated | Roots | `GenericStoragePlan.cs:127`; `ObjectGenerationPlan.cs:78`; `LlvmEmitter.cs:512` | checked-only roots (R4-M28) by `TyCtxt.Applies` | K8 (F4) | member deleted G9 |
| Default evaluator instances | Edges | `DefaultGenerationPlan.cs:33-133`; `LlvmEmitter.Defaults.cs:7-48`; `GenericStoragePlan.cs:166-179`; `ObjectGenerationPlan.cs:36-52` | MIR inlines a default (`M/Build/MirBuilder.Defaults.cs:139`); a recursive one is a DefaultTemplate edge (`:135`) | K1 | |
| Static initializer slots | Roots | `LlvmEmitter.cs:147-180`; `BL.cs:252-263` | `staticEnsure` → StaticInit edge (F10); slot state machine | K7, C7 | |
| Per-instance ownership plans (`AnalyzeInstance`, `FailedInstance`, `InstanceStorageLimit`) | Plans | `A/OwnershipAnalysis.Instances.cs:21-94`; `LlvmEmitter.cs:71-79, 429`; `LlvmEmitter.Defaults.cs:15` | deleted; its two holes fixed in the old definition run first (OQ6) | Q1–Q4, G7 | |

## 5. Emission→Binding call sites (42 members, 82 sites)

Class: P = the new owner reads an existing published fact (HIR, CallPlan, `TyCtxt`, `AdtDef`, a Binding result) or the MIR form carries it;
D = a re-check or display text, deleted with its site; N = a new Binding view a plan unit adds; T = a declaration-table read that becomes a
symbol column. Every site's file is deleted or re-pointed at G7; the Unit column names the unit that supplies the new owner.

| Member | Class | Sites (count) | New owner | Unit | Note |
| --- | --- | --- | --- | --- | --- |
| DiagnosticTypeName | D | `BL.Calls.cs:185` ×2, `:190` ×2; `LlvmEmitter.cs:479` (5) | deleted with the failure strings | G7 | member stays |
| FitsVerifiedTypeAt | D | `BL.Calls.cs:288`; `BL.Closures.cs:336`; `BL.StructBorrows.cs:469`; `BL.Updates.cs:118, 119, 120` ×2; `BL.Values.cs:251, 326` (9) | deleted (`MirValidator` owns MIR type agreement) | G7 | then old-Analysis-only, not named in H11 |
| FitsType | D | `BL.Values.cs:250` (1) | deleted | G7 | member stays |
| CallableSignatureFits | D | `BL.Closures.cs:147` (1) | deleted | G7 | member stays |
| ClosureSignature | D | `BL.Closures.cs:146` (1) | deleted (input of the re-check above) | G7 | member stays |
| FunctionItemSignature (erasure) | D | `BL.Closures.cs:131` (1) | deleted (input of `ItemContractFits`) | G7 | |
| ItemContractFits | D | `BL.Closures.cs:132` (1) | deleted | G7 | member stays |
| SupportsIdentityAcquisition, IsCopyOperation | D | `BL.Values.cs:38` ×2 (2) | deleted (acquisition published in HIR) | G7 | members stay |
| PlaceOriginSource, PlaceOriginBinder, PlaceOriginSlot | D | `BL.References.cs:129, 134` ×2; `BL.Sequences.cs:129, 132` ×2 (6) | deleted (MIR Places; Origins erased, SPEC 3.3.1) | G7 | members stay |
| CandidateOriginBinder | D | `BL.Match.cs:646` (1) | deleted | G7 | member stays |
| SameModuloOpenOrigins (lowering) | D | `BL.Defaults.cs:19, 36` (2) | deleted (MIR inlines defaults) | G7 | |
| SameModuloOpenOrigins (collectors) | N | `DefaultGenerationPlan.cs:76` ×2, `:77`; `GenericStoragePlan.cs:436, 437` ×2 (6) | `TyCtxt.Erase` instance key (PD5) | K3, K4 | member deleted G9 |
| IsVerifiedSpecialization | D | `LlvmEmitter.cs:588` (1) | `CheckInputs` gate → PD12 gate and PD11 rows | C0a, G7 | member deleted G9 |
| Result | D | `LlvmEmitter.cs:523` (1) | `Compilation.Generates` gate (PD12) | C0a, G7 | member stays |
| PrepareTypeStorage | D | `BL.Structs.cs:31` (1) | deleted: codegen Types come through `TyCtxt.Apply`, which prepares storage (`Bd/Binding.Structs.cs:35`) | G7 | member deleted G9 |
| InstantiateStorageType (lowering) | P | `BL.cs:116`; `BL.Calls.cs:314` (2) | `TyCtxt.Apply` with the instance Subst (`Bd/TyCtxt.cs:235`) | C0b1, C3a | |
| InstantiateStorageType (collectors) | P | `GenericStoragePlan.cs:274, 311, 421`; `DefaultGenerationPlan.cs:52, 62` (5) | `TyCtxt.Apply`/`Compose` on the representative Subst | K4, K5 | |
| FunctionItemContext | P | `BL.Closures.cs:73`; `CompilerFunctionAdapters.cs:65`; `GenericStoragePlan.cs:275` (3) | `TyCtxt.SubstOfItem` (`Bd/TyCtxt.cs:224`) | K5, C5a | |
| FunctionItemSignature (adapter) | P | `CompilerFunctionAdapters.cs:58` (1) | `TyCtxt.Signature` under the item Subst; `Abi.Of` (P8) | C5b | |
| InstantiateDefaultCall | P | `DefaultGenerationPlan.cs:37` (1) | inline default; DefaultTemplate edge under `TyCtxt.Compose` | K1 | then old-Analysis-only, not named in H11 |
| SelectSpecialization | P | `GenericStoragePlan.cs:455` (1) | `TyCtxt.Implementation(f, s, 0)` | K4 | CallPlan overload deleted H11 |
| InstantiateForwardedCall | N | `GenericStoragePlan.cs:519` (1) | Fn: `TyCtxt.Compose` (exists); requirement: `TyCtxt.Witness` | K4, K6 (F5b) | member deleted H11 (PD16) |
| HasFormattingCallback | P | `GenericStoragePlan.cs:254` (1) | MIR Requirement callee | K6 | |
| TryResolveFormattingCallback | N | `GenericStoragePlan.cs:189` (1) | `TyCtxt.Witness` | K6, C9b (F5b) | member deleted H11 |
| ComparisonPlan | N | `GenericStoragePlan.Comparisons.cs:26` (1) | `TyCtxt.Comparison` | C8f (F6) | CallPlan overload stays to R7 |
| DestructionCall | P | `GenericStoragePlan.Destructors.cs:30` (1) | `AdtDef.DestructorSymbol` + `TyCtxt.Synthesized` | K7 | member deleted H11 |
| DictionaryStorageCall | N | `LlvmEmitter.cs:35` (1) | Dictionary-storage symbols | C8e (F2c) | readers deleted G9 |
| ProveCopy (element) | P | `BL.Elements.cs:306` (1) | MIR operand Copy/Move | C2b | |
| ProveCopy (object payload) | P | `ObjectGenerationPlan.cs:176` (1) | `TyCtxt.Copy` on the representative | C6a | |
| ProveSealed | P | `LlvmEmitter.cs:37` (1) | `TyCtxt.Sealed` on the representative | K7, C6b | |
| StoredType | P | `ObjectGenerationPlan.cs:244` (1) | `AdtDef.Base` (`Bd/AdtDef.cs:39`) | C6a | |
| IsInapplicableVirtualBody | N | `GenericStoragePlan.cs:127`; `ObjectGenerationPlan.cs:78`; `LlvmEmitter.cs:512` (3) | `TyCtxt.Applies`; checked-only roots (R4-M28) | K8 (F4) | member deleted G9 |
| ImplementationContext | N | `VirtualGenerationPlan.cs:154` (1) | `VTableDef` entries + `TyCtxt.Implementation` | K8 (F4) | member deleted G9 |
| VirtualApplicability | N | `VirtualGenerationPlan.cs:193` (1) | `TyCtxt.Applies` | K8, C6c (F4) | member stays (F4 wraps it) |
| TryGetVirtualOverride | N | `VirtualGenerationPlan.cs:343` (1) | `VTableDef` | C6c (F4) | member stays (Hover) |
| PropertyCall | P | `BL.StructBorrows.cs:31` (1) | accessor CallPlan; MIR Getter/Setter call | C3a | |
| PropertyUpdateStorage, StorageProjection | P | `BL.StructBorrows.cs:381` ×2, `:382` (3) | MIR Place projections | C2a | PropertyUpdateStorage deleted H11 |
| IsGetterResult | P | `BL.StructBorrows.cs:327` (1) | MIR Places | C2a | |
| TryGetAdaptation | P | `BL.Values.cs:289` (1) | HIR Adjust → MIR Ref/Adapt | C2b | |
| TryPairLayer | P | `BL.CompositeMatch.cs:35` (1) | MIR PairLayer projection | C2a | |
| Startup | T | `EmissionArtifacts.cs:75`; `LlvmEmitter.cs:259, 264, 511, 517` (5) | startup symbol column | C0b2, K11 (F2a) | |
| LibraryImports | T | `EmissionArtifacts.cs:141`; `LlvmEmitter.cs:142` (2) | library-import symbol column | P8 (F2b) | |
| (nested type `VirtualSlot`) | — | `VirtualGenerationPlan.cs:397` (1) | deleted with the file | G7 | not in the 82 |

### 5b. Facts hidden in syntax reads (those the plan's units name, plus those met while auditing; not a census)

| Fact | Old sites | New owner | Unit |
| --- | --- | --- | --- |
| A function needs instantiation (`FunctionKoto.RequiresInstantiation`) | `GenericStoragePlan.cs:67-68` | `TyCtxt.Instantiates` | F3 (K1) |
| C layout (attribute chain `LayoutMode == "C"`) | `AggregateLayout.cs:258-263` | `AdtDef.IsCLayout` | F1 (P7b) |
| Struct base (`StructKoto.Bases`) | `ObjectGenerationPlan.cs:244`; `AggregateLayout.cs:300` | `AdtDef.Base` (exists) | C6a, P7b |
| Runtime type identity (package, containers, closure path and span) | `ObjectGenerationPlan.cs:246-308` | ordinal of the Origin-erased Type (PD17) | C6a |
| Virtual slot order (declarations sorted by path and span; `IsVirtual`/`IsOverride`) | `VirtualGenerationPlan.cs:41-54` | `VTableDef` in member order (SPEC 6.2.1) | F4 |
| Startup function as `FunctionKoto` | `LlvmEmitter.cs:264` | startup symbol and module ordinal | F2a (C0b2) |
| Imports keyed by `FunctionKoto` | `LlvmEmitter.cs:318` | import symbol column | F2b (P8) |
| Dictionary helper functions (`c.Library.Dictionary*`) | `LlvmEmitter.cs:191-197` | Dictionary-storage symbols | F2c (C8e) |
| Runtime source locations keyed by Koto | `BL.cs:542`; `LlvmEmitter.cs:168` | Core location text by `SourceRef` | P5 |
| Test cases keyed by `FunctionKoto` | `LlvmEmitter.cs:255` | `TestCase` symbol column | P6 (C10a) |
| Tuple-operator mode (`CallPlan.TupleOperator`) | `BL.ContractComparisons.cs:35` | builder expansion (PD15) | F7 (C8f) |
| Interpolation estimate from `InterpolatedStringKoto.Formatting` | `BL.Interpolation.cs:123`; `BL.Calls.cs:222-223` | per-instance estimate (port of `FormattingBound`) | C9c |
| Static initializers (`StaticInitializer`, `StaticScalar.IsDynamic`) | `LlvmEmitter.cs:150, 168`; `BL.cs:256` | `MirStatic` and StaticInit bodies | C7 (`C/StaticScalar.cs` deleted H11) |
| Destructor registration by `IsDestructor` | `LlvmEmitter.cs:107` | `AdtDef.DestructorSymbol` | K7 |
| Runtime type test target (`IsKoto.BoundRuntimeTest`) | `ObjectGenerationPlan.cs:101` | MIR TypeTest (`M/Build/MirBuilder.Calls.cs:207`) | K8, C6a |
| Support gates on syntax (containers, members, attributes, captures) | `LlvmEmitter.cs:514-605` | PD12 gate and PD11 coverage rows | C0a, C0b1 |
