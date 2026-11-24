// tatara_inspect: a COFF or ELF object's, or each member's of an archive, symbols as llvm-nm --undefined-only and --defined-only
// --extern-only list them (--format=posix's letter and name; llvm-nm's reading of archives and of COFF short import members), each
// member's form, COMDAT definitions and directive references, the archive's symbol index, and the default-library fact (tatara.h):
// COFF, llvm-readobj --coff-directives'; ELF, its section table's.
#include "internal.h"

#include "llvm/ADT/DenseMap.h"
#include "llvm/ADT/DenseSet.h"
#include "llvm/ADT/SmallVector.h"
#include "llvm/ADT/StringSwitch.h"
#include "llvm/BinaryFormat/ELF.h"
#include "llvm/Object/Archive.h"
#include "llvm/Object/COFF.h"
#include "llvm/Object/COFFImportFile.h"
#include "llvm/Object/ELFObjectFile.h"
#include "llvm/Object/IRObjectFile.h"
#include "llvm/Object/ObjectFile.h"
#include "llvm/Support/Allocator.h"
#include "llvm/Support/CommandLine.h"
#include "llvm/Support/Endian.h"
#include "llvm/Support/StringSaver.h"
#include <algorithm>
#include <cctype>
#include <optional>
#include <string>
#include <utility>
#include <vector>

using namespace llvm;
using namespace llvm::object;

