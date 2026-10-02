# Raw pointer storage boundary (2026-10-02)

The raw pointer units replaced the private storage lending primitives (`lend`, `split`, `lendKey`, `lendValue`,
`splitValue`, `inlineBase`, `release`, `placeValue`, `addressOfI64`) with raw Place borrows and takes, `@raw`,
`Kimi.Raw` and `Loan<T>` Fields. This timing compares the iteration, indexing, growth, Dictionary and formatting hot
paths built by the compiler before those units (`b528d4ed`) and after them (`acca2dd3`). Timing only, outside
normal Verify.

`artifacts/benchmarks/raw-pointers-before-after/bench.ps1` builds one source per case with both Release compilers
at O2 and O0, discards each executable's first launch, alternates the two executables' runs, requires equal
output and compares the address-free instruction sequences. The cases share one prelude (an `Array<i64>` and a
`Dictionary<i64, i64>` of 1,024 entries, an `[8 of i64]`, a `HeapBuffer`) and repeat a body: shared and exclusive
`for` over the Array, i32 indexing, a Slice selection with its iteration, fixed-array iteration, append/pop,
owned-string append/clear, Dictionary lookup (a linear key search through the equality callback), shared and
exclusive Dictionary iteration, insertion/removal, and `Utf8Writer.write` of integers.

Windows x64, .NET 10.0.401 SDK. 11 O2 and 5 O0 runs per case, then 31 O0 runs for the cases whose first O0
difference exceeded 3%.

| Result | Observation |
| --- | --- |
| O2 | All 12 cases produce the same instruction sequence before and after; differences in min/median (within ±1.3%, except the high-variance `WriteIntegers` at ±3% with 70 ms deviation) are placement or noise. |
| O0 | Code differs because the library bodies changed. With 31 runs, `ForArray` −1.3%, `IndexArray` −2.1%, `ForSlice` −0.3%, `ForFixed` −2.3% and `DictionaryInsertRemove` −0.9% (medians); no case is slower beyond noise. |
| Size | O0 `.text` grows by 208 bytes in every case: the runtime's `__kimi_raw_allocate`, emitted unconditionally like the other runtime helpers. O2 `.text` is unchanged. |

Evidence: `artifacts/benchmarks/raw-pointers-before-after/{results,results-o0-recheck}.json` and `run*.txt`.
