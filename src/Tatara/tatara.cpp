// tatara_identity: the build identity and the LLVM version this DLL was built with (tatara.h).
#include "tatara.h"

#include "llvm/Config/llvm-config.h"

#ifndef TATARA_BUILD_IDENTITY
#error TATARA_BUILD_IDENTITY is defined by build.ps1.
#endif

#define TATARA_STRING(x) #x
#define TATARA_EXPANDED_STRING(x) TATARA_STRING(x)

void tatara_identity(TataraIdentity* identity)
{
    identity->build = TATARA_EXPANDED_STRING(TATARA_BUILD_IDENTITY);
    identity->llvmVersion = LLVM_VERSION_STRING;
}
