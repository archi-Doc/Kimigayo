# R8-L0 census (L0a): evidence only

Base: dev `e1d888368` (2026-11-03), plan `draft/Proposals/2026-11-03 R7 R8 R9 Implementation Plan.md` §5.2 row L0, §5.0, §7.2.
No code, build, test or timing. Paths are relative to the repository root; `LRH` = `src/Kimi/Compiler/Backend/LegacyRuntimeHelpers`,
`FC` = `src/Kimi/Compiler/Backend/Codegen/FunctionCodegen`, `Cat` = `src/Kimi/Compiler/Binding/KimiLibraryCatalog.Definitions.cs`,
`Lib` = `src/Kimi/Library`, `B/F/Fl/R/W` = `src/Kimi/Compiler/Backend/{Utf8BufferRuntime,Utf8FormatRuntime,Utf8FloatRuntime,Utf8FloatRyu,WindowsRuntime}.ll.in`.

## Summary

| Item | Plan says | Census | Uncovered or mismatched |
| --- | --- | --- | --- |
| 1 Body-disposition kinds | 53 | **53** (54 in the arm with `None`) | 7 Array/fixed-array storage-boundary kinds are named by no unit part |
| 2 `.ll.in` defines | 30 (B 5, F 15, Fl 2, R 2, W 6) | **30**, same split; plus 31 templated `{{name}}` headers | 9 dispositions need a plan correction (2.3) |
| 3 Layout handle rows | Slice, Array, Dictionary rows and `LengthWord` | **3 rows + `LengthWord`**; 1 row reader, 3 `LengthWord` readers, 6 hard-coded offset sites | String is not a handle row (a `Primitives` row) |
| 4 Allocation-count inventory | 6 classes | **5 test classes** (31 methods, 76 cases: 49 native-audit rows, 22 managed-byte rows, 5 others); `NativeAllocationAudit` is the audit helper (254 call sites in 93 files), of which the R8-family rows are tabled | — |
| 5 Texts to revise | SP1–SP4's 13 citations | **6 stale numbers**, 2 numbers with another subject | §22.5.5, IMPL §21.2.2, §21.5.3, utf8-formatting, testing-profile gone; §22.4 → §22.7, §6.5 → §6.7 |
| 6 Strings kernel | needed | sketch in §6 | — |

## 1. The Body-disposition kinds

Counted from `src/Kimi/Compiler/Binding/KimiLibraryKinds.cs:217-232`: `IntrinsicDisposition.Of`'s first arm returns `default`, and
`DispositionKind.Body` is member 0 (:206). A script (`re.findall('CompilerFunctionKind\.(\w+)')` over lines 219-232) finds 54 distinct kinds,
`None` among them: **53 Body kinds**. `CompilerFunctionKind` (:60-138) has 76 members: 53 Body + `None` + 4 Shim (:233) + 13 Intrinsic
(:234-246) + 5 Fail (:247-251).

The legacy row is `LRH.cs:227-263` (`Row(kind)`, its writer `LegacyWriter`); the formatting rows are `LRH.Formatting.cs:229-282`; the Array
helper rows `LRH.Arrays.cs:247-335`; the Dictionary Place row `LRH.Dictionaries.cs:89`. Declarations are the catalog's `Function:` rows
(`grep -n 'Function: CompilerFunctionKind\.' Cat`). Library callers are `grep -nE '(Storage\.)?<name>\(' Lib/*.kimi` without declaration
lines, each mapped to its enclosing function; public members are also called by user code, which is not counted. Kernel coverage is
`grep -oE` of the member calls in `src/Benchmark/Kimi/Corpus/*.kimi`. Every kernel calls `Console.writeLine` once, at the end (not timed).

Shapes: **R** a call of a fixed-symbol runtime routine (one per module); **H** a call of a per-instance helper the module writes for the
element or entry layout (`__kimi_array_<op>_<suffix>`, `__kimi_dictionary_place<suffix>`, or the capacity routine); **I** an inline sequence in
the caller (loads, stores, address arithmetic).

