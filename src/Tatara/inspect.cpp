// tatara_inspect: a COFF or ELF object's symbols as llvm-nm --undefined-only and --defined-only --extern-only list them (--format=posix's
// letter and name), and the format's two facts (tatara.h): COFF, llvm-readobj --unwind --coff-directives'; ELF, its section table's.
#include "internal.h"

#include "llvm/ADT/StringSwitch.h"
#include "llvm/BinaryFormat/ELF.h"
#include "llvm/Object/COFF.h"
#include "llvm/Object/ELFObjectFile.h"
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

// llvm-nm's getSymbolNMTypeChar for an ELF symbol in a section (getNMSectionTagAndName found it) that is neither weak, undefined,
// common nor absolute.
char sectionLetter(const ELFSymbolRef& symbol, const ELFSectionRef& section)
{
    uint8_t binding = symbol.getBinding();
    if (binding == ELF::STB_GNU_UNIQUE) {
        return 'u';
    }

    if (binding != ELF::STB_GLOBAL && binding != ELF::STB_LOCAL) {
        return '?';
    }

    uint64_t flags = section.getFlags();
    if (flags & ELF::SHF_EXECINSTR) {
        return 't';
    }

    if (section.getType() == ELF::SHT_NOBITS) {
        return 'b';
    }

    if (flags & ELF::SHF_ALLOC) {
        return (flags & ELF::SHF_WRITE) ? 'd' : 'r';
    }

    Expected<StringRef> name = section.getName();
    if (!name) {
        consumeError(name.takeError());
        return '?';
    }

    if (name->starts_with(".debug")) {
        return 'N';
    }

    return (flags & ELF::SHF_WRITE) ? '?' : 'n';
}

// llvm-nm's getNMSectionTagAndName for a COFF or ELF symbol: an ELF symbol in no readable section ?; undefined U, a weak one v (an ELF
// object symbol) or w; an ELF IFUNC i; weak V or W; common C; absolute a; else the format's section letter, upper case when global
// but ELF's unique u.
char letter(const ObjectFile& file, const SymbolRef& symbol, uint32_t flags, StringRef name)
{
    const auto* elf = dyn_cast<ELFObjectFileBase>(&file);
    std::optional<ELFSectionRef> section;
    if (elf && !(flags & (SymbolRef::SF_Absolute | SymbolRef::SF_Common | SymbolRef::SF_Undefined))) {
        Expected<section_iterator> found = symbol.getSection();
        if (!found) {
            consumeError(found.takeError());
            return '?';
        }

        if (*found == file.section_end()) {
            return '?';
        }

        if (Expected<StringRef> sectionName = (*found)->getName(); !sectionName) {
            consumeError(sectionName.takeError());
            return '?';
        }

        section = ELFSectionRef(**found);
    }

    bool object = elf && ELFSymbolRef(symbol).getELFType() == ELF::STT_OBJECT;
    if (flags & SymbolRef::SF_Undefined) {
        return !(flags & SymbolRef::SF_Weak) ? 'U' : object ? 'v' : 'w';
    }

    if (elf && ELFSymbolRef(symbol).getELFType() == ELF::STT_GNU_IFUNC) {
        return 'i';
    }

    if (flags & SymbolRef::SF_Weak) {
        return object ? 'V' : 'W';
    }

    if (flags & SymbolRef::SF_Common) {
        return 'C';
    }

    char result = (flags & SymbolRef::SF_Absolute) ? 'a'
        : section ? sectionLetter(ELFSymbolRef(symbol), *section)
                  : sectionLetter(*cast<COFFObjectFile>(&file), symbol, name);
    if (elf && ELFSymbolRef(symbol).getBinding() == ELF::STB_GNU_UNIQUE) {
        return result;
    }

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

// COFF: llvm-readobj prints a RuntimeFunction per entry of a .pdata section and every .drectve section's text. ELF: a non-empty
// .eh_frame section, and an SHT_LLVM_DEPENDENT_LIBRARIES section (.deplibs). False: a section the reader could not read.
bool readFlags(const ObjectFile& file, StringRef path, TataraResult& outcome, raw_ostream& message)
{
    bool elf = isa<ELFObjectFileBase>(&file);
    for (const SectionRef& section : file.sections()) {
        Expected<StringRef> name = section.getName();
        if (!name) {
            message << "llvm-readobj: error: " << path << ": " << toString(name.takeError()) << "\n";
            return false;
        }

        if (elf) {
            outcome.hasUnwind |= *name == ".eh_frame" && section.getSize() != 0;
            outcome.hasDefaultLibrary |= ELFSectionRef(section).getType() == ELF::SHT_LLVM_DEPENDENT_LIBRARIES;
            continue;
        }

        if ((*name == ".pdata" || name->starts_with(".pdata$")) && section.getSize() >= sizeof(Win64EH::RuntimeFunction)) {
            outcome.hasUnwind = 1;
        }

        if (*name == ".drectve") {
            Expected<StringRef> contents = section.getContents();
            if (!contents) {
                message << "llvm-readobj: error: " << path << ": " << toString(contents.takeError()) << "\n";
                return false;
            }

            if (contents->contains_insensitive("DEFAULTLIB")) {
                outcome.hasDefaultLibrary = 1;
            }
        }
    }

    return true;
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

        const ObjectFile* file = binary->getBinary();
        if (!isa<COFFObjectFile>(file) && !isa<ELFObjectFileBase>(file)) {
            message << "llvm-nm: error: " << path << ": not a COFF or ELF object\n";
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
        return readFlags(*file, path, outcome, message) ? 0 : 1;
    });
}
