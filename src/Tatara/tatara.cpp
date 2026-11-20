// tatara_identity and tatara_abi: the build identity, the LLVM version and the ABI this DLL was built with (tatara.h).
#include "tatara.h"

#include "llvm/Config/llvm-config.h"

#ifndef TATARA_BUILD_IDENTITY
#error TATARA_BUILD_IDENTITY is defined by build.ps1.
#endif
#ifndef TATARA_ABI
#error TATARA_ABI is defined by build.ps1.
#endif

#define TATARA_STRING(x) #x
#define TATARA_EXPANDED_STRING(x) TATARA_STRING(x)

void TATARA_CALL tatara_identity(TataraIdentity* identity)
{
    identity->build = TATARA_EXPANDED_STRING(TATARA_BUILD_IDENTITY);
    identity->llvmVersion = LLVM_VERSION_STRING;
}

const char* TATARA_CALL tatara_abi(void)
{
    return TATARA_EXPANDED_STRING(TATARA_ABI);
}
