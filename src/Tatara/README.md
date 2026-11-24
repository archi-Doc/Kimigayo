# Tatara

`tatara.dll`: a C ABI (`tatara.h`) over the pinned LLVM and LLD static libraries, so Kimi can run LLVM's and LLD's work in
process. Setup (`src/backend/windows-x64/setup.ps1 -LlvmRoot <the extracted archive>`) builds it with `build.ps1` and installs it into
the toolchain root, `installation.json` records its SHA-256 and `check-toolchain.ps1` checks it (unit B6b3); `kimi toolchain verify` reports the
compiler's Tatara. A build of Kimi copies the
checkout's `toolchain/tatara.dll` beside `Kimi.dll`, the one file the compiler loads (unit B6b1), so build Kimi again after setup
changes it. A worktree whose `tatara.h` differs from the shared root's `tatara.dll` (a unit that changes `src/Tatara`, an older
base) gives itself a toolchain directory of its own, removing the junction alone (`cmd /c rmdir toolchain`, never `/s` or a recursive
delete, which empties the shared root), and never runs setup through the junction.

## Operations

`tatara_optimize` (`opt -passes=verify`, then `opt -S -passes=default<O2> -mtriple`), `tatara_emit` (`opt -passes=verify`, then
`llc -filetype=obj` with the target's CPU, features, relocation and code model) and `tatara_link` (`lld-link` or `ld.lld`, named
by its first argument as an LLD executable's name, through `lld::lldMain`) read the files the tools would and give equal bytes:
optimize and emit return their output (the text and the object) in the result, which the caller writes, and LLD writes its own
files, the only files Tatara writes. `tatara_inspect` reads a COFF or ELF object, or each member of an archive, and returns each
member's undefined and defined external symbols with `llvm-nm`'s letters, its form (relocatable object, COFF short import member or
linked image), its definitions in sections that admit copies (COMDAT) and its `.drectve`'s `/include:` and `/alternatename:`
references, the archive's symbol index (each entry's member), and two flags: whether a member names a default library (a COFF
`.drectve` naming `DEFAULTLIB`, an ELF `.deplibs` section) and whether the input is an archive.
Each call returns 0 (success), 1 (failed; Tatara can run again) or 2 (failed; Tatara cannot run again in this process) and a
result whose message carries the tools' text, released with `tatara_release`.

`tatara_identity` (the build identity and the LLVM version) and `tatara_abi` (the SHA-256 of `tatara.h`) are frozen: they are
the start check's bootstrap, and their shapes never change.

## Crash recovery

At load, Tatara registers the X86 target, enables `CrashRecoveryContext` and installs a fatal-error handler that records the
reason and leaves through the recovery context: its own process-wide LLVM state. Every operation runs inside
`CrashRecoveryContext::RunSafelyOnThread` on a thread that reserves the tools' 10,000,000 bytes of stack. The other state kept
across calls is LLD's parallel executor, created by the first link: its worker threads take the host's default stack and run
outside the call's recovery context and reserve. After a crash, a fatal LLVM error or LLD's `canRunAgain` false the call returns
2, and every later call returns 2 naming the first cause. `abort()`, a stack overflow beyond the reserve, and a fatal error or
crash on LLD's worker threads still end the process.

## The archive

The LLVM release archive `clang+llvm-<version>-x86_64-pc-windows-msvc.tar.xz` of the supply catalog's `llvmVersion`
(`src/backend/windows-x64/profile.json`). Its SHA-256 lives in `.github/workflows/test.yml`, the step that installs the pinned tools.

## Build

```powershell
./src/Tatara/build.ps1 -LlvmRoot <extracted archive> -OutputDirectory <directory>
```

The build needs the extracted archive (its `bin`, `include` and `lib`), Visual Studio's C++ x64 tools (found with
`vswhere -latest -prerelease`), the Windows SDK, and the DIA SDK that Visual Studio installs (`DIA SDK\lib\amd64\diaguids.lib`).
The output directory must lie outside the repository or where Git ignores it (for example under `temp/`), never in `src/Tatara`.
The script checks the archive's version against the catalog, compiles with the archive's `clang-cl`, links with its `lld-link`,
loads the DLL in a child process to check its identity, ABI and version, and prints the DLL's SHA-256, the build identity, the ABI,
the LLVM version, the MSVC toolset and the Windows SDK.

The build identity is the SHA-256 over every `.h`, `.cpp` and `.ps1` file under `src/Tatara` (the rule in `identity.ps1`, which
`build.ps1` dot-sources). The check also compares `tatara_abi` with the SHA-256 of `tatara.h`, and the DLL's export list with the
functions `tatara.h` declares.

## Link

Tatara links as LLVM's own tools link. The LLVM libraries are globbed, and the Windows libraries and options are read from
`LLVMSupport`'s `INTERFACE_LINK_LIBRARIES` in the archive's `lib/cmake/llvm/LLVMExports.cmake`; the reader accepts only the
shapes it knows and fails on any other token. `lldCOFF.lib`, `lldELF.lib` and `lldCommon.lib` are added for `tatara_link`. One departure:
`diaguids.lib` comes from the discovered Visual Studio, because `LLVMDebugInfoPDB`'s interface names a path on LLVM's build
machine.

## Allocator

The DLL's static CRT allocates with LLVM's integrated rpmalloc (in `LLVMSupport.lib`), as `clang-cl`, `opt`, `llc` and
`lld-link` do. The interface's `-INCLUDE:malloc` selects it, and the build fails if the interface does not carry it. No
allocation crosses the C ABI, so the allocator stays the DLL's own.

## LibXml2

Not added (the owner's decision). `LLVMWindowsManifest.lib` stays off the link line; `WindowsManifest.cpp` defines
`llvm::windows_manifest`'s members as LLVM's own build without LibXml2 does (`isAvailable` false, `merge` an error). Kimi's link passes no manifest option.

## Updating LLVM

1. Adopt the new version in the supply catalog (`profile.json`) and update the archive's URL and digest in CI.
2. Extract the new archive and run `build.ps1` against it; the version checks fail on any mismatch. Nothing in
   `build.ps1` lists the LLVM libraries (every `LLVM*.lib` but `LLVMWindowsManifest` and `LLVM-C` is offered to the linker) or
   the Windows options (read from the archive); a link-interface token the reader does not know stops the build.
3. If the prebuilt archive no longer fits, build LLVM from source with the archive's settings: Release; static release CRT
   (`CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded`); `LLVM_INTEGRATED_CRT_ALLOC` with rpmalloc; RTTI off; exceptions off; assertions off; zlib and zstd off; targets AArch64,
   ARM, X86, BPF, WebAssembly, RISCV and NVPTX; LLD's `lldCOFF`, `lldELF` and `lldCommon` libraries and the `llvm`, `llvm-c` and
   `lld` headers installed.
