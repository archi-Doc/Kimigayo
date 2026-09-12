# Windows x64 backend

This directory supplies the native helpers for SPEC §21.5.7. The reviewed package is pinned in [profile.json](profile.json); [ADOPTION.md](ADOPTION.md) records its scope and validation. Local builds remain candidates until their actual hash matches the adopted catalog. The helpers are separate from the six runtime operations emitted into Kimigayo LLVM modules. LLVM **22.1.8** is the current profile version.

## Build and test

Run in PowerShell with the matching LLVM tools on PATH (or provide `-LlvmBin`):

```powershell
./backend/windows-x64/build.ps1 -Kernel32 'C:/Program Files (x86)/Windows Kits/10/Lib/10.0.26100.0/um/x64/kernel32.lib'
```

Choose an actual installed x64 SDK import library. The script does not download tools, search for a convenient SDK, or modify the machine configuration. It records the selected library path/hash. `-AllowUnpinnedToolchain` permits exploratory tests only and records the mismatch.

Outputs are under ignored `bin/`: the native `.lib`, per-member COFF/unwind/disassembly reports, C-generated LLVM test IR, O0/O2 objects/executables, and `verification.json`. A failed run leaves `status: incomplete`; old files do not establish current success. Executable tests have a 30-second timeout.

## Implementation

Each archive member defines exactly one strong external symbol: `memcpy`, `memmove`, `memset`, or `__chkstk`. The memory functions use baseline x64 integer loads/stores in eight-byte chunks and byte tails, with no overread, heap allocation, calls, or nonvolatile register changes. The forward copy loop is shared in assembly source. Memmove selects direction from the actual overlap and returns immediately for identical addresses. These are baseline implementations; SIMD tuning requires separate measurements and acceptance tests.

The helpers are assembled directly to native COFF, so the user's LLVM optimization pipeline cannot turn their bodies into recursive libcalls. They contain no bitcode, CRT startup, TLS, default library directives, stack protector dependencies, or `_fltused`. Each has Windows unwind metadata. `__chkstk` takes and preserves RAX, preserves the other general registers, probes downward in 4096-byte steps, and leaves the caller to allocate its frame. It uses no ordinary C wrapper around the special probe ABI.

The sources are project-owned MIT-licensed implementations, not copied compiler-rt files. LLVM is used as the assembler and toolchain.

## Current checks

- Enforce the version of tools that report it; llvm-lib exposes no version switch and is explicitly recorded as unversioned with its executable hash. Retain source, build-script and kernel32 hashes, and reject source changes during verification.
- Inspect every member for native COFF, unwind records, absent undefined symbols/default-library dependencies, and no calls. Check the archive's four exported symbols and absence of `_fltused`.
- Test memory operations across byte/word boundaries, offsets 0–15, both overlap directions, self-move, exact return pointers, byte truncation and unchanged surrounding storage.
- Test zero through 4096-byte buffers immediately adjacent to inaccessible pages, detecting overreads and overwrites.
- Check probe sizes around page boundaries and across multiple pages, RAX/RCX/RDX preservation, and a compiler-generated 32 KiB stack frame.
- Verify test LLVM IR, run O0 and `default<O2>`/llc O2, link with a custom entry and `/NODEFAULTLIB`, and require native process exit code zero.

The test harness uses additional Windows memory-protection APIs solely for fault-detection tests. The production archive has no external dependencies. The test executable supplies its own `_fltused` marker and imports ExitProcess; it does not rely on CRT initialization.

## Adoption boundary

The report is a **tested candidate**, not the compiler's adopted supply catalog: `adopted` stays false and `packageVersion` stays null. Preserve that distinction when consuming it. Hashes are computed from the actual output, never placeholders.

The current catalog was frozen after assembly/unwind review and generated Kimigayo module validation described in ADOPTION.md. A different archive needs renewed review, native and generated-module verification, and a new immutable catalog version. Helper tests alone do not cover all runtime/ABI features, i128 expansion, all possible machine states, or compiler emission. They do not establish a throughput improvement.

Rebuilding is repeatable from these sources and recorded tools; adoption must compare the resulting archive hash rather than assume filename or timestamp equality. The build script does not publish or install a runtime package.

For the one-line Application, project/toolchain configuration, manual builder and native emission tests, see [the Hello example](../../examples/Hello/README.md). `manual-build.ps1` is invoked separately from the compiler; `test-emission.ps1` verifies the generated modules and test-only runtime fault adapters.
