# Windows x64 backend

The supply catalog is `profile.json`, embedded by the compiler and read by the builders. Package release **0.1.1** follows the shared `Directory.Build.props` Version. Supply ABI **3** holds `__chkstk` alone: the four C memory functions (`memcmp`, `memcpy`, `memmove`, `memset`) left the archive, since every generated module defines them in its runtime's LLVM IR (IMPL 21.4.7; the Tatara plan's decision 24). LLVM **22.1.8** and the windows-x64-v1 profile name are independent version contracts. The adopted one-member native COFF archive has SHA-256:

```text
626a858a08d70544c0a37b4e0cc6a2df39134d3eddade59e3ff92506f17f125f
```

This replaces the ABI 2 five-member archive `4ef5b90f70bf22dea6fd2a6fb495f11025e3ecf4b7afa68b356801c9252c5d26`, whose `chkstk.obj` member is byte-equal to this archive's. Filename-only archive member names avoid checkout-path dependence. Local builds still report a *tested candidate*: their actual hash must match the catalog before use. The adopted archive is committed beside the catalog (`kimi_backend_windows_x64_v1.lib`, with the generated `kernel32.lib`, whose SHA-256 the catalog's `kernel32.sha256` pins) and carried by the compiler, which stages and links them in every native build. Candidates, pinned and verified reproductions included, remain in `artifacts/backend/windows-x64/`; none is installed into a toolchain. Adopting another archive or kernel32 library changes its hash in `profile.json` and commits the reproduced file in the same commit. A different archive needs renewed review, verification and an immutable shared package release. These changes do not publish a package.

## Assembly and ABI review

The member defines one strong symbol, `__chkstk`. It contains no calls, bitcode, undefined symbols, default library directives, CRT/TLS initialization or `_fltused` definition.

`__chkstk` preserves RAX and saves/restores RCX/RDX. Its initial probe address is the caller's pre-call RSP (`lea 24(%rsp)` after the return address and two pushes). Probes move down by 4096 bytes and then the remaining tail. The two pushes match `ALLOC_SMALL size=8` unwind codes at offsets 1 and 2. The caller allocates its frame; no allocation is hidden in the helper.

## Validation

Pinned native O0/O2 tests (`tests/native.c` with `tests/probe.S`, linked with the candidate archive) cover probe sizes zero/page/multipage, register restoration and a compiler-generated 32 KiB stack frame. Disassembly, dependencies and unwind records are inspected for the member. The memory functions' cases moved with the functions to the runtime: `tests/memory.c` (offsets 0–15, byte/chunk boundaries, both overlap directions, exact return pointers, protected-page-adjacent buffers through 4096 bytes; for memcmp both pointer alignments, every differing byte through length 65, unsigned ordering across `0x7f`/`0x80`, equal ranges, reversed arguments and null zero-count) runs at O0 and O2 on the runtime's memory part alone, with no archive (`TataraExecutorTest.TheMemoryPartPassesTheHelperSuite`).

String comparisons call the Kimi library witnesses (`StringWitness.equals` and `compare`; R8-S1d2, R8-S1e). String comparison fixtures retain unknown-input helper bodies through O2, compare distinct buffers and embedded NUL bytes, and use inaccessible pointers to test paths that must skip byte reads. Original generated modules and separate runtime lifetime audits test comparison Loans, transfers, cleanup, Abort and divergence. O0/O2 objects are checked against actual backend and Windows import providers before custom-entry `/NODEFAULTLIB` linking and execution. Current execution boundaries are recorded in [STATUS](../../../docs/STATUS.md#4-llvm-generation-coverage); historical counts remain in the source snapshot linked there.

The independent runtime adapter suite covers partial/failed writes, invalid handles and lengths, nonrecursive diagnostics, allocation/free failures, Static/Heap destruction and no cleanup after Abort. String comparison adds no runtime operation or Windows API. Kernel32 imports continue to come from project-owned `kernel32.def` and the pinned llvm-dlltool executable; the library they generate is the committed `kernel32.lib`, pinned by `kernel32.sha256`.

Generated reports retain tool, source and archive hashes in `artifacts/backend/windows-x64/verification.json`; runtime results are under `artifacts/verify/emission-native/{Debug,Release}`. Scalar object dependency lists are under the repository's `artifacts/verify/scalar-native`. Reports are local evidence, not committed binaries; the two adopted libraries are the only committed binaries. The review does not claim general borrow/object execution, i128 helpers, complete machine-state exploration or measured throughput improvements. NativeAOT tests were not run for C.51.
