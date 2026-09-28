# Windows x64 backend

The supply catalog is `profile.json`, embedded by the compiler and read by the builders. Package release **0.1.1** follows the shared `Directory.Build.props` Version. Supply ABI **2** adds `memcmp`; LLVM **22.1.8** and the windows-x64-v1 profile name are independent version contracts. The adopted five-member native COFF archive has SHA-256:

```text
4ef5b90f70bf22dea6fd2a6fb495f11025e3ecf4b7afa68b356801c9252c5d26
```

This replaces the ABI 1 four-symbol archive `30e6940ecb13b6ca0634d8b99e8596b0d1680aa6bae141adb012721c410401cd`. The existing four members are unchanged. Filename-only archive member names avoid checkout-path dependence. Local builds still report a *tested candidate*: their actual hash must match the catalog before use. Pinned, verified reproductions are installed into the selected toolchain's `windows_x64` directory; other candidates remain in `bin/` without replacing the installed archive. A different archive needs renewed review, verification and an immutable shared package release. These changes do not publish a package.

## Assembly and ABI review

Each member defines one strong symbol: `__chkstk`, `memcmp`, `memcpy`, `memmove` or `memset`. The memory helpers use bounded eight-byte accesses and byte tails, modify only volatile integer registers, contain no calls, and have empty unwind codes. They contain no bitcode, undefined symbols, default library directives, CRT/TLS initialization or `_fltused` definition.

`memcmp` follows the Microsoft x64 C ABI. It checks the count before accessing memory. For a differing eight-byte chunk, XOR and bit-scan-forward locate the first differing byte in little-endian order; zero-extended byte subtraction supplies the signed result. Equal chunks advance by eight bytes, and the tail never overreads. It performs no writes or allocations and accepts null pointers when count is zero. Native COFF assembly prevents LLVM O2 from replacing its implementation with a recursive libcall.

Memmove copies backward only for overlapping destination-above-source ranges. Zero-count and self-move paths access no bytes. The other memory helper contracts are unchanged.

`__chkstk` preserves RAX and saves/restores RCX/RDX. Its initial probe address is the caller's pre-call RSP (`lea 24(%rsp)` after the return address and two pushes). Probes move down by 4096 bytes and then the remaining tail. The two pushes match `ALLOC_SMALL size=8` unwind codes at offsets 1 and 2. The caller allocates its frame; no allocation is hidden in the helper.

## Validation

Pinned native O0/O2 tests cover memory operations at offsets 0–15, byte/chunk boundaries, both overlap directions, exact return pointers, and protected-page-adjacent buffers through 4096 bytes. Memcmp additionally checks both pointer alignments independently, every differing byte through length 65, unsigned ordering across `0x7f`/`0x80`, equal ranges, reversed arguments and null zero-count. Probe tests cover zero/page/multipage sizes, register restoration and a compiler-generated 32 KiB stack frame. Disassembly, dependencies and unwind records are inspected for every member.

Generated string-comparison modules use memcmp for content equality and common-prefix ordering. Fixtures retain unknown-input helper bodies through O2, compare distinct buffers and embedded NUL bytes, and use inaccessible pointers to test paths that must skip byte reads. Original generated modules and separate runtime lifetime audits test comparison Loans, transfers, cleanup, Abort and divergence. O0/O2 objects are checked against actual backend and Windows import providers before custom-entry `/NODEFAULTLIB` linking and execution. C.51 validation counts are recorded in [STATUS](../../STATUS.md#4-llvm-generation-coverage).

The independent runtime adapter suite covers partial/failed writes, invalid handles and lengths, nonrecursive diagnostics, allocation/free failures, Static/Heap destruction and no cleanup after Abort. String comparison adds no runtime operation or Windows API. Kernel32 imports continue to come from project-owned `kernel32.def` and the pinned llvm-dlltool executable; their supply identities are unchanged.

Generated reports retain tool, source and archive hashes in `bin/verification.json`; runtime results are under `bin/emission-native/{Debug,Release}`. Scalar object dependency lists are under the repository's `bin/scalar-native`. Reports are local evidence, not committed binaries. The review does not claim general borrow/object execution, i128 helpers, complete machine-state exploration or measured throughput improvements. NativeAOT tests were not run for C.51.
