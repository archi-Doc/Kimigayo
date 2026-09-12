# Windows x64 backend 1.0.0

The supply catalog is `profile.json`, embedded by the compiler and read by the manual builder. Its four-symbol native COFF archive is adopted for windows-x64-v1 / LLVM 22.1.8 with SHA-256:

```text
74f87661ca1493fedd9d688df5e64f1346ddf9190a12db2f1b012b2f509b500c
```

Two builds with the pinned toolchain reproduced this hash. `build.ps1` still reports a *tested candidate*: a local rebuild is not authority to change the catalog. Select the existing adopted archive by its actual hash. Replacing it requires new review/verification and an immutable new package version.

## Review and evidence

The project-owned assembly was reviewed with its generated COFF disassembly/unwind records. Memory helpers modify only volatile integer registers, use bounded eight-byte/byte accesses, contain no calls, and have zero-byte prologues with empty unwind codes. Memmove selects backward copy only for overlapping destination-above-source ranges; zero/self-copy accesses no bytes. No SIMD state or nonvolatile registers are changed.

`__chkstk` preserves RAX and saves/restores the only two modified registers, RCX/RDX. Its initial probe address is the caller's pre-call RSP (`lea 24(%rsp)` after the call return address and two pushes); successive probes move down by 4096 bytes and finally the remaining tail. The two one-byte pushes match the two `ALLOC_SMALL size=8` unwind codes at offsets 1 and 2. They hold volatile registers, so unwind restores the stack without a nonvolatile-register recovery rule. No frame allocation is hidden inside the helper.

Pinned native O0/O2 tests exercised offsets 0–15, copy/fill boundaries and exact return values, both overlap directions, protected-page-adjacent buffers, zero/page/multipage probes, register restoration, and a compiler-generated 32 KiB stack frame. Archive members have exactly four strong exports, no undefined symbols, calls, bitcode, `_fltused`, default library directives or CRT/TLS initialization sections.

The actual Kimigayo-emitted modules also passed LLVM verification, O0/O2 object generation, unwind/dependency inspection, custom-entry `/NODEFAULTLIB` linking and execution for compiler Debug and Release. The generated executable outputs were verified byte-for-byte. A separate test-only OS adapter exercised partial/failed/zero/excess writes, handle failures, checked address/length limits, DWORD chunking, nonrecursive diagnostics, DWORD error formatting, allocation/free failure, Static/Heap destruction and no cleanup after Abort. Production code retains the six runtime operations and seven Windows APIs.

The selected `kernel32.lib` matches SDK 10.0.22621.0 x64. Local reports in `backend/windows-x64/bin/verification.json` retain tool/source/library hashes; generated-module reports are in `bin/emission-native/{Debug,Release}/verification.json`. These reports are generated evidence, not committed binaries or fabricated supply identities.

This adoption establishes the four helper contracts and the literal-output execution slice. It does not claim execution coverage for general structs/enums, borrows, objects, callbacks, arbitrary arithmetic helpers, or Library ABI. No throughput improvement or complete Windows-context/unwind-state exploration is claimed.
