// tatara_optimize: opt -passes=verify, then opt -S -passes=default<O2> -mtriple=<triple> (tatara.h).
#include "internal.h"

#include "llvm/Analysis/TargetLibraryInfo.h"
#include "llvm/IR/Verifier.h"
#include "llvm/Passes/PassBuilder.h"

using namespace llvm;

int32_t TATARA_CALL tatara_optimize(const TataraTarget* target, TataraText ir, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        raw_string_ostream message(outcome.message);
        LLVMContext context;
        std::unique_ptr<Module> module = tatara::readModule(tatara::view(ir), context, message);
        if (!module) {
            return 1;
        }

        // As opt: -mtriple overrides the module's triple; the target machine has no CPU or features and opt's default
        // --codegen-opt-level=0 (CodeGenOptLevel::None); a module without a data layout takes the target's.
        Triple triple(Triple::normalize(tatara::view(target->triple)));
        module->setTargetTriple(triple);
        std::unique_ptr<TargetMachine> machine = tatara::createMachine(
            triple, "", "", TargetOptions(), std::nullopt, std::nullopt, CodeGenOptLevel::None, "opt", message);
        if (!machine) {
            return 1;
        }

        if (module->getDataLayoutStr().empty()) {
            module->setDataLayout(machine->createDataLayout());
        }

        LoopAnalysisManager loops;
        FunctionAnalysisManager functions;
        CGSCCAnalysisManager components;
        ModuleAnalysisManager modules;
        // As opt's default<O2>: SLP vectorization on (its -print-pipeline-passes lists slp-vectorizer); the other tuning defaults.
        PipelineTuningOptions tuning;
        tuning.SLPVectorization = true;
        PassBuilder builder(machine.get(), tuning);
        TargetLibraryInfoImpl library(triple);
        functions.registerPass([&] { return TargetLibraryAnalysis(library); });
        builder.registerModuleAnalyses(modules);
        builder.registerCGSCCAnalyses(components);
        builder.registerFunctionAnalyses(functions);
        builder.registerLoopAnalyses(loops);
        builder.crossRegisterProxies(loops, functions, components, modules);
        ModulePassManager passes = builder.buildPerModuleDefaultPipeline(OptimizationLevel::O2);
        passes.addPass(VerifierPass());
        passes.run(*module, modules);
        if (context.getDiagHandlerPtr()->HasErrors) {
            return 1;
        }

        // The text as opt -S writes it on Windows (OF_TextWithCRLF: each "\n" as "\r\n"; LLVM prints no raw "\r").
        std::string text;
        raw_string_ostream printed(text);
        module->print(printed, nullptr);
        outcome.output.reserve(text.size() + StringRef(text).count('\n'));
        for (char c : text) {
            if (c == '\n') {
                outcome.output += '\r';
            }

            outcome.output += c;
        }

        return 0;
    });
}