namespace {

// A listed symbol: its name, llvm-nm's letter and whether its section admits copies (a defined symbol's, tatara.h's text kind 5).
struct Listed {
    std::string name;
    char letter;
    bool copy;
};

using Symbols = std::vector<Listed>;

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

// llvm-nm's getNMTypeChar for a member that is not an object file. A COFF short import member: its import type's letter (code t, data
// d, const r), upper case, since every symbol it defines is global. A bitcode member (IRObjectFile): undefined U, a weak one w; weak W;
// common C; else t for code and d for data, upper case when global.
char memberLetter(const SymbolicFile& file, uint32_t flags)
{
    if (const auto* import = dyn_cast<COFFImportFile>(&file)) {
        int type = import->getCOFFImportHeader()->getType();
        return type == COFF::IMPORT_CODE ? 'T' : type == COFF::IMPORT_DATA ? 'D' : type == COFF::IMPORT_CONST ? 'R' : '?';
    }

    if (flags & SymbolRef::SF_Undefined) {
        return (flags & SymbolRef::SF_Weak) ? 'w' : 'U';
    }

    if (flags & SymbolRef::SF_Weak) {
        return 'W';
    }

    if (flags & SymbolRef::SF_Common) {
        return 'C';
    }

    char result = (flags & SymbolRef::SF_Executable) ? 't' : 'd';
    return (flags & SymbolRef::SF_Global) ? static_cast<char>(std::toupper(static_cast<unsigned char>(result))) : result;
}

// A COFF COMDAT selection whose copies lld-link keeps one of: any, same size, exact match or largest (no duplicates is an ordinary
// definition, an associative section defines no symbol of its own, newest LLD refuses).
bool admitsCopies(uint8_t selection)
{
    return selection == COFF::IMAGE_COMDAT_SELECT_ANY || selection == COFF::IMAGE_COMDAT_SELECT_SAME_SIZE
        || selection == COFF::IMAGE_COMDAT_SELECT_EXACT_MATCH || selection == COFF::IMAGE_COMDAT_SELECT_LARGEST;
}

// The indexes of an object's sections whose definitions admit copies (tatara.h's text kind 5). COFF: an IMAGE_SCN_LNK_COMDAT section
// whose section symbol's definition admitsCopies. ELF: a section an SHT_GROUP with GRP_COMDAT lists (ld.lld keeps one group per
// signature). False with the tool's message when a section cannot be read.
bool copySections(const ObjectFile& file, StringRef name, DenseSet<uint64_t>& indexes, raw_ostream& message)
{
    if (const auto* coff = dyn_cast<COFFObjectFile>(&file)) {
        for (const SymbolRef& symbol : coff->symbols()) {
            const coff_aux_section_definition* definition = coff->getCOFFSymbol(symbol).getSectionDefinition();
            if (!definition || !admitsCopies(definition->Selection)) {
                continue;
            }

            Expected<section_iterator> section = symbol.getSection();
            if (!section) {
                message << "llvm-nm: error: " << name << ": " << toString(section.takeError()) << "\n";
                return false;
            }

            if (*section != coff->section_end() && (coff->getCOFFSection(**section)->Characteristics & COFF::IMAGE_SCN_LNK_COMDAT)) {
                indexes.insert((*section)->getIndex());
            }
        }

        return true;
    }

    // An SHT_GROUP's words, in the object's byte order: the group's flags, then its sections' indexes.
    endianness order = file.isLittleEndian() ? endianness::little : endianness::big;
    for (const SectionRef& section : file.sections()) {
        if (ELFSectionRef(section).getType() != ELF::SHT_GROUP) {
            continue;
        }

        Expected<StringRef> contents = section.getContents();
        if (!contents) {
            message << "llvm-readobj: error: " << name << ": " << toString(contents.takeError()) << "\n";
            return false;
        }

        size_t words = contents->size() / 4;
        if (words != 0 && (support::endian::read32(contents->data(), order) & ELF::GRP_COMDAT)) {
            for (size_t i = 1; i < words; ++i) {
                indexes.insert(support::endian::read32(contents->data() + 4 * i, order));
            }
        }
    }

    return true;
}

// Whether a defined symbol of an object lies in one of its copy sections (copySections).
Expected<bool> isCopy(const ObjectFile& file, const SymbolRef& symbol, const DenseSet<uint64_t>& copies)
{
    if (copies.empty()) {
        return false;
    }

    Expected<section_iterator> section = symbol.getSection();
    if (!section) {
        return section.takeError();
    }

    return *section != file.section_end() && copies.contains((*section)->getIndex());
}

// The value of a .drectve token naming the option (its "/" or "-" form, any case, as LLD's directive parser and option table match
// it), or nothing.
std::optional<StringRef> optionValue(StringRef token, StringRef option)
{
    bool named = token.size() > option.size() && (token[0] == '/' || token[0] == '-')
        && token.substr(1, option.size()).equals_insensitive(option);
    return named ? std::optional<StringRef>(token.substr(option.size() + 1)) : std::nullopt;
}

// Whether a name can be listed: the texts are lines and an empty line ends a member's block (tatara.h; kind 5's lines are bare
// names), so an empty name or one holding a line break fails the input (1) with llvm-nm's message naming the member (C-533 N-1, C1),
// never a misaligned block.
bool listable(StringRef text, StringRef name, raw_ostream& message)
{
    if (!text.empty() && text.find_first_of("\r\n") == StringRef::npos) {
        return true;
    }

    message << "llvm-nm: error: " << name << (text.empty() ? ": a name is empty" : ": a name holds a line break: ");
    message.write_escaped(text);
    message << "\n";
    return false;
}

// A .drectve's references (tatara.h's text kind 6), its text split as lld-link splits it (the Windows command-line tokenizer): "I
// <name>" per /include: and "A <from>=<to>" per /alternatename:, in its order. A reference missing a name is skipped (LLD reports it
// when it links the member); every other option is LLD's. LLD's own parser (lld::coff) is not in the archive's headers. False when a
// name cannot be listed.
bool readDirectives(StringRef text, StringRef name, std::string& references, raw_ostream& message)
{
    BumpPtrAllocator allocator;
    StringSaver saver(allocator);
    SmallVector<StringRef, 16> tokens;
    cl::TokenizeWindowsCommandLineNoCopy(text, saver, tokens);
    for (StringRef token : tokens) {
        if (std::optional<StringRef> include = optionValue(token, "include:"); include && !include->empty()) {
            references += "I " + include->str() + "\n";
            if (!listable(*include, name, message)) {
                return false;
            }

            continue;
        }

        auto [from, to] = optionValue(token, "alternatename:").value_or("").split('=');
        if (!from.empty() && !to.empty()) {
            references += "A " + from.str() + "=" + to.str() + "\n";
            if (!listable(from, name, message) || !listable(to, name, message)) {
                return false;
            }
        }
    }

    return true;
}

// llvm-nm's order (by name) and posix form, as "<letter> <name>\n", then the empty line that ends the member's block.
void write(Symbols& symbols, std::string& text)
{
    std::stable_sort(symbols.begin(), symbols.end(), [](const Listed& a, const Listed& b) { return a.name < b.name; });
    for (const Listed& symbol : symbols) {
        text += symbol.letter;
        text += ' ';
        text += symbol.name;
        text += '\n';
    }

    text += '\n';
}

// A member's symbols into its blocks of kinds 1, 2 and 5 (the copies among kind 2's, in its order). False with llvm-nm's message when a
// symbol cannot be read.
bool readSymbols(const SymbolicFile& file, const DenseSet<uint64_t>& copies, StringRef name, TataraResult& outcome, raw_ostream& message)
{
    const auto* object = dyn_cast<ObjectFile>(&file);
    Symbols undefined;
    Symbols defined;
    for (const BasicSymbolRef& symbol : file.symbols()) {
        Expected<uint32_t> flags = symbol.getFlags();
        if (!flags) {
            message << "llvm-nm: error: " << name << ": " << toString(flags.takeError()) << "\n";
            return false;
        }

        if (*flags & SymbolRef::SF_FormatSpecific) {
            continue;
        }

        std::string symbolName;
        raw_string_ostream printed(symbolName);
        if (Error error = symbol.printName(printed)) {
            message << "llvm-nm: error: " << name << ": " << toString(std::move(error)) << "\n";
            return false;
        }

        bool reference = *flags & SymbolRef::SF_Undefined;
        if (!reference && !(*flags & SymbolRef::SF_Global)) {
            continue;
        }

        if (!listable(symbolName, name, message)) {
            return false;
        }

        char kind = object ? letter(*object, SymbolRef(symbol), *flags, symbolName) : memberLetter(file, *flags);
        Expected<bool> copy = object && !reference ? isCopy(*object, SymbolRef(symbol), copies) : Expected<bool>(false);
        if (!copy) {
            message << "llvm-nm: error: " << name << ": " << toString(copy.takeError()) << "\n";
            return false;
        }

        (reference ? undefined : defined).push_back({symbolName, kind, *copy});
    }

    write(undefined, outcome.undefinedSymbols);
    write(defined, outcome.definedSymbols);
    for (const Listed& symbol : defined) {
        outcome.comdats += symbol.copy ? symbol.name + "\n" : "";
    }

    outcome.comdats += '\n';
    return true;
}

// An object's sections: every COFF .drectve section's text, as llvm-readobj --coff-directives prints it, for DEFAULTLIB in any case
// (flag kind 1) and its references (kind 6, readDirectives); ELF, an SHT_LLVM_DEPENDENT_LIBRARIES section (.deplibs). False with
// llvm-readobj's message when a section cannot be read.
bool readSections(const ObjectFile& file, StringRef name, TataraResult& outcome, raw_ostream& message)
{
    bool elf = isa<ELFObjectFileBase>(&file);
    for (const SectionRef& section : file.sections()) {
        Expected<StringRef> sectionName = section.getName();
        if (!sectionName) {
            message << "llvm-readobj: error: " << name << ": " << toString(sectionName.takeError()) << "\n";
            return false;
        }

        if (elf) {
            outcome.hasDefaultLibrary |= ELFSectionRef(section).getType() == ELF::SHT_LLVM_DEPENDENT_LIBRARIES;
            continue;
        }

        if (*sectionName != ".drectve") {
            continue;
        }

        Expected<StringRef> contents = section.getContents();
        if (!contents) {
            message << "llvm-readobj: error: " << name << ": " << toString(contents.takeError()) << "\n";
            return false;
        }

        outcome.hasDefaultLibrary |= contents->contains_insensitive("DEFAULTLIB");
        if (!readDirectives(*contents, name, outcome.directives, message)) {
            return false;
        }
    }

    return true;
}

// LLD's ImportFile::parse check, which COFFImportFile does not make before it reads a short import member's header and name: the member
// holds its whole header and exactly SizeOfData bytes after it, among them the name's NUL (C-533 B1).
bool isBrokenImport(const SymbolicFile& file)
{
    const auto* import = dyn_cast<COFFImportFile>(&file);
    StringRef data = import ? import->getData() : StringRef();
    size_t header = sizeof(coff_import_header);
    return import && (data.size() < header || data.size() != header + import->getCOFFImportHeader()->SizeOfData
        || !data.drop_front(header).contains('\0'));
}

// One member (an object is one): its block of each per-member text (kinds 1, 2, 4, 5 and 6) and its flag. name: the input's path, or
// "<path>(<member>)" for an archive's member, as llvm-nm names it in a message. False with the tool's message; a broken short import
// member with LLD's.
bool readMember(const SymbolicFile& file, StringRef name, TataraResult& outcome, raw_ostream& message)
{
    if (isBrokenImport(file)) {
        message << "llvm-nm: error: " << name << ": broken import library\n";
        return false;
    }

    const auto* object = dyn_cast<ObjectFile>(&file);
    outcome.forms += isa<COFFImportFile>(&file) ? 'i' : object && !object->isRelocatableObject() ? 's' : 'o';
    outcome.forms += '\n';
    DenseSet<uint64_t> copies;
    bool read = (!object || copySections(*object, name, copies, message)) && readSymbols(file, copies, name, outcome, message)
        && (!object || readSections(*object, name, outcome, message));
    outcome.directives += '\n';
    return read;
}

// The formats an input or an archive's member is read in, as lld-link links them: a COFF or ELF object, a COFF short import member,
// and LLVM bitcode (LinkAmbiguity's P4: lld-link links a bitcode member; llvm-nm reads it through IRObjectFile).
bool isMember(const Binary& binary)
{
    return isa<COFFObjectFile>(&binary) || isa<ELFObjectFileBase>(&binary) || isa<COFFImportFile>(&binary) || isa<IRObjectFile>(&binary);
}

// An archive's member as llvm-nm reads it, named "<path>(<member>)": through readMember, or a failure for another format.
bool readChild(const Archive::Child& child, StringRef path, LLVMContext& context, TataraResult& outcome, raw_ostream& message)
{
    Expected<StringRef> member = child.getName();
    if (!member) {
        message << "llvm-nm: error: " << path << ": " << toString(member.takeError()) << "\n";
        return false;
    }

    std::string name = (path + "(" + *member + ")").str();
    Expected<std::unique_ptr<Binary>> binary = child.getAsBinary(&context);
    if (!binary) {
        message << "llvm-nm: error: " << name << ": " << toString(binary.takeError()) << "\n";
        return false;
    }

    if (!isMember(**binary)) {
        message << "llvm-nm: error: " << name << ": not a COFF or ELF object\n";
        return false;
    }

    return readMember(*cast<SymbolicFile>(binary->get()), name, outcome, message);
}

// The archive's symbol index (kind 7) as Archive::symbols() yields it: "<position> <name>\n" per entry, the position the walk gave
// the member it names. False with LLVM's message when an entry's member cannot be read or is no member the walk read.
bool readIndex(const Archive& archive, const DenseMap<uint64_t, uint32_t>& positions, StringRef path, TataraResult& outcome, raw_ostream& message)
{
    for (const Archive::Symbol& symbol : archive.symbols()) {
        Expected<Archive::Child> member = symbol.getMember();
        if (!member) {
            message << "llvm-nm: error: " << path << ": " << toString(member.takeError()) << "\n";
            return false;
        }

        auto position = positions.find(member->getChildOffset());
        if (!listable(symbol.getName(), path, message)) {
            return false;
        }

        if (position == positions.end()) {
            message << "llvm-nm: error: " << path << ": the symbol index names no member for " << symbol.getName() << "\n";
            return false;
        }

        outcome.index += std::to_string(position->second) + " " + symbol.getName().str() + "\n";
    }

    return true;
}

// An archive (flag kind 2): its members in order through readChild, each member's position kept by its offset, then its index. A
// thin archive fails: its members are outside the file. False with LLVM's message as llvm-nm prints it.
bool readArchive(const Archive& archive, StringRef path, LLVMContext& context, TataraResult& outcome, raw_ostream& message)
{
    outcome.isArchive = 1;
    if (archive.isThin()) {
        message << "llvm-nm: error: " << path << ": thin archive\n";
        return false;
    }

    DenseMap<uint64_t, uint32_t> positions;
    Error error = Error::success();
    bool read = true;
    for (const Archive::Child& child : archive.children(error)) {
        uint32_t position = positions.size();
        positions[child.getChildOffset()] = position;
        if (!readChild(child, path, context, outcome, message)) {
            read = false;
            break;
        }
    }

    if (error) {
        message << "llvm-nm: error: " << path << ": " << toString(std::move(error)) << "\n";
        return false;
    }

    return read && readIndex(archive, positions, path, outcome, message);
}

} // namespace

int32_t TATARA_CALL tatara_inspect(TataraText obj, TataraResult** result)
{
    return tatara::run(result, [&](TataraResult& outcome) -> int32_t {
        raw_string_ostream message(outcome.message);
        StringRef path = tatara::view(obj);
        LLVMContext context; // the operation's own, for bitcode
        Expected<OwningBinary<Binary>> binary = createBinary(path, &context);
        if (!binary) {
            message << "llvm-nm: error: " << path << ": " << toString(binary.takeError()) << "\n";
            return 1;
        }

        const Binary* input = binary->getBinary();
        if (const auto* archive = dyn_cast<Archive>(input)) {
            return readArchive(*archive, path, context, outcome, message) ? 0 : 1;
        }

        if (!isMember(*input)) {
            message << "llvm-nm: error: " << path << ": not a COFF or ELF object\n";
            return 1;
        }

        return readMember(*cast<SymbolicFile>(input), path, outcome, message) ? 0 : 1;
    });
}
