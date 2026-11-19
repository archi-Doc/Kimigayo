# Tatara

`tatara.dll`: a C ABI (`tatara.h`) over the pinned LLVM and LLD static libraries, so Kimi can run LLVM's and LLD's work in
process. In this stage it exports only `tatara_identity`; setup, the installation record and CI take the DLL over in unit A4.

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
loads the DLL in a child process to check its identity and version, and prints the DLL's SHA-256, the build identity, the LLVM
version, the MSVC toolset and the Windows SDK.

The build identity is the SHA-256 over every `.h`, `.cpp` and `.ps1` file under `src/Tatara` (rule in `build.ps1`).

## LibXml2

Not added (the owner's decision). `LLVMWindowsManifest.lib` stays off the link line; with `lldCOFF` (unit A2) Tatara defines `llvm::windows_manifest`'s
members as LLVM's own build without LibXml2 does (`isAvailable` false, `merge` an error). Kimi's link passes no manifest option.

## Updating LLVM

1. Adopt the new version in the supply catalog (`profile.json`) and update the archive's URL and digest in CI.
2. Extract the new archive and run `build.ps1` against it; the version checks fail on any mismatch, and no library list changes
   (every `LLVM*.lib` but `LLVMWindowsManifest` and `LLVM-C` is offered to the linker).
3. If the prebuilt archive no longer fits, build LLVM from source with the archive's settings: Release; static release CRT
   (`CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded`); RTTI off; exceptions off; assertions off; zlib and zstd off; targets AArch64,
   ARM, X86, BPF, WebAssembly, RISCV and NVPTX; LLD's `lldCOFF`, `lldELF` and `lldCommon` libraries and the `llvm`, `llvm-c` and
   `lld` headers installed.
