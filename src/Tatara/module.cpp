// Reading an IR module and making a target machine as opt and llc do (internal.h).
#include "internal.h"

#include "llvm/IR/DiagnosticHandler.h"
#include "llvm/IR/DiagnosticInfo.h"
#include "llvm/IR/DiagnosticPrinter.h"
#include "llvm/IR/Verifier.h"
#include "llvm/IRReader/IRReader.h"
#include "llvm/MC/TargetRegistry.h"
#include "llvm/Support/SourceMgr.h"

using namespace llvm;

namespace {

// As LLVMContext's default printing and llc's handler: "<severity>: <text>" per diagnostic, disabled remarks dropped; an error is
// counted (DiagnosticHandler::HasErrors) instead of exiting the process.
class Diagnostics final : public DiagnosticHandler {
public:
    explicit Diagnostics(raw_ostream& message) : message(message) {}

    bool handleDiagnostics(const DiagnosticInfo& info) override
    {
        if (auto* remark = dyn_cast<DiagnosticInfoOptimizationBase>(&info); remark && !remark->isEnabled()) {
            return true;
        }

        DiagnosticPrinterRawOStream printer(message);
        message << LLVMContext::getDiagnosticMessagePrefix(info.getSeverity()) << ": ";
        info.print(printer);
        message << "\n";
        return true;
    }

private:
    raw_ostream& message;
};

} // namespace

std::unique_ptr<Module> tatara::readModule(StringRef path, LLVMContext& context, raw_ostream& message)
{
    context.setDiagnosticHandler(std::make_unique<Diagnostics>(message));
    SMDiagnostic error;
    std::unique_ptr<Module> module = parseIRFile(path, error, context);
    if (!module) {
        error.print("opt", message);
        return nullptr;
    }

    if (verifyModule(*module, &message)) {
        message << "opt: " << path << ": error: input module is broken!\n";
        return nullptr;
    }

    return module;
}

std::unique_ptr<TargetMachine> tatara::createMachine(const Triple& triple, StringRef cpu, StringRef features,
    const TargetOptions& options, std::optional<Reloc::Model> relocation, std::optional<CodeModel::Model> code,
    CodeGenOptLevel level, const char* tool, raw_ostream& message)
{
    std::string error;
    const Target* target = TargetRegistry::lookupTarget(triple, error);
    if (!target) {
        message << tool << ": " << error << "\n";
        return nullptr;
    }

    return std::unique_ptr<TargetMachine>(target->createTargetMachine(triple, cpu, features, options, relocation, code, level));
}
