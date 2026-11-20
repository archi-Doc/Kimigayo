// tatara_link: lld-link with the given arguments, through lld::lldMain and LLD's COFF driver (tatara.h).
#include "internal.h"

#include "lld/Common/Driver.h"
#include <vector>

LLD_HAS_DRIVER(coff)

int32_t TATARA_CALL tatara_link(const TataraText* arguments, size_t count, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        // argv[0] names the flavor and LLD's log name, as the lld-link executable's does.
        std::vector<std::string> texts;
        texts.reserve(count);
        for (size_t i = 0; i < count; i++) {
            texts.push_back(tatara::view(arguments[i]).str());
        }

        std::vector<const char*> argv{"lld-link"};
        for (const std::string& text : texts) {
            argv.push_back(text.c_str());
        }

        llvm::raw_string_ostream message(outcome.message);
        lld::Result linked = lld::lldMain(argv, message, message, {{lld::WinLink, &lld::coff::link}});
        if (!linked.canRunAgain) {
            return 2;
        }

        return linked.retCode == 0 ? 0 : 1;
    });
}
