// tatara_emit: opt -passes=verify, then llc -O<level> -filetype=obj -mtriple -mcpu -mattr -relocation-model -code-model (tatara.h).
#include "internal.h"

#include "llvm/ADT/StringSwitch.h"
#include "llvm/Analysis/TargetLibraryInfo.h"
#include "llvm/IR/LegacyPassManager.h"
#include "llvm/Support/FileSystem.h"
#include "llvm/Support/Path.h"
#include "llvm/Support/ToolOutputFile.h"

using namespace llvm;

namespace {

// llc's -relocation-model and -code-model values; an empty value is llc's default (none given). False: not one of llc's values.
bool parseRelocation(StringRef value, std::optional<Reloc::Model>& model)
{
    if (value.empty()) {
        return true;
    }

    model = StringSwitch<std::optional<Reloc::Model>>(value)
                .Case("static", Reloc::Static)
                .Case("pic", Reloc::PIC_)
                .Case("dynamic-no-pic", Reloc::DynamicNoPIC)
                .Case("ropi", Reloc::ROPI)
                .Case("rwpi", Reloc::RWPI)
                .Case("ropi-rwpi", Reloc::ROPI_RWPI)
                .Default(std::nullopt);
    return model.has_value();
}

bool parseCode(StringRef value, std::optional<CodeModel::Model>& model)
{
    if (value.empty()) {
        return true;
    }

    model = StringSwitch<std::optional<CodeModel::Model>>(value)
                .Case("tiny", CodeModel::Tiny)
                .Case("small", CodeModel::Small)
                .Case("kernel", CodeModel::Kernel)
                .Case("medium", CodeModel::Medium)
                .Case("large", CodeModel::Large)
                .Default(std::nullopt);
    return model.has_value();
}

// As llc's codegen::setFunctionAttributes with -mcpu and -mattr given: the CPU where a function names none; the features appended to
// a function's own.
void setFunctionAttributes(Function& function, StringRef cpu, StringRef features)
{
    if (!cpu.empty() && !function.hasFnAttribute("target-cpu")) {
        function.addFnAttr("target-cpu", cpu);
    }

    if (!features.empty()) {
        StringRef own = function.getFnAttribute("target-features").getValueAsString();
        function.addFnAttr("target-features", own.empty() ? features.str() : (own + "," + features).str());
    }
}

} // namespace

int32_t TATARA_CALL tatara_emit(const TataraTarget* target, TataraText ir, TataraText obj, int32_t optimizationLevel, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        raw_string_ostream message(outcome.message);
        std::optional<CodeGenOptLevel> level = CodeGenOpt::getLevel(optimizationLevel);
        std::optional<Reloc::Model> relocation;
        std::optional<CodeModel::Model> code;
        if (!level) {
            message << "llc: invalid optimization level: " << optimizationLevel << "\n";
            return 1;
        }

        if (!parseRelocation(tatara::view(target->relocationModel), relocation)) {
            message << "llc: invalid relocation model: " << tatara::view(target->relocationModel) << "\n";
            return 1;
        }

        if (!parseCode(tatara::view(target->codeModel), code)) {
            message << "llc: invalid code model: " << tatara::view(target->codeModel) << "\n";
            return 1;
        }

        LLVMContext context;
        std::unique_ptr<Module> module = tatara::readModule(tatara::view(ir), context, message);
        if (!module) {
            return 1;
        }

        // As llc: -mtriple overrides the module's triple and the target's data layout replaces the module's; the object's name for
        // debug information is the -o name (the file name; llc runs in the object's directory).
        Triple triple(Triple::normalize(tatara::view(target->triple)));
        StringRef cpu = tatara::view(target->cpu);
        StringRef features = tatara::view(target->features);
        TargetOptions options;
        options.ObjectFilenameForDebug = sys::path::filename(tatara::view(obj)).str();
        std::unique_ptr<TargetMachine> machine =
            tatara::createMachine(triple, cpu, features, options, relocation, code, *level, "llc", message);
        if (!machine) {
            return 1;
        }

        module->setTargetTriple(triple);
        module->setDataLayout(machine->createDataLayout());
        for (Function& function : *module) {
            setFunctionAttributes(function, cpu, features);
        }

        // As llc: a ToolOutputFile, kept only on success, so a failure after the file is opened leaves no object.
        std::error_code error;
        ToolOutputFile object(tatara::view(obj), error, sys::fs::OF_None);
        if (error) {
            message << "llc: error: " << tatara::view(obj) << ": " << error.message() << "\n";
            return 1;
        }

        legacy::PassManager passes;
        passes.add(new TargetLibraryInfoWrapperPass(TargetLibraryInfoImpl(triple)));
        if (machine->addPassesToEmitFile(passes, object.os(), nullptr, CodeGenFileType::ObjectFile, false)) {
            message << "llc: error: target does not support generation of this file type\n";
            return 1;
        }

        passes.run(*module);
        object.os().close();
        if (object.os().has_error()) {
            message << "llc: error: " << tatara::view(obj) << ": " << object.os().error().message() << "\n";
            object.os().clear_error();
            return 1;
        }

        if (context.getDiagHandlerPtr()->HasErrors) {
            return 1;
        }

        object.keep();
        return 0;
    });
}
