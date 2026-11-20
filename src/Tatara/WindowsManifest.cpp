// llvm::windows_manifest's members as LLVM's own build without LibXml2 defines them (DECISIONS T-M3: no LibXml2), so lldCOFF links
// without LLVMWindowsManifest.lib: no merger is available and a merge is an error. Kimi's link passes no manifest option.
#include "llvm/WindowsManifest/WindowsManifestMerger.h"

#include "llvm/Support/MemoryBuffer.h"

#include <memory>

namespace llvm {
namespace windows_manifest {

bool isAvailable() { return false; }

char WindowsManifestError::ID = 0;

WindowsManifestError::WindowsManifestError(const Twine& Msg) : Msg(Msg.str()) {}

void WindowsManifestError::log(raw_ostream& OS) const { OS << Msg; }

class WindowsManifestMerger::WindowsManifestMergerImpl {};

WindowsManifestMerger::WindowsManifestMerger() : Impl(std::make_unique<WindowsManifestMergerImpl>()) {}

WindowsManifestMerger::~WindowsManifestMerger() = default;

Error WindowsManifestMerger::merge(MemoryBufferRef) { return make_error<WindowsManifestError>("no libxml2"); }

std::unique_ptr<MemoryBuffer> WindowsManifestMerger::getMergedManifest() { return nullptr; }

} // namespace windows_manifest
} // namespace llvm
