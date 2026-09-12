# Windows x64 backend

This directory supplies the native helpers for SPEC §21.5.7. The reviewed package is pinned in [profile.json](profile.json); [ADOPTION.md](ADOPTION.md) records its scope and validation. Local builds remain candidates until their actual hash matches the adopted catalog. The helpers are separate from the six runtime operations emitted into Kimigayo LLVM modules. LLVM **22.1.8** is the current profile version.

## Build and test

Run in PowerShell with the matching LLVM tools on PATH (or provide `-LlvmBin`):

```powershell
./backend/windows-x64/build.ps1 -LlvmBin 'C:/App/llvm'
```

No Windows SDK import library is required. kernel32.def is the shared source for the compiler and scripts. They run llvm-dlltool to generate an x64 import library, validate its DLL and ten API exports, and link that output. The script does not download or install tools. llvm-dlltool is required and its approved executable SHA-256 is pinned in profile.json; it has no version banner. A different binary requires the explicit exploratory override, with a warning and unverified record. The expected LLVM release comes from `profile.json`, also embedded in the compiler. `build.ps1` and `manual-build.ps1` reject version mismatches by default. `-AllowUnpinnedToolchain` permits exploratory work only, warns for every mismatched tool, and records expected/actual versions and `unverifiedToolchain: true`; all other checks still apply. Missing tools and unreadable versions always fail. `test-emission.ps1` requires matching tools and matching candidate verification, with no override.

Run `./backend/windows-x64/test-toolchain.ps1` for version-policy regression tests without an LLVM installation. Run `./backend/windows-x64/test-kernel32.ps1 -LlvmBin C:/App/llvm` for import generation, tool identity, failure, privacy and reproducibility tests.

Outputs are under ignored `bin/`: the native `.lib`, per-member COFF/unwind/disassembly reports, C-generated LLVM test IR, O0/O2 objects/executables, and `verification.json`. A failed run leaves `status: incomplete`; old files do not establish current success. Executable tests have a 30-second timeout.

Saved native diagnostics, build/verification JSON reports, and LLVM source metadata replace the checkout root with `/_/project` and the current user's home directory with `/_/user`. Archive member names and LLVM object output names use filenames without directories. These paths describe the inputs; use the recorded SHA-256 hashes to identify the actual files. Tool invocation and link manifests still use resolvable paths. LLVM program string constants are preserved. Run `./backend/windows-x64/test-artifact-paths.ps1` to check this policy. Local .NET/NuGet `obj/` caches and build logs may still contain absolute paths; they are ignored by Git and are not shareable build reports.

## Implementation

Each archive member defines exactly one strong external symbol: `memcpy`, `memmove`, `memset`, or `__chkstk`. The memory functions use baseline x64 integer loads/stores in eight-byte chunks and byte tails, with no overread, heap allocation, calls, or nonvolatile register changes. The forward copy loop is shared in assembly source. Memmove selects direction from the actual overlap and returns immediately for identical addresses. These are baseline implementations; SIMD tuning requires separate measurements and acceptance tests.

The helpers are assembled directly to native COFF, so the user's LLVM optimization pipeline cannot turn their bodies into recursive libcalls. They contain no bitcode, CRT startup, TLS, default library directives, stack protector dependencies, or `_fltused`. Each has Windows unwind metadata. `__chkstk` takes and preserves RAX, preserves the other general registers, probes downward in 4096-byte steps, and leaves the caller to allocate its frame. It uses no ordinary C wrapper around the special probe ABI.

The sources are project-owned MIT-licensed implementations, not copied compiler-rt files. LLVM is used as the assembler and toolchain.

## Current checks

- Enforce the version of tools that report it; llvm-lib exposes no version switch and is explicitly recorded as unversioned with its executable hash. Pin the versionless llvm-dlltool binary hash; retain source, build-script, normalized definition and generated kernel32 hashes, and reject source changes during verification.
- Inspect every member for native COFF, unwind records, absent undefined symbols/default-library dependencies, and no calls. Check the archive's four exported symbols and absence of `_fltused`.
- Test memory operations across byte/word boundaries, offsets 0–15, both overlap directions, self-move, exact return pointers, byte truncation and unchanged surrounding storage.
- Test zero through 4096-byte buffers immediately adjacent to inaccessible pages, detecting overreads and overwrites.
- Check probe sizes around page boundaries and across multiple pages, RAX/RCX/RDX preservation, and a compiler-generated 32 KiB stack frame.
- Verify test LLVM IR, run O0 and `default<O2>`/llc O2, link with a custom entry and `/NODEFAULTLIB`, and require native process exit code zero.

The test harness uses additional Windows memory-protection APIs solely for fault-detection tests. The production archive has no external dependencies. The test executable supplies its own `_fltused` marker and imports ExitProcess; it does not rely on CRT initialization.

## Adoption boundary

The report is a **tested candidate**, not proof that a local rebuild is adopted: `adopted` stays false. `packageVersion` follows `Directory.Build.props` Version, shared with Kimigayo. The catalog hash/ABI must still match before use. Hashes are computed from the actual output, never placeholders.

The current catalog was frozen after assembly/unwind review and generated Kimigayo module validation described in ADOPTION.md. A different archive needs renewed review, native and generated-module verification, and a new immutable catalog version. Helper tests alone do not cover all runtime/ABI features, i128 expansion, all possible machine states, or compiler emission. They do not establish a throughput improvement.

Rebuilding is repeatable from these sources and recorded tools; adoption must compare the resulting archive hash rather than assume filename or timestamp equality. The build script does not publish or install a runtime package.

For the one-line Application and compiler commands, see [the Hello example](../../examples/Hello/README.md). `kimi build` directly runs LLVM through C# and needs no PowerShell runtime. `manual-build.ps1` remains a separately invoked alternative. `test-emission.ps1` verifies generated modules and test-only runtime fault adapters; `test-cli.ps1` exercises build, run, emit-llvm, toolchain policy and exit-code propagation.
