// tatara_link: lld-link or ld.lld with the given arguments, through lld::lldMain and LLD's COFF and ELF drivers (tatara.h).
#include "internal.h"

#include "lld/Common/Driver.h"
#include <vector>

LLD_HAS_DRIVER(coff)
LLD_HAS_DRIVER(elf)

int32_t TATARA_CALL tatara_link(const TataraText* arguments, size_t count, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        // arguments[0] names the flavor and LLD's log name, as an LLD executable's argv[0] does: one of the two drivers below.
        llvm::raw_string_ostream message(outcome.message);
        llvm::StringRef flavor = count == 0 ? llvm::StringRef() : tatara::view(arguments[0]);
        if (flavor != "lld-link" && flavor != "ld.lld") {
            message << "lld: error: arguments[0] names the flavor, lld-link or ld.lld, not '" << flavor << "'\n";
            return 1;
        }

        std::vector<std::string> texts;
        texts.reserve(count);
        for (size_t i = 0; i < count; i++) {
            texts.push_back(tatara::view(arguments[i]).str());
        }

        std::vector<const char*> argv;
        argv.reserve(count);
        for (const std::string& text : texts) {
            argv.push_back(text.c_str());
        }

        lld::Result linked = lld::lldMain(argv, message, message, {{lld::WinLink, &lld::coff::link}, {lld::Gnu, &lld::elf::link}});
        if (!linked.canRunAgain) {
            return 2;
        }

        return linked.retCode == 0 ? 0 : 1;
    });
}
