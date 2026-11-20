// tatara_inspect: the COFF object's symbols as llvm-nm --undefined-only and --defined-only --extern-only list them (--format=posix's
// letter and name), and llvm-readobj --unwind --coff-directives' two facts (tatara.h).
#include "internal.h"

#include "llvm/ADT/StringSwitch.h"
#include "llvm/Object/COFF.h"
#include "llvm/Object/ObjectFile.h"
#include "llvm/Support/Win64EH.h"
#include <algorithm>
#include <cctype>
#include <utility>
#include <vector>

using namespace llvm;
using namespace llvm::object;

namespace {

using Symbols = std::vector<std::pair<std::string, char>>;

// llvm-nm's getSymbolNMTypeChar for a COFF symbol that is neither weak, undefined, common nor absolute.
char sectionLetter(const COFFObjectFile& file, const SymbolRef& symbol, StringRef name)
{
    char letter = StringSwitch<char>(name).StartsWith(".debug", 'N').StartsWith(".sxdata", 'N').Default('?');
    if (letter != '?') {
        return letter;
    }

    COFFSymbolRef coff = file.getCOFFSymbol(symbol);
    uint32_t characteristics = 0;
    if (!COFF::isReservedSectionNumber(coff.getSectionNumber())) {
        Expected<section_iterator> section = symbol.getSection();
        if (!section) {
            consumeError(section.takeError());
            return '?';
        }

        const coff_section* header = file.getCOFFSection(**section);
        characteristics = header->Characteristics;
        if (Expected<StringRef> sectionName = file.getSectionName(header)) {
            if (sectionName->starts_with(".idata")) {
                return 'i';
            }
        } else {
            consumeError(sectionName.takeError());
        }
    }

    if (coff.getSectionNumber() == COFF::IMAGE_SYM_DEBUG) {
        return 'n';
    }

    if (characteristics & COFF::IMAGE_SCN_CNT_CODE) {
        return 't';
    }

    if (characteristics & COFF::IMAGE_SCN_CNT_INITIALIZED_DATA) {
        return (characteristics & COFF::IMAGE_SCN_MEM_WRITE) ? 'd' : 'r';
    }

    if (characteristics & COFF::IMAGE_SCN_CNT_UNINITIALIZED_DATA) {
        return 'b';
    }

    if (characteristics & COFF::IMAGE_SCN_LNK_INFO) {
        return 'i';
    }

    return coff.isSectionDefinition() ? 's' : '?';
}

// llvm-nm's getNMTypeChar for a COFF symbol: weak w or W, undefined U, common C, absolute a, else the section's letter; upper case
// when global.
char letter(const COFFObjectFile& file, const SymbolRef& symbol, uint32_t flags, StringRef name)
{
    if (flags & SymbolRef::SF_Weak) {
        return (flags & SymbolRef::SF_Undefined) ? 'w' : 'W';
    }

    if (flags & SymbolRef::SF_Undefined) {
        return 'U';
    }

    if (flags & SymbolRef::SF_Common) {
        return 'C';
    }

    char result = (flags & SymbolRef::SF_Absolute) ? 'a' : sectionLetter(file, symbol, name);
    return (flags & SymbolRef::SF_Global) ? static_cast<char>(std::toupper(static_cast<unsigned char>(result))) : result;
}

// llvm-nm's order (by name) and posix form, as "<letter> <name>\n".
void write(Symbols& symbols, std::string& text)
{
    std::stable_sort(symbols.begin(), symbols.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
    for (const auto& [name, kind] : symbols) {
        text += kind;
        text += ' ';
        text += name;
        text += '\n';
    }
}

} // namespace

int32_t TATARA_CALL tatara_inspect(TataraText obj, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        raw_string_ostream message(outcome.message);
        StringRef path = tatara::view(obj);
        Expected<OwningBinary<ObjectFile>> binary = ObjectFile::createObjectFile(path);
        if (!binary) {
            message << "llvm-nm: error: " << path << ": " << toString(binary.takeError()) << "\n";
            return 1;
        }

        const auto* file = dyn_cast<COFFObjectFile>(binary->getBinary());
        if (!file) {
            message << "llvm-nm: error: " << path << ": not a COFF object\n";
            return 1;
        }

        Symbols undefined;
        Symbols defined;
        for (const SymbolRef& symbol : file->symbols()) {
            Expected<uint32_t> flags = symbol.getFlags();
            if (!flags) {
                message << "llvm-nm: error: " << path << ": " << toString(flags.takeError()) << "\n";
                return 1;
            }

            if (*flags & SymbolRef::SF_FormatSpecific) {
                continue;
            }

            Expected<StringRef> name = symbol.getName();
            if (!name) {
                message << "llvm-nm: error: " << path << ": " << toString(name.takeError()) << "\n";
                return 1;
            }

            if (*flags & SymbolRef::SF_Undefined) {
                undefined.emplace_back(name->str(), letter(*file, symbol, *flags, *name));
            } else if (*flags & SymbolRef::SF_Global) {
                defined.emplace_back(name->str(), letter(*file, symbol, *flags, *name));
            }
        }

        write(undefined, outcome.undefinedSymbols);
        write(defined, outcome.definedSymbols);

        // llvm-readobj prints a RuntimeFunction per entry of a .pdata section and every .drectve section's text.
        for (const SectionRef& section : file->sections()) {
            Expected<StringRef> name = section.getName();
            if (!name) {
                message << "llvm-readobj: error: " << path << ": " << toString(name.takeError()) << "\n";
                return 1;
            }

            if ((*name == ".pdata" || name->starts_with(".pdata$")) && section.getSize() >= sizeof(Win64EH::RuntimeFunction)) {
                outcome.hasUnwind = 1;
            }

            if (*name == ".drectve") {
                Expected<StringRef> contents = section.getContents();
                if (!contents) {
                    message << "llvm-readobj: error: " << path << ": " << toString(contents.takeError()) << "\n";
                    return 1;
                }

                if (contents->contains_insensitive("DEFAULTLIB")) {
                    outcome.hasDefaultLibrary = 1;
                }
            }
        }

        return 0;
    });
}
