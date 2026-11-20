// The recovery boundary (tatara.h): the process-wide LLVM state set once at load, every operation inside
// CrashRecoveryContext::RunSafelyOnThread with the tools' stack, and the refusal after a return of 2.
#include "internal.h"

#include "llvm-c/Target.h"
#include "llvm/Support/CrashRecoveryContext.h"
#include "llvm/Support/ErrorHandling.h"
#include "llvm/Support/Format.h"
#include "llvm/Support/Process.h"

using namespace llvm;

namespace {

// The stack opt, llc, lld-link, llvm-nm and llvm-readobj reserve (their PE headers); dotnet reserves 1,572,864 bytes.
constexpr unsigned ToolStack = 10'000'000;

bool fatalRecorded = false;
std::string fatalReason;
bool refused = false;
std::string firstCause;

// Records the reason, then leaves through the current recovery context (a handler that returned would exit the process).
void recordFatal(void*, const char* reason, bool)
{
    if (!fatalRecorded) {
        fatalRecorded = true;
        fatalReason = reason;
    }
    sys::Process::Exit(1);
}

// Tatara's only process-wide LLVM state, set once when the DLL loads.
struct Load {
    Load()
    {
        LLVMInitializeX86TargetInfo();
        LLVMInitializeX86Target();
        LLVMInitializeX86TargetMC();
        LLVMInitializeX86AsmPrinter();
        LLVMInitializeX86AsmParser();
        CrashRecoveryContext::Enable();
        install_fatal_error_handler(recordFatal, nullptr);
    }
} load;

} // namespace

int32_t tatara::run(TataraResult** result, function_ref<int32_t(TataraResult&)> operation)
{
    TataraResult* outcome = new TataraResult();
    *result = outcome;
    if (refused) {
        outcome->message = "Tatara cannot run again in this process; the first cause:\n" + firstCause;
        return 2;
    }

    int32_t code = 2;
    CrashRecoveryContext context;
    bool completed = context.RunSafelyOnThread([&] { code = operation(*outcome); }, ToolStack);
    if (completed && !fatalRecorded && code != 2) {
        return code;
    }

    raw_string_ostream message(outcome->message);
    if (fatalRecorded) {
        message << "LLVM ERROR: " << fatalReason << "\n";
    } else if (!completed) {
        message << "Tatara: the operation crashed (code " << format_hex(static_cast<uint32_t>(context.RetCode), 10) << ")\n";
    }

    refused = true;
    firstCause = outcome->message;

    // The operation's frame never returned: the output it opened is closed here (its descriptor recorded until closeOutput) and
    // removed, as llc and opt leave none when they crash.
    if (outcome->outputDescriptor >= 0) {
        sys::Process::SafelyCloseFileDescriptor(outcome->outputDescriptor);
    }

    if (!outcome->output.empty()) {
        tatara::removeOutput(outcome->output, message);
    }

    return 2;
}
