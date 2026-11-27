// What Tatara's sources share behind the C ABI: the result object, the recovery boundary (recovery.cpp), reading an IR module
// as the tools read it and making a target machine (module.cpp). Nothing here crosses the ABI.
#ifndef TATARA_INTERNAL_H
#define TATARA_INTERNAL_H

#include "tatara.h"

#include "llvm/ADT/STLFunctionalExtras.h"
#include "llvm/ADT/StringRef.h"
#include "llvm/IR/LLVMContext.h"
#include "llvm/IR/Module.h"
#include "llvm/Support/raw_ostream.h"
#include "llvm/Target/TargetMachine.h"
#include "llvm/TargetParser/Triple.h"
#include <memory>
#include <optional>
#include <string>

// One call's outcome; its texts and flags are what tatara_text and tatara_flag return (tatara.h's kinds). output: optimize's or emit's
// output (kind 3), set only on success.
struct TataraResult {
    std::string message;
    std::string undefinedSymbols;
    std::string definedSymbols;
    std::string forms, comdats, directives, index;
    int32_t hasDefaultLibrary = 0;
    int32_t isArchive = 0;
    std::string output;
};

namespace tatara {

inline llvm::StringRef view(TataraText text) { return llvm::StringRef(text.bytes, text.length); }

// Sets *result and runs the operation inside crash recovery on a thread reserving the tools' stack. The operation returns 0, 1 or 2
// (2: LLD cannot run again); a crash or a fatal error makes the call 2, and after a 2 every call returns 2 with the first cause.
int32_t run(TataraResult** result, llvm::function_ref<int32_t(TataraResult&)> operation);

// The IR file parsed and verified as opt reads it (messages as opt prints them); the context's diagnostics written to message.
std::unique_ptr<llvm::Module> readModule(llvm::StringRef path, llvm::LLVMContext& context, llvm::raw_ostream& message);

// The triple's target machine, or null with the tool's message.
std::unique_ptr<llvm::TargetMachine> createMachine(const llvm::Triple& triple, llvm::StringRef cpu, llvm::StringRef features,
    const llvm::TargetOptions& options, std::optional<llvm::Reloc::Model> relocation, std::optional<llvm::CodeModel::Model> code,
    llvm::CodeGenOptLevel level, const char* tool, llvm::raw_ostream& message);

} // namespace tatara

#endif
