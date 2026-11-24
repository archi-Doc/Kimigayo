// Tatara: the C ABI over the pinned LLVM and LLD libraries, built into tatara.dll by build.ps1.
//
// Responsibility: run in process what Kimi's native build runs as LLVM's and LLD's tools: optimize (opt), emit (llc), inspect
//   (llvm-nm and llvm-readobj, for COFF and ELF objects and archives) and link (lld-link or ld.lld), with bytes and text equal to the
//   tools'; and report the build's identity.
// Inputs: the pinned LLVM archive (its headers and static libraries); at run time, file paths and the target values of each call.
// Outputs: tatara.dll, exporting the functions declared here and nothing else; each call's result (optimize's and emit's output
//   among its texts); the files LLD writes, which are the only files Tatara writes.
// Invariants: built and linked as LLVM's own tools are (static release CRT, no RTTI, no exceptions, the archive's link interface);
//   the DLL's static CRT allocates with LLVM's integrated rpmalloc and no allocation crosses the ABI. tatara_identity's and
//   tatara_abi's strings are static; a result's texts are owned by the result until tatara_release. Tatara's process-wide LLVM
//   state is set once at load: the X86 target's registration, CrashRecoveryContext::Enable() and a fatal-error handler that records
//   the reason and leaves through the recovery context. Every operation runs inside CrashRecoveryContext::RunSafelyOnThread on a
//   thread reserving the tools' 10,000,000 bytes of stack, with its own LLVMContext; no target machine is kept between calls. Kept
//   besides: LLD's parallel executor, created by the first link and kept for the process (each link sets its strategy from its
//   arguments); its worker threads take the host's default stack and run outside the call's recovery context. After a return of 2
//   every later call returns 2 with the first cause. What still ends the process: abort() (LLVM's bad-allocation path, for
//   example), a stack overflow beyond the reserve, and a fatal error or crash on LLD's worker threads.
// Prohibitions: no C++ type, exception or allocation crosses this ABI; no process is started; no cl::opt is set; no path or version
//   is hard-coded in the build (build.ps1 reads the version from the supply catalog and finds Visual Studio with vswhere).
//   Callers serialize calls.
#ifndef TATARA_H
#define TATARA_H

#include <stddef.h>
#include <stdint.h>

#define TATARA_CALL __cdecl // explicit on every export: /Gv does not override it

#ifdef __cplusplus
extern "C" {
#endif

// Every struct that crosses holds only pointer-sized members (no packing dependence).
// UTF-8, not NUL-terminated; an argument's text is borrowed for the call, a result's is owned by the result. Emit's output, an
// object's bytes, is not text.
typedef struct TataraText { const char* bytes; size_t length; } TataraText;
// The values llc takes as -mtriple, -mcpu, -mattr, -relocation-model and -code-model (an empty value: the tool's default).
typedef struct TataraTarget { TataraText triple, cpu, features, relocationModel, codeModel; } TataraTarget;
// Opaque; owned by Tatara until tatara_release.
typedef struct TataraResult TataraResult;

// Frozen from A1a and from A2 (the start check's bootstrap; their shapes never change again).
// identity: a caller-owned, non-null pointer that the call fills. build: the build identity (identity.ps1). llvmVersion:
// LLVM_VERSION_STRING. Both static, never freed.
typedef struct TataraIdentity { const char* build; const char* llvmVersion; } TataraIdentity;
__declspec(dllexport) void TATARA_CALL tatara_identity(TataraIdentity* identity);
// A static string: the SHA-256 (lower-case hex) of this file's bytes, passed by build.ps1.
__declspec(dllexport) const char* TATARA_CALL tatara_abi(void);

// ir and inspect's obj are paths of files Tatara reads, as the tools do. Emit's obj only names the object (llc's -o: the name debug
// information gives it) and is never opened. Optimize and emit return their output in the result (tatara_text kind 3), which the
// caller writes; only LLD writes files.
// 0: success. 1: the request failed (the message says why); Tatara can run again. 2: the request failed and Tatara cannot run again
// in this process (a fatal LLVM or LLD error, a crash caught by recovery, or LLD's canRunAgain false); every later call returns 2
// with the first cause. *result is set for every return code and is released by the caller. Never throws.
// optimize: opt -passes=verify, then opt -S -passes=default<O2> -mtriple=<triple>; its output the text opt writes (CRLF line ends).
__declspec(dllexport) int32_t TATARA_CALL tatara_optimize(const TataraTarget* target, TataraText ir, TataraResult** result);
// emit: opt -passes=verify, then llc -O<optimizationLevel> -filetype=obj with the target's values (optimizationLevel 0 to 3); its
// output the object's bytes.
__declspec(dllexport) int32_t TATARA_CALL tatara_emit(const TataraTarget* target, TataraText ir, TataraText obj, int32_t optimizationLevel, TataraResult** result);
// inspect: the COFF or ELF object's, or each member's of an archive (llvm-nm's reading of archives and of COFF short import members),
// symbols as llvm-nm lists them, the members' facts below and the flags; a thin archive, another format, a short import member LLD
// calls broken (shorter than its header and SizeOfData, or no NUL in its name) or a listed name holding a line break is a failure (1).
__declspec(dllexport) int32_t TATARA_CALL tatara_inspect(TataraText obj, TataraResult** result);
// link: LLD with these arguments as its argv: arguments[0] names the flavor, "lld-link" (COFF) or "ld.lld" (ELF), as an LLD
// executable's name does; another flavor is a failure (1).
__declspec(dllexport) int32_t TATARA_CALL tatara_link(const TataraText* arguments, size_t count, TataraResult** result);
// kind 0: the message (every operation; the tools' standard output and error, empty when they print nothing). Inspect, per member
// in archive order (an object is one member), kinds 1, 2, 5 and 6 one block per member, each ended by an empty line: 1 the undefined
// symbols, 2 the defined external symbols, one "<llvm-nm's letter> <name>\n" per symbol in llvm-nm's order; 4 the member's form, one
// letter and "\n" per member: o a relocatable object, i a COFF short import member, s a linked image; 5 the names of kind 2 whose
// section admits copies, "<name>\n" each in kind 2's order (COFF: an IMAGE_SCN_LNK_COMDAT section selecting any, same size, exact match
// or largest; ELF: a section of an SHT_GROUP with GRP_COMDAT); 6 a COFF member's .drectve references in their order, split as
// lld-link splits it: "I <name>\n" per /include: and "A <from>=<to>\n" per /alternatename: (either prefix, any case; one missing a
// name is skipped; every other option is LLD's; ELF: none). 7 the archive's symbol index, "<member> <name>\n" per entry in its order,
// <member> the position (from 0) of the member it names; empty for an object or an archive without an index. Optimize and emit: 3
// the output, set by a successful call only. Another kind, or a kind the operation does not set: an empty text.
__declspec(dllexport) TataraText TATARA_CALL tatara_text(const TataraResult* result, int32_t kind);
// Inspect, kind 1 (a library the object, or an archive's member, names for the link): COFF, 1 when a .drectve section contains
// DEFAULTLIB in any case; ELF, 1 when an SHT_LLVM_DEPENDENT_LIBRARIES (.deplibs) section exists. Kind 2: 1 for an archive. Otherwise
// 0. Another kind: -1 (kind 0 too: unused since PLAN G1271's removal, never reused).
__declspec(dllexport) int32_t TATARA_CALL tatara_flag(const TataraResult* result, int32_t kind);
__declspec(dllexport) void TATARA_CALL tatara_release(TataraResult* result);

#ifdef __cplusplus
}
#endif

#endif