| # | Kind | Declaration (Cat; Lib) | Legacy row (writer → routine) | Library callers (Lib) | Shape | Kernel | Unit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | WriteLine | :29; Console.kimi:2 | LRH:230 Routine → `__kimi_write_line` (W:296 template) | — | R | all 9 (once) | W2 |
| 2 | WriteLineUtf8 | :132; Console.kimi:3 | LRH.Formatting:282 → `__kimi_write_line_utf8` (B:25) | — | R | — | W1c |
| 3 | Replace | :64; Intrinsics.kimi:2 | LRH:231 Replace (FC.Updates.cs:26) | — | I | — | U1 |
| 4 | Exchange | :65; Intrinsics.kimi:3 | LRH:232 Exchange (FC.Updates) | — | I | — | U1 |
| 5 | Swap | :66; Intrinsics.kimi:4 | LRH:233 Swap (FC.Updates, FC.Arguments.cs:208) | — | I | — | U1 |
| 6 | ArrayReserve | :78; ArrayOperations.kimi:13 | LRH:237 Instance → `__kimi_array_reserve` (RuntimeAbi.cs:100) | Array.kimi:152 appendAll, :164 initCapacity, :191 appendCopies | H | — | A1a |
| 7 | ArrayAppend | :79; ArrayOperations:14 | LRH:239 Instance → `__kimi_array_append_` (LRH.Arrays:250) | Array.kimi:156 appendAll | H | Array, Tuples (setup in ErasedCalls, VirtualDispatch, Iterators, SharedHandles) | A1a |
| 8 | ArrayInsert | :80; ArrayOperations:15 | LRH:240 → `__kimi_array_insert_` | Array.kimi:48-49 insert | H | — | A1b |
| 9 | ArrayPop | :81; ArrayOperations:16 | LRH:241 → `__kimi_array_pop_` | Array.kimi:133 swapRemove, :145 truncate, :155 appendAll | H | Array | A1b |
| 10 | ArrayRemove | :82; ArrayOperations:17 | LRH:242 → `__kimi_array_remove_` | Array.kimi:59-60 remove | H | — | A1b |
| 11 | ArrayClear | :83; ArrayOperations:18 | LRH:243 → `__kimi_array_clear_` | — | H | Array, Tuples | A1c |
| 12 | ArrayShrinkToFit | :84; ArrayOperations:20 | LRH:238 Instance → `__kimi_array_shrink` (RuntimeAbi.cs:105) | — | H | — | A1c |
| 13 | ArraySwap | :85; ArrayOperations:19 | LRH:244 → `__kimi_array_swap_` | Array.kimi:69 swap, :131-132 swapRemove, :211 removeAll, :221 reverse, :239/:245 sort, :265/:270 sort | H | Array, Tuples (via `Array.swap`) | A1b |
| 14 | StorageSetArrayLength | :231; StorageOperations.kimi:22 | LRH:249 Length (FC.Arrays.cs:45, `LengthWord`) | Array.kimi:181 initRepeating, :198 appendCopies | I | — | A1a |
| 15 | StorageBorrowShared | :153; StorageOperations:15 | LRH:245 Instance → `__kimi_array_borrow_` (LRH.Arrays:313) | Array.kimi:29 iterate | H | Array (`for` over the Array) | **none named** |
| 16 | StorageBorrowExclusive | :154; StorageOperations:16 | LRH:245 → `__kimi_array_borrow_` | Array.kimi:21/25 sliceUniq, :33 iterateUniq, :118 tryGetPairUniq, :174 initRepeating, :193 appendCopies | H | — | **none named** |
| 17 | StorageOwn | :155; StorageOperations:17 | LRH:247 → `__kimi_array_own_` (LRH.Arrays:321) | Array.kimi:38 intoIterator | H | SharedHandles (`for … in handles@move`) | **none named** |
| 18 | StorageBorrowUniqSlice | :41; StorageOperations:47 | LRH:245 → `__kimi_array_borrow_` | UniqSlice.kimi:30/34 sliceUniq | H | — | **none named** |
| 19 | StorageBorrowFixedShared | :166; StorageOperations:43 | LRH:234 FixedStorage (FC.Storage.cs:20, FC.Calls.cs:283) | Storage.kimi:235 FixedArray.iterate | I | — | **none named** |
| 20 | StorageBorrowFixedExclusive | :167; StorageOperations:44 | LRH:234 FixedStorage | Storage.kimi:227/231 sliceUniq, :239 iterateUniq, :283 tryGetPairUniq | I | — | **none named** |
| 21 | StorageOwnFixed | :170; StorageOperations:45 | LRH:248 Instance → `__kimi_fixed_own_` (LRH.Arrays:329) | Storage.kimi:244 FixedArray.intoIterator | H | — | **none named** |
| 22 | StorageBorrowDictionary | :158; StorageOperations:26 | LRH:254 DictionaryBorrow (FC.Storage.cs:55) | Dictionary.kimi:66 tryGet, :75 iterate; DictionaryStorage.kimi:188 findEntry | I | Dictionary | K1a |
| 23 | StorageBorrowDictionaryExclusive | :160; StorageOperations:28 | LRH:254 DictionaryBorrow | Dictionary.kimi:22 indexUniq, :79 iterateUniq | I | — | K1a |
| 24 | StorageOwnDictionary | :162; StorageOperations:30 | LRH:255 DictionaryOwn (FC.Storage:56) | Dictionary.kimi:84 intoIterator | I | — | K1a |
| 25 | StorageKeyAt | :163; StorageOperations:35 | LRH:256 KeyAt (FC.Storage:57) | DictionaryStorage.kimi:55 removeEntry | I | Dictionary (`remove`) | K1a |
| 26 | StorageValueAt | :164; StorageOperations:39 | LRH:257 ValueAt (FC.Storage:58) | Dictionary.kimi:71 tryGet; DictionaryStorage.kimi:55 removeEntry, :66-67 replaceValue | I | Dictionary (`tryGet`, `remove`) | K1a |
| 27 | StorageDictionaryLayout | :175; StorageOperations:59 | LRH:253 DictionaryLayout (FC.Storage:54) | Dictionary.kimi:42 tryInsert, :53 insertOrReplace; DictionaryStorage.kimi:31 clearEntries, :51 removeEntry, :64 replaceValue, :74 reserveEntries, :80 shrinkEntries | I | Dictionary | K1a |
| 28 | StoragePlaceDictionaryEntry | :179; StorageOperations:83 | LRH:252 Place → `__kimi_dictionary_place` (LRH.Dictionaries:90; calls `append`) | Dictionary.kimi:42 tryInsert, :53 insertOrReplace | H | Dictionary | K1b |
| 29 | TextFixed | :106; Text.kimi:2 | LRH.Formatting:229 → `__kimi_text_fixed` (B:35) | — | R | Interpolation | W1c |
| 30 | TextHeap | :107; Text.kimi:3 | :230 → `__kimi_text_heap` (B:47) | — | R | — | W1c |
| 31 | TextUtf8 | :109; Text.kimi:6 | :252 → `__kimi_text_utf8` (B:83) | — | R | Interpolation | W1c |
| 32 | TextValidateUtf8 | :110; Text.kimi:7 | :253 → `__kimi_text_validate_utf8` (B:185) | — | R | — | W1c |
| 33 | TextToString | :111; Text.kimi:8 | :244 → `__kimi_text_to_string` (F:130) | — | R | — | W1c |
| 34 | TextTryFormat | :112; Text.kimi:10 | :246 → `__kimi_text_try_format` (F:150) | — | R | — | W1c |
| 35 | TextRelease | :113; Text.kimi:12 | :251 → `__kimi_text_release` (B:66) | Formatting.kimi:57 (a Text value's `drop`) | R | — | W1c |
| 36 | FixedBufferBytes | :114; FixedBuffer.kimi:2 | :254 → `__kimi_buffer_bytes` (B:72) | — | R | — | W1a |
| 37 | FixedBufferText | :115; FixedBuffer:3 | :259 → `__kimi_buffer_text` (B:202) | — | R | — | W1a |
| 38 | FixedBufferValidate | :116; FixedBuffer:4 | :264 → `__kimi_buffer_validate` (B:216) | — | R | — | W1a |
| 39 | FixedBufferClear | :117; FixedBuffer:5 | :269 → `__kimi_buffer_clear` (B:89) | — | R | Interpolation | W1a |
| 40 | FixedBufferReserve | :118; FixedBuffer:6 | :274 → `__kimi_fixed_reserve` (B:319) | — | R | — | W1a |
| 41 | FixedBufferIntoText | :119; FixedBuffer:7 | :276 → `__kimi_buffer_text` (same symbol as #37, another ABI row) | — | R | — | W1a |
| 42 | HeapBufferBytes | :120; HeapBuffer.kimi:2 | :254 → `__kimi_buffer_bytes` | — | R | — | W1a |
| 43 | HeapBufferText | :121; HeapBuffer:3 | :259 → `__kimi_buffer_text` | — | R | — | W1a |
| 44 | HeapBufferValidate | :122; HeapBuffer:4 | :264 → `__kimi_buffer_validate` | — | R | — | W1a |
| 45 | HeapBufferClear | :123; HeapBuffer:5 | :269 → `__kimi_buffer_clear` | — | R | — | W1a |
| 46 | HeapBufferReserve | :124; HeapBuffer:6 | :275 → `__kimi_heap_reserve` (B:325) | — | R | — | W1a |
| 47 | HeapBufferIntoString | :125; HeapBuffer:7 | :277 → `__kimi_buffer_into_string` (B:233) | — | R | — | W1a |
| 48 | WindowPush | :126; WriteWindow.kimi:2 | :278 → `__kimi_window_push` (B:357) | — | R | — | W1b |
| 49 | WindowAppend | :127; WriteWindow:3 | :279 → `__kimi_window_append` (B:374) | — | R | — | W1b |
| 50 | WindowLimit | :128; WriteWindow:4 | :280 → `__kimi_window_limit` (B:399) | — | R | — | W1b |
| 51 | WindowCommit | :129; WriteWindow:6 | :281 → `__kimi_window_commit` (B:420) | — | R | — | W1b |
| 52 | WriterWrite | :130; Utf8Writer.kimi:2 | :237 → `__kimi_writer_write_builtin` (F:491) for a built-in witness; a user witness through the write wrapper (FC.Formatting.cs:45-59) | Core.kimi:116-117, :219-221, :293-295, :508-510 (Range formatting) | R | Interpolation (each `$tryWrite` and owning-interpolation write; MirBuilder.Formatting.cs:25-35) | W1b |
| 53 | WriterStatus | :131; Utf8Writer:4 | :235 → `__kimi_writer_status` (B:16) | — | R | Interpolation (`$tryWrite`'s status test, MirBuilder.Formatting.cs:137) | W1b |

Per unit: U1 3, A1 9 (a 3, b 4, c 2), K1 7 (a 6, b 1), W1 26 (a 12, b 6, c 7 + `WriteLineUtf8`), W2 1 (`WriteLine`), **none 7**. S1 and S2
replace no Body kind: they replace the Shim kinds (`BuiltinEquals`, `BuiltinCompare`, `BuiltinFormat`; :233) and the string routines
(`LRH.cs` `StringEqual`/`StringCompare`, `FC.Strings`).

Kernel coverage: 17 of 53 kinds run in a kernel (Array: 7, 9, 11, 13, 15; Tuples: 7, 11, 13; SharedHandles: 17; Dictionary: 22, 25-28;
Interpolation: 29, 31, 39, 52, 53; all nine: 1, once). Not timed by any kernel (36): Replace/Exchange/Swap, reserve/insert/remove/
shrinkToFit, setArrayLength, the exclusive, slice and fixed-array storage kinds (16, 18-21, 23-24), and 21 of W1's 26 kinds.

**Flags (item 1).**
- 1a. Kinds 15-21 (`StorageBorrowShared`, `StorageBorrowExclusive`, `StorageOwn`, `StorageBorrowUniqSlice`, `StorageBorrowFixedShared`,
  `StorageBorrowFixedExclusive`, `StorageOwnFixed`) are in no unit's parts. A1c deletes `LRH.Arrays.cs` and "the Array `LegacyRow` items",
  which removes the helpers of 15-18 and 21 without naming their Kimigayo form; the fixed-array kernel of 19-20 (`FC.Storage.cs:20`,
  `LRH.Storage.cs`) is deleted by no row except X1's "`LegacyRuntimeHelpers*` deleted". A1 (or a new A1d) should name them.
- 1b. `FixedBufferIntoText` and `FixedBufferText` share the routine symbol `__kimi_buffer_text` with different ABI rows (LRH.Formatting:259,
  :276).
- 1c. No kernel times kinds 2-6, 8, 10, 12, 14, 16, 18-21, 23-24, 30, 32-38, 40-51 (36 kinds). For them A1, K1a, U1 and W1 have only the
  allocation tests and native families; the Strings kernel (§6) adds none of them.

## 2. The `.ll.in` defines

`grep -n '^define' src/Kimi/Compiler/Backend/<file>.ll.in`: B 5, F 15, Fl 2, R 2, W 6 = **30**. The files also hold 31 templated headers
(`grep -n '^{{'`: B 19, F 4, W 8, one W data marker `{{abort_reasons}}` W:16) whose signatures C# fills from its rows
(`LlvmModuleText.ExpandRuntime`). Loading: all five are embedded resources (`src/Kimi/Kimi.csproj:39-43`). W is written into every module
(`LlvmModuleText.cs:129, 145-148, 236-241`; `ModuleCodegen.Frame.cs:93`). B, F, Fl and R are one text (`LRH.Formatting.cs:290-312`) written
whole when the module requests `HelperNeeds.FormattingRuntime` (`ModuleCodegen.Frame.cs:77-86, 116-119`; requested by any formatting
`LegacyRow`, `LRH.cs:170`, and `FC.Interpolation.cs:62, 102, 147`); there is no per-define selection.

| Define (file:line) | Emitted or referenced by C# | Called by other `.ll.in` code | Plan disposition | Census disposition |
| --- | --- | --- | --- | --- |
| `__kimi_utf8_valid` B:99 | — | `buffer_valid` B:179; `{{text_validate_utf8}}` B:190 | delete (Buffer) | W1 |
| `__kimi_buffer_valid` B:167 | — | `{{buffer_text}}` B:204, `{{buffer_validate}}` B:218, `{{buffer_into_string}}` B:235 | delete (Buffer) | W1 |
| `__kimi_buffer_reserve` B:254 | via `FixedBufferReserve`/`HeapBufferReserve` rows | `{{fixed_reserve}}` B:321, `{{heap_reserve}}` B:327, `writer_reserve` F:200, F:205 | delete (Buffer) | **W2** (the legacy adapter reaches it) |
| `__kimi_window_address` B:331 | — | `{{window_push}}` B:364, `{{window_append}}` B:386, `format_float` Fl:141, `writer_reserve` F:237, `format_bytes` F:280, `format_integer` F:377, `format_character` F:418 | delete (Buffer) | **W2 and the float reduction** |
| `__kimi_window_advance` B:344 | — | `window_push` B:366, `window_append` B:389, `writer_reserve` F:239, `utf8_commit` F:257 | delete (Buffer) | **W2** |
| `__kimi_format_take_string` F:9 | `LRH.Formatting:197` (toString over a Body formatter, from `ModuleCodegen.Formatting.cs:108`) | `format_finish` F:111, `{{text_to_string}}` F:143 | delete (Format) | W2 |
| `__kimi_format_stack_finish` F:24 | `LRH.Formatting:76`; `FC.Interpolation:149-150` (`FormatFinish`, stack) | — | delete | W2 |
| `__kimi_format_check` F:32 | `LRH.Formatting:84-85`; `FC.Interpolation:128` (`FormatSegment` literal) | `format_finish` F:99, F:106 | delete | W2 |
| `__kimi_format_healthy` F:45 | `LRH.Formatting:80`; `FC.Formatting:45, 55` (write wrapper) | — | delete | W2 |
| `__kimi_format_hint` F:53 | `LRH.Formatting:65`; `IntrinsicRows.cs:110` (`FormatSegment`); `FC.Interpolation:103` | — | delete | W2 |
| `__kimi_format_literal` F:62 | `LRH.Formatting:102`; `FC.Interpolation:124-127` | — | delete | W2 |
| `__kimi_format_finish` F:96 | `LRH.Formatting:69-72`; `IntrinsicRows.cs:111` (`FormatFinish`); `FC.Interpolation:149-150` | `stack_finish` F:26 | delete | W2 |
| `__kimi_format_capacity` F:115 | — (`LRH.Formatting:94-97` `FormatBounds` mirrors its table) | `{{text_to_string}}` F:135 | delete | W1c |
| `__kimi_writer_fail` F:170 | `LRH.Formatting:88-89`; `FC.Formatting:59` | `writer_reserve` F:228, `{{writer_write_builtin}}` F:506 | delete | W2 |
| `__kimi_writer_reserve` F:178 | — (its `user` arm F:210-218 is `ErasedReserve`'s behaviour) | `format_float` Fl:135, `format_finish` F:105, `format_bytes` F:274, `format_integer` F:371, `format_character` F:412 | delete | **W2, after `ErasedReserve` gets a home** |
| `__kimi_utf8_commit` F:249 | — | `format_float` Fl:211, `format_finish` F:108, `format_bytes` F:282, `format_integer` F:395, `format_character` F:483 | delete | **W2 and the float reduction** |
| `__kimi_format_bytes` F:268 | — | `format_float` Fl:60, :66, :76; `format_literal` F:89; `{{format_builtin}}` F:594, :597, :603 | delete | **W2 and the float reduction** |
| `__kimi_decimal_div10` F:294 | — | `format_integer` F:366, F:385 | delete (S2 names it) | **S2 only if `formatInteger128` moves** (2.3) |
| `__kimi_format_integer` F:350 | — (reached from the `BuiltinFormat` shim, FC.Shims:58, :76, and the WriterWrite/TextToString/TextTryFormat rows) | `{{format_builtin}}` F:536-579 (10 integer kinds; i128 F:575, u128 F:579) | delete (S2 names it) | **S2 only if `formatInteger128` moves** (2.3) |
| `__kimi_format_character` F:403 | — | `{{format_builtin}}` F:587 | delete | S2 (not named in S2's row) |
| `__kimi_decimal_store` Fl:6 | — | `format_float` Fl:189 | keep, reduced | reduce |
| `__kimi_format_float` Fl:25 | — (`IntrinsicId.FormatFloat` is `Unproduced`, `IntrinsicRows.cs:82`) | `{{format_builtin}}` F:583 | keep, reduced | reduce; **depends on 6 Buffer/Format defines** |
| `__kimi_ryu32` R:7 | — | `format_float` Fl:81 | keep Ryu | keep |
| `__kimi_ryu64` R:407 | — | `format_float` Fl:84 | keep Ryu | keep |
| `__kimi_write_bytes` W:27 | — | `try_stderr` W:86, `stdout` W:170 | keep | keep |
| `__kimi_try_stderr` W:84 | — | `{{abort_message}}` W:94-109, `{{abort}}` W:122-161 | keep | keep |
| `__kimi_stdout` W:168 | — (`WriteStdout` `Unproduced`, `IntrinsicRows.cs:81`; reached from `{{write_line}}` W:301-302, `{{write_line_utf8}}` B:30-31) | as left | keep | keep (W2's `writeStdout` body needs a producer) |
| `__kimi_alloc` W:180 | `RuntimeAbi.cs:86-87`; `FC.Erasures:146`; `ObjectRuntime.cs:302`; `LRH.Arrays:125` | `{{text_heap}}` B:55, `buffer_reserve` B:283, `raw_allocate` W:224 | keep | keep |
| `__kimi_raw_allocate` W:209 | `RuntimeAbi.cs:89-90`; `IntrinsicRows.cs:70`; `FC.Intrinsics:140-143` | — | keep | keep |
| `__kimi_free` W:236 | `RuntimeAbi.cs:92-93`; `FC.Erasures:308`; `ObjectRuntime.cs:97`; `ObjectRuntime.Handles.cs:67`; `LRH.Arrays:136, 199, 204` | `{{text_release}}` B:68, `{{buffer_into_string}}` B:235/248, `buffer_reserve` B:291, `{{destroy_string}}` W:270, `{{array_free}}` W:292 | keep | keep |

Totals (census): delete 20 as the plan says, but distributed W1 3 (`utf8_valid`, `buffer_valid`, `format_capacity`); W2 11 (the eight
adapter routines `take_string` … `writer_fail`, plus `writer_reserve`, `buffer_reserve`, `window_advance`); W2 and the float reduction 3
(`utf8_commit`, `format_bytes`, `window_address`); S2 3 (`format_character`; `format_integer` and `decimal_div10` only if 2c is resolved);
reduce 2, keep Ryu 2, keep Windows 6. Defines whose callers no planned unit removes: `writer_reserve` (kept `ErasedReserve`),
`format_integer` and `decimal_div10` (kept `formatInteger128`), and `utf8_commit`, `format_bytes`, `window_address` (the float entry, whose
reduction no unit owns).

**2.3 Flags (item 2).**
- 2a. **W1 cannot delete all of B.** The legacy adapter that lives until W2 calls `buffer_reserve`, `window_address`, `window_advance`
  (through `writer_reserve`, `utf8_commit`), `{{window_commit}}`, and `FormatBuffer`'s routines are `{{text_heap}}`/`{{text_fixed}}`
  (`IntrinsicRows.cs:104-109`; FC.Interpolation:65, :70). W1's row ("`Utf8BufferRuntime.ll.in` (431) … deleted") holds only if W1 re-points
  the adapter at the Kimigayo bodies.
- 2b. **The float entry depends on Format and Buffer routines** (`format_bytes`, `writer_reserve`, `utf8_commit`, `window_address`); no unit
  is assigned the reduction (X1 only checks "Float reduced"). S2 routes floats through `formatFloat` and is the natural owner.
- 2c. **RQ16 against S2.** `formatInteger128` (interim, RQ16; kept by I1) is served by `format_integer` + `decimal_div10` (F:292-401; i128 at
  F:575, F:579); S2 deletes both. The R5 audit (`draft/Sketches/R5 Step 0/audit.md:154`) gives its runtime as F:294. Either i128 gets another
  implementation in S2, or the two defines stay until RQ16's stage.
- 2d. **Kept intrinsics whose runtime is in deleted files.** `ErasedWriter` (W1b keeps `TextWriter` as `ErasedWriter`) is `{{text_writer}}`
  B:5 (`IntrinsicRows.cs:83-88`); `ErasedReserve` is `writer_reserve`'s `user` arm. Both need a new home before B and F go.
- 2e. **Loader.** Fl and R load only through `LRH.Formatting.cs:290-312` under `Needs.Formatting`; the kept Ryu and float entry need a loader
  and a need flag (presumably `FormatFloat`'s). `ExpandRuntime` requires each row's marker exactly once, so a row and its template go in
  one commit.
- 2f. The "Buffer 5, Format 15" figures understate the deletion: B holds 19 and F 4 more templated function bodies.
- 2g. Side note: S2 deletes `FormatKind`, still read by W1/W2 code (`FC.Interpolation:126`, `LRH.Formatting:149` `FormatBound` read by
  `FC.Interpolation:168`, `FC.Formatting:101`, `FC.Shims:77`); S2 must precede or carry those readers.

## 3. Layout's handle rows

`grep -rn 'LengthWord\|Layout\.Row(\|HandleOf' src/Kimi tests`.

| Item (file:line) | Content | Readers (file:line, purpose) |
| --- | --- | --- |
| `Layout.Rows.cs:19-22` `Handles` (`[Transitional("R8")]`, reason :12) | Slice `Words(2)` length word 1 {buffer, length}; Array `Words(3)` length word 1 {buffer, length, capacity}; Dictionary `Words(7)` length word 3 {buffer, used, capacity, length, head, tail, free} (comment :14-17) | `HandleOf` :73-84 only |
| `Layout.Rows.cs:87-97` `Words(n)` | `{ [0 x i64], [n*8 x i8] }`, size/stride n*8, alignment 8, argument `ptr` | the `Handles` initializer |
| `Layout.Rows.cs:53` `Row`'s sequence arm | `HandleOf(type).Words` | `Layout.Placement.cs:72` (`Walk`: an unplaced Type's static row; every `Layout.Of`/`LoweringOf` consumer of a handle). Test: `tests/xUnitTest/Tests/LayoutTest.cs:523-536` |
| `Layout.Rows.cs:68` `LengthWord` | `HandleOf(sequence).Length` (1, 1, 3) | `FC.Arrays.cs:40` (literal stores its length), `FC.Arrays.cs:60` (`setArrayLength`), `FC.Places.cs:170` (`Len` rvalue, FC.Rvalues:62; Index bounds check, FC.Places:132). No test reads it |
| `Layout.Rows.cs:62-63` `Indirect` | names Slice, Array, Dictionary; not a row | `InstanceCollector.Layouts.cs:149`, `InstanceCollector.Witnesses.cs:266` |

Hard-coded handle offsets outside the rows (R1's goldens hold them only if the structs keep the field order): `FC.Places:131`, `FC.Arrays:41`
(buffer word 0 via `RuntimeAbi.StringReference.Layout`); `FC.Intrinsics:234-256` (`Raw.slice`, via `LoweringOf`); `LRH.Storage.cs:94-105`
`WriteCapacity` (offset 16); `LRH.Arrays.cs:93, 127, 152, 171, 173, 219` (offsets 8, 16); W:282, :284 `{{array_init}}`; `RuntimeAbi.cs:80-83`
`ArrayHandleParameters`.

String is not a handle row: `Layout.Rows.cs:106` `Primitives[String] = RuntimeAbi.String` (`RuntimeAbi.cs:42-43`, `%kimi.string`, 24 bytes,
{data, byteLength, releaseKind}; `LayoutTest.cs:68`). `DictionaryStorage.Handle` exists (`Lib/DictionaryStorage.kimi:5-14`, `#Layout("C")`,
fields buffer, used, capacity, length, head, tail, free: Field 3 = `LengthWord` 3); no C# names it. `ArrayRepr`, `SliceRepr`, `StringRepr` do
not exist (`grep -rn 'ArrayRepr\|SliceRepr\|StringRepr' src tests`: none).

## 4. The allocation-count inventory

How counting works. `NativeAllocationAudit` (`tests/xUnitTest/NativeAllocationAudit.cs:13`) is a static helper, not a test class:
`WriteFixture(name, source, Bound allocations, Bound frees, Bound bytes, …, Bound? transferred)` (:30) rewrites `HeapAlloc`/`HeapFree` to
counting hooks (:105-128), checks each counter against its inclusive `Bound` at exit (:219-221; exit 93 on failure, :94) and counts copied
bytes at the `__kimi_array_grow` and `__kimi_transfer_bytes` anchors (:18-19, :69-70). A number is the exact bound `[n, n]`; `Bound.Any` is
unbounded (:224-230). **The counts are checked when the native fixture runs**, not in xUnit. `AllocationMeasurement.Measure`
(`tests/xUnitTest/AllocationMeasurement.cs:46`) measures the compiler's managed bytes: 4 warm-up and 12 measured operations (:18-19), the
largest window (:160). Notation below: allocs/frees/bytes(/transferred); "mB" = managed bytes of a warm compiler pass.

### 4.1 The five cost classes

`DynamicArrayCostTest` (`tests/xUnitTest/Tests/DynamicArrayCostTest.cs`; helper :251-258: frees = allocs, bytes Any):

| Method / rows (line) | Asserted | Family |
| --- | --- | --- |
| SliceCopiesAllocateAtMostOnce… rows 0 / 1 / 17 / 1024 (:12-15) | 0/0/–/0; 1/1/–/0 ×3 | Slice `toArray` |
| RepeatingConstructionAllocatesAtMostOnce… rows 0 / 1 / 17 / 1024 (:22-25) | 0/0/–/0; 1/1/–/0 ×3 | Array `init(repeating:)` (setArrayLength, A1a) |
| RepeatingAllocationFailureReportsTheConstruction (:31) | exit 1, `abort KIMI_E_ALLOC…` | Array |
| RepeatingConstructionCompilesOnlyItsImplementationLoop rows true/false (:43-44) | IR shape, no count | Array |
| ZeroSizedRepeatingConstructionAllocatesNothing (:57) | 0/0/–/0 | Array |
| AppendingCopiesRetainsPreallocatedStorage (:62) | 1/1/–/0 | A1a (append, reserve) |
| ZeroSizedSliceCopiesAllocateNothing (:67) | 0/0/–/0 | Slice |
| ExclusiveViewsAndChildrenAllocateNoElementStorage (:72) | 0/0/–/Any | Slice (fixed `sliceUniq`) |
| BorrowedElementUpdatesReuseTheBuffer (:77) | 1/1/–/0 | ArrayLiteral |
| BorrowedFixedArrayUpdatesAllocateNoStorage (:82) | 0/0/–/Any | fixed array |
| DisjointPairsAllocateNoAdditionalStorage rows Array / Fixed (:87-88) | 1/1/–/0; 0/0/–/Any | Array, fixed (`tryGetPairUniq`) |
| SortingRetainsStorageWithoutFurtherAllocation (:94) | 1/1/–/0 | Array (sort → swapAt, A1b) |
| EmptyConstructionAndEmptyOperationsAllocateNothing (:99) | 0/0/–/0 | A1 (reserve 0, clear, shrinkToFit, pop, owning `for`) |
| ZeroSizedElementsNeverAllocate (:104) | 0/0/–/0 | A1 (append, insert, remove, reserve, shrinkToFit) |
| CapacityPreservingMutationAndChurnNeedOnlyTheInitialAllocation (:109) | 1/1/–/0 | A1 (churn ×2048) |
| RepeatedSharedViewsAndIterationAllocateNoStorage (:118) | 1/1/–/0 | Slice, Range |
| GrowthTransfersStayLinear… 12 rows n ∈ {1,4,5,17,1000,1024} × {Append, Reserve} (:123-134, method :135, bounds :140) | allocs = frees ∈ [1, 1+⌈log2 n⌉], transferred ∈ [0, 16n]: n=1 [1,1]/[0,16]; 4 [1,3]/[0,64]; 5 [1,4]/[0,80]; 17 [1,6]/[0,272]; 1000 [1,11]/[0,16000]; 1024 [1,11]/[0,16384] | A1a (append, reserve growth) |
| ShrinkSuccessAndFailurePreserveLiveValues rows false / true (:145-146) | 2/2/Any/0; 2/1/Any/0 with `failAllocation: 2` | A1c (shrinkToFit) |
| WarmStorageIteratorEmissionAllocatesNothing 3 rows (:166-168) | 0 mB | iterator emission |
| WarmArrayAnalysisAndEmissionAllocateNothing rows Binding/Ownership/Emission/Validation/Pipeline (:181-185) | 0 mB | Array (compiler) |
| ReloadEmitsOnlyTheCurrentlyUsedCachedHelpers (:226) | IR lacks `__kimi_array_remove_*`; no count | A1b (remove; the helper cache goes with A1) |

`DictionaryCostTest` (`tests/xUnitTest/Tests/DictionaryCostTest.cs`, all K1):

| Method / rows (line) | Asserted |
| --- | --- |
| GrowthMovesOnlyLinearStorage… (method :20, bounds :25-35: allocs = frees = 1 + doublings from capacity 4; bytes = 96 + Σ 24·cap; transferred [(cap−4)·24, 48n]) rows (1,F) :12, (1,T) :17 | 1/1/96/[0,48] |
| row (4,F) :13 | 1/1/96/[0,192] |
| row (5,F) :14 | 2/2/288/[96,240] |
| rows (17,F) :15, (17,T) :18 | 4/4/1440/[672,816] |
| rows (1000,F) :16, (1000,T) :19 | 9/9/49056/[24480,48000] |
| RepeatedSearchAndSharedIterationAllocateNoEntryOrViewStorage (:40, call :60) | 1/1/96/0 |
| WarmDictionaryCompilationReusesStorage rows Binding/Ownership/Emission/Validation/Pipeline/(Ownership, prefix 1) (:65-70; assert :124) | 0 mB |

`SliceCostTest` (`tests/xUnitTest/Tests/SliceCostTest.cs`; program `Operations` :12-25 ×1024):

| Method (line) | Asserted | Family |
| --- | --- | --- |
| FixedArraySliceOperationsAllocateNothing (:29) | 0/0/0/– | Slice, Range |
| ArraySliceOperationsAllocateOnlyTheBackingArray (:34) | 1/1/16/– | Slice (+ArrayLiteral) |
| WarmSliceAnalysisAndEmissionAllocateNothing rows Binding/Ownership/Emission (:39-41) | 0 mB | Slice (compiler) |

`Utf8CompilationCostTest` (`tests/xUnitTest/Tests/Utf8CompilationCostTest.cs`):

| Method / rows (line) | Asserted | Family |
| --- | --- | --- |
| WarmFormattingCompilationHasBoundedAllocation rows Binding, Ownership (:12-13) | `InRange(allocated, 0, 0)` (:65) | W1, W2, S2 (compiler) |
| same, rows Emission, Validation, Pipeline (:14-16) | ≤ 12 × 256 = 3,072 mB per window (:64-65; "measured: 216 bytes") | W1, W2, S2 (compiler) |
| ReloadRecomputesFormattingBoundsAndSelectedCalls (:69) | IR contains `alloca [26 x i8]`; stdout `value=42` | W2, S2 |

`WorkloadCostTest` (`tests/xUnitTest/Tests/WorkloadCostTest.cs`; helper :58-59: allocs and frees each `Bound(1, n)`, bytes Any):

| Method / rows (line) | Asserted | Family |
| --- | --- | --- |
| ProcessingWorkloadAllocationsAreBounded (:21; `Bound = 3`, :17) | [1,3] / [1,3] | Milestone35 (Array, Dictionary, closures, formatting) |
| ScaledWorkloadAllocationsStayLogarithmic rows 64 / 1024 (:28-29; bound :50 = 1 + ⌈log2 n⌉ + 2) | [1,9]; [1,13] | workload |

Totals: 31 methods (18 Fact, 13 Theory), 76 cases; 49 native-audit rows, 22 managed-byte rows, 5 shape/IR asserts. 27 methods carry
`Purpose=Allocation`; untagged: DynamicArray :31, :45, :226 and Utf8Compilation :69.

### 4.2 R8-family native-audit rows outside the five classes

`grep -rn 'NativeAllocationAudit.WriteFixture' tests`: 254 call sites in 93 files. The audit's own test, `NativeAllocationAuditTest.cs`
(:28-97; sources Append :16, Growth :19, Entries :22), checks the hook (1/1/16, 2/2/48/16, 2/2/288/96, failing bounds exit 93). The rows R8's
families must hold (allocs/frees/bytes; Tn = a theory of n rows):

| Family | Rows (file:line expected) |
| --- | --- |
| K1 | BorrowedDictionaryIndexTest:87 1/1/192; DependentDictionaryLiteralTest:148 1/1/128; DependentPointerEmissionTest:76 1/1/128; DictionaryCapacityTest:102 2/2/288/96; DictionaryEntryStorageTest:23, :41, :58, :73 1/1/{64,96,192,96}; DictionaryIndexTest:23, :46, :68, :98, :114, :140 {1/1/96, 1/1/192, 1/1/224, 2/2/192, 1/1/192, 1/1/128}; DictionaryIterationTest:33 T2 1/1/96, :77 T2 1/1/192, :89 0/0/0, :103 1/1/256, :122 T2 1/1/192, :139 1/1/192, :153 1/1/64; DictionaryLiteralEmissionTest:168 T3 1/1/{64,96,96}; DictionaryOperationsTest:18, :51, :79, :110, :132, :192, :209 1/1/{128,96,96,128,192,160,256}; DictionaryRemainderTest:210 1/1/96; DictionaryShrinkTest:36 T2 2/(1 or 2)/240 (fail 2), :42 1/1/192, :69 2/2/240; DictionaryStorageTest:13, :18, :23, :28, :33 {0/0/0, 1/1/96, 2/2/288, 1/1/96, 1/1/96}; DictionaryStoredReferenceTest:142 T3 1/1/128 |
| K1 + A1 | NestedCollectionTest:23 T9 (allocs = frees: 2/320, 2/208, 2/416, 2/112, 2/192, 2/192, 1/96, 1/96, 2/416); :55 T14 (3/512, 3/416, 2/416, 3/512, 3/512, 2/416, 3/512, 3/512, ArrayPop 2/320, 2/192, 4/384, 3/416, 3/416, 3/416) |
| Array, Slice reads | SharedElementReadTest:28 T2 1/1/16, :45 T2 1/1/96, :63 T2 1/1/32, :85 1/1/96 |
| S1 | SharedStringTest:27 3/3/11, :43 0/0/0, :85 0/0/0, :58 T2 {1/1/96, 0/0/0}, :77 1/1/96 |
| S2 | Utf8ConversionTest:24 T9 (Integer 1/11, Wide 1/39, Bool 1/5, Char 1/4, Unit 1/2, String 1/8, EmptyString 0/0, View 1/8, EmptyView 0/0), :33 T2 {1/1/11, 1/1/4}, :45 T4 0/0/0, :65 0/0/0, :93 2/2/6; Utf8FormatTest:33 T21, :41 T3, :57 T4, :85 all 0/0/0; Utf8IntegrationTest:48 0/0/0 |
| W1 | Utf8BorrowTest:61 0/0/0; Utf8BufferTest:178 T5 (Fixed 0/0, Empty 0/0, Heap 1/16, EmptyTransfer 0/0, AllocatedEmptyTransfer 1/16); Utf8UserFormatTest:25 T2, :41, :64 T2, :96 0/0/0, :125 T3 {1/1/3, 1/1/3, 1/1/4}; Utf8WriterTest:16 T3, :32 0/0/0 |
| W1 / W2 | Utf8TryWriteTest:25 T4, :63, :80, :96, :138 T2, :172 0/0/0; :189 2/2/33 |
| W2 | Utf8ConsoleTest:36 0/0/0 + `alloca [33 x i8]` (:40); :53 T4 (1023 0/0, 1024 0/0, 1025 1/1025, 1024Full 0/0); :78 T15 0/0/0; :96, :110, :137 0/0/0; :124 1/1/9; Utf8InterpolationTest:19 1/1/33, :34 1/1/4, :97 T3 {1/1/21, 1/1/18, 1/1/21}, :103 0/0/0, :117 1/1/2, :134 2/2/22, :171 T3 {2/2/60, 2/2/34, 2/2/12} |
| float (reduce) | Utf8FloatTest:36 T17 0/0/0, :44 T2 {1/1/17, 1/1/24} |

The remaining call sites (closures, objects, Rc/Arc, generics, drops) are in no R8 family; the grep above lists them.

## 5. The texts to revise (plan §5.2 SP1–SP4)

The SP list repeats PLAN10 §11 (`draft/Proposals/2026-10-10 Compiler Size Reduction and Single MIR Plan.md:625, 629, 633`), written before
the SPEC rewrite `72fb4b791` (2026-10-15), which renumbered chapters 21-22 and merged utf8-formatting into SPEC §22.9-22.10 and the testing
profile into IMPL §20.6. Old numbers checked with `git show 72fb4b791^:<file>`; headings with `grep -n '^#'`.

| Plan cite | Now (file:line) | Status | R8 fact |
| --- | --- | --- | --- |
| SPEC §22.1 | `docs/spec/22-core-execution-and-foreign-functions.md:7` Required Kimi declarations | matches | :11 identity "never by name" (SP2); :40 `Intrinsics` (replace/exchange/swap); :41 `Raw`; :43 `Console.writeLine`; :53 `string`. No representation-struct text yet (SP1 adds it) |
| SPEC §22.4 | 22-…:327 Program startup and static storage | **wrong subject** | `writeLine` is now **§22.7 Console output** (:471, overloads :473, behaviour :475) |
| SPEC §22.5.5 | — (§22.5 :383 has only 22.5.1 :387, 22.5.2 :411) | **missing** | old "String handle and writeLine" deleted; facts now at IMPL §21.1.3:67, SPEC §3.1.2:62, §22.5.2:435 (`KIMI_E_STRING_RELEASE`), IMPL §21.4.4:168 |
| SPEC §6.5 | `docs/spec/06-declarations-and-containers.md:351` Enums | **wrong subject** | Attributes are now **§6.7** (:413); :417 "The recognized Attributes are `#Layout`, `#LibraryImport` and `#Test`" (SP2) |
| SPEC §15.7 | `docs/spec/15-ownership-and-lifetime-analysis.md:792` Whole-value updates | matches | :794 the updates are "compiler-intrinsic" (U1) |
| SPEC §15.7.1 | 15-…:814 Evaluation and transfer | matches | :816 no constructor, user code, destruction or Abort (SP3's unsafe obligation) |
| utf8-formatting §1.1 | — (file merged) → SPEC §22.9.1 Declarations (22-…:532) | **stale** | :551 "Only `Text.fixed`, `Text.writer` and `reserve` form these Loans" (W1); no compiler wording left |
| utf8-formatting §6.2 (also plan :514, :647, :778, §10.2 :783) | — → SPEC §22.10.5 Allocation (22-…:675, bounded Types :677) | **stale** | the 33-byte row and the 1,024-byte limit are no longer in SPEC (only `Utf8ConsoleTest`); W2's bound declaration may change :677's closed list |
| testing-profile | — → IMPL §20.6 Test execution (`docs/impl/20-compilation-configuration.md:160`) | **stale, no R8 fact** | the protocol is implementation-defined (:195) |
| IMPL §21.2.2 | — (§21.2 Foreign function ABI :74 has no subsections) | **missing** | old "Value metadata and object descriptors"; only IMPL §21.1.3:67 remains |
| IMPL §21.5.3 | — (chapter 21 ends at §21.4) | **missing** | old "Checked instructions and raw pointers"; nearest §21.3.3 :114, §21.4.3 :157, no R8 fact (RP9) |
| SETTLED (new entry) | `docs/SETTLED.md:256-268` | reconcile | the constructor entry's reason (:268, "a rule available only to the Kimi library…") meets G1's library-only attributes and A1's bodies |
| STYLE, LIBRARY, README | see below | — | — |

Other places that state a fact R8 changes (not in SP1–SP4):

| File:line | Fact | Unit |
| --- | --- | --- |
| `docs/spec/03-types-and-values.md:62` (§3.1.2) | string storage layout implementation-defined | R1 (SP1's "identical layout" conflicts unless the structs stay internal) |
| `docs/impl/21-layout-runtime-and-code-generation.md:67` (§21.1.3) | string representation and runtime metadata implementation-defined; the size table :52-60 has no handle sizes | R1 (likely home of SP1's text) |
| `docs/spec/05-raw-pointers-and-unsafe-memory.md:40` (§5.2) | an operation contract is a library function "without a body (§22.1)" | U1, A1 (the bodyless set changes) |
| 05-…:44, `docs/spec/appendices/E-terminology.md:97` | the punnable Types (no pair list) | R1/RQ11 cite "Appendix E's pairs": the list is §5.2:44 |
| 05-…:116, :126 (§5.4) | a raw conversion is a Copy only for the same normalized Type | R1 (needs §5.2's extension) |
| `docs/spec/appendices/D-deferred-features.md:25` | Attribute semantics beyond Layout/LibraryImport/Test deferred | G1, SP2 |
| 22-…:635, :677 (§22.10.1, §22.10.5) | built-in formatting reserves the exact length once; the bounded Types | S2, W2 |
| `docs/LIBRARY.md:320, 359, 456, 519-520, 606` | fixed-array entry conformances recorded by the compiler; "formatting profile … required costs"; `Console.writeLine` links §22.1 (should be §22.7) | G1, W2 |
| `src/Kimi/Library/README.md:14-15, 29-31, 46-55, 62, 63-69, 87-88, 101-105, 113-114, 119-120, 134-149` | Array/Slice storage "supplied by the compiler"; signature-only resources; catalog and compiler-function kind; DictionaryStorage C layout "to agree with the backend"; Text "uses compiler-supplied LLVM IR … a port is optional" | R1, A1, G1, W1, S2, W2 |

Pre-existing stale references (not R8's): `LIBRARY.md:318` and `Library/README.md:114` "(SPEC §22.2)" (now Iteration); `LIBRARY.md:519` links
§22.1 for `writeLine`. Outside R8: plan R9-B1 (:536) cites IMPL §20.8.3, now §20.3.2 (`docs/impl/20-…:111`).

## 6. A Strings kernel (sketch; not written)

The other kernels' conventions (`src/Benchmark/O2Corpus.md`; `src/Benchmark/Kimi/Corpus/Tuples.kimi:16-24`): one Application
`src/Benchmark/Kimi/Corpus/Strings.kimi`, one line `checksum N` printed by `Console.writeLine`, the same at O0 and O2; the linear
congruential `next` and the invertible `fold` (FNV-1a step, xorshift) over each round's sum; a few tenths of a second at O2; the expected line
added to `$expected` in `src/backend/windows-x64/benchmark-o2-corpus.ps1:30-33` (the script fails if a kernel lacks one) and a row in
O2Corpus.md's table. Timing only: allocation stays with the tests of §4.

What it should time, each part in its own non-inlined function so `opt -O2` output can be read per part:

| Part | Work per round | Covers (today → R8) |
| --- | --- | --- |
| Comparison | 64 heap strings built once (from integer formatting: a shared 12-byte prefix, lengths 0-40, some equal, some differing only in the last byte) plus 8 Static literals; `==`/`!=` over all 64×64 pairs, `<`/`>=` over all pairs of 32, an insertion sort of 64 by `<` (as Tuples), one Static-vs-heap block | `__kimi_string_equal`/`__kimi_string_compare` (`LRH.cs` StringEqual/StringCompare, `FC.Strings`) → S1's `compareBytes` and the `Equatable`/`Comparable` witness bodies; this part decides S1's intrinsic fallback (plan §8.2 R8-S1, PLAN10) |
| Destruction | 256 owned strings made (`Text.toString` of an `i64`, an owning interpolation), stored in an `Array<string>` with Static ones mixed in, then `clear` and a scope drop; one string moved out by `pop` and dropped | `{{destroy_string}}` (W:262) through both release kinds (heap, Static), Array clear glue → S1 ("release kinds unchanged"), A1c's `destroyValues<T>` |
| Integer formatting | `$tryWrite` into a cleared `Text.fixed` buffer of `i8`, `i16`, `i32`, `i64`, `u64`, `isize` values spanning 0, ±1, extremes and 1-19 digits; one `Text.toString(i32)` | `__kimi_format_integer`, `__kimi_decimal_div10` via the `BuiltinFormat` shim → S2's Kimigayo formatting. Leave `i128` out (RQ16 keeps `formatInteger128`) and floats out (Ryu stays) |

Sizes: about 16,384 rounds for comparison and destruction and 1,048,576 formatted values (the Interpolation kernel's count), to be tuned so
each part takes a third of a few tenths of a second at O2 on the reference machine; the round counts are fixed at the kernel's commit. Checksum:
each round's sum folds the comparison results weighted by pair index (as Tuples.kimi), the destroyed count, and the written lengths plus the
last byte of each formatted value. Overlap to avoid: Tuples' third leaf already compares short Static strings (Tuples.kimi:5-14), and
Interpolation already formats `isize` and `bool`; this kernel adds heap strings, long common prefixes, other integer widths and destruction.
Review: R for the kernel (plan L0 row); it lands before S1 so S1's A/B has a base.
