// Tatara: the C ABI over the pinned LLVM and LLD libraries, built into tatara.dll by build.ps1.
//
// Responsibility: expose LLVM's and LLD's in-process work to Kimi through one C ABI; in unit A1, only the identity of the build.
// Inputs: the pinned LLVM archive (its headers and static libraries) and the build identity that build.ps1 computes over the
//   .h, .cpp and .ps1 files of src/Tatara.
// Outputs: tatara.dll, exporting the functions declared here and nothing else.
// Invariants: built and linked as LLVM's own tools are (static release CRT, no RTTI, no exceptions, the archive's link interface);
//   the DLL's static CRT allocates with LLVM's integrated rpmalloc, as LLVM's own tools do, and no allocation crosses the ABI;
//   every string the library returns is static, owned by the library and never freed; the build identity and the LLVM version are
//   fixed at compile time.
// Prohibitions: no C++ type, exception or allocation crosses this ABI; no process is started; no path or version is hard-coded
//   in the build (build.ps1 reads the version from the supply catalog and finds Visual Studio with vswhere).
#ifndef TATARA_H
#define TATARA_H

#ifdef __cplusplus
extern "C" {
#endif

// Static strings owned by the library; never freed. build: the build identity (build.ps1). llvmVersion: LLVM_VERSION_STRING.
// identity: a caller-owned, non-null pointer that the call fills.
typedef struct TataraIdentity { const char* build; const char* llvmVersion; } TataraIdentity;
__declspec(dllexport) void tatara_identity(TataraIdentity* identity);

#ifdef __cplusplus
}
#endif

#endif
