# Builds tatara.dll from an extracted LLVM release archive and checks it (designs: Tatara A1 section 2.3, A2 section 4).
# Every output and intermediate goes under -OutputDirectory; nothing is written inside src/Tatara.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../backend/windows-x64/toolchain.ps1')

# 1. Version: the supply catalog is the one source; the tools and the header are checked against it.
$expectedVersion = (Read-KimiWindowsProfile).llvmVersion
if (-not (Test-Path -LiteralPath $LlvmRoot -PathType Container)) { throw "LLVM root not found: $LlvmRoot" }
$LlvmRoot = (Resolve-Path -LiteralPath $LlvmRoot).Path
$clang = (Get-KimiLlvmToolIdentity -ToolPath (Join-Path $LlvmRoot 'bin/clang-cl.exe') -ExpectedVersion $expectedVersion).path
$lld = (Get-KimiLlvmToolIdentity -ToolPath (Join-Path $LlvmRoot 'bin/lld-link.exe') -ExpectedVersion $expectedVersion).path
$configHeader = Join-Path $LlvmRoot 'include/llvm/Config/llvm-config.h'
if (-not (Test-Path -LiteralPath $configHeader -PathType Leaf)) { throw "LLVM header not found: $configHeader" }
$headerVersion = [regex]::Match((Get-Content -LiteralPath $configHeader -Raw), '(?m)^#define LLVM_VERSION_STRING "([^"]*)"').Groups[1].Value
if ($headerVersion -cne $expectedVersion) { throw "LLVM version mismatch: header=$configHeader; expected=$expectedVersion; actual=$headerVersion" }
$configCmake = Join-Path $LlvmRoot 'lib/cmake/llvm/LLVMConfig.cmake'
if (-not (Test-Path -LiteralPath $configCmake -PathType Leaf)) { throw "LLVM configuration not found: $configCmake" }
$definitions = [regex]::Match((Get-Content -LiteralPath $configCmake -Raw), '(?m)^set\(LLVM_DEFINITIONS "([^"]*)"\)')
if (-not $definitions.Success) { throw "LLVM_DEFINITIONS not found: $configCmake" }
$definitions = @($definitions.Groups[1].Value -split ' ' | Where-Object { $_ })

# 2. Output place: outside the repository, or inside it only where Git ignores it; never inside src/Tatara.
$repository = (& git -C $PSScriptRoot rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw "Cannot find the repository of $PSScriptRoot" }
$repository = [IO.Path]::GetFullPath($repository)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).ProviderPath)
function Test-Inside([string] $path, [string] $directory) {
    $relative = [IO.Path]::GetRelativePath($directory, $path)
    return -not ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or [IO.Path]::IsPathRooted($relative))
}
if (Test-Inside $OutputDirectory $PSScriptRoot) { throw "Output directory is inside src/Tatara: $OutputDirectory" }
if (Test-Inside $OutputDirectory $repository) {
    & git -C $repository check-ignore -q -- ([IO.Path]::GetRelativePath($repository, $OutputDirectory).Replace('\', '/'))
    if ($LASTEXITCODE -eq 1) { throw "Output directory is inside the repository and not ignored by Git (git check-ignore exit 1): $OutputDirectory" }
    if ($LASTEXITCODE -ne 0) { throw "git check-ignore failed with exit code ${LASTEXITCODE}: $OutputDirectory" }
}

# 3. Tools outside the archive: Visual Studio's C++ tools and the DIA SDK; clang-cl and lld-link find the toolset and the SDK.
$vswhere = (Get-Command vswhere.exe -ErrorAction SilentlyContinue).Source
if (-not $vswhere) { $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe' }
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw "vswhere not found: $vswhere" }
$visualStudio = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'Visual Studio with the C++ x64 tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64) not found by vswhere' }
$dia = Join-Path $visualStudio 'DIA SDK/lib/amd64/diaguids.lib'
if (-not (Test-Path -LiteralPath $dia -PathType Leaf)) { throw "DIA SDK not found: $dia" }

# The build identity (identity.ps1's one rule) and the ABI: the SHA-256 (lower-case hex) of tatara.h's bytes.
. (Join-Path $PSScriptRoot 'identity.ps1')
$identity = Get-TataraBuildIdentity
$abi = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'tatara.h') -Algorithm SHA256).Hash.ToLowerInvariant()

# 4. Compile with the archive's configuration: static release CRT, no RTTI, no exceptions, its LLVM_DEFINITIONS.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cpp' -File | ForEach-Object FullName | Sort-Object -CaseSensitive)
$objects = @($sources | ForEach-Object { Join-Path $OutputDirectory ([IO.Path]::GetFileNameWithoutExtension($_) + '.obj') })
$dll = Join-Path $OutputDirectory 'tatara.dll'
$compile = @('/nologo', '/c', '/MT', '/GR-', '/EHs-c-', '/O2', '/std:c++17', '/Brepro') + $definitions +
    @("-DTATARA_BUILD_IDENTITY=$identity", "-DTATARA_ABI=$abi", '-I', (Join-Path $LlvmRoot 'include'),
    "/Fo$OutputDirectory$([IO.Path]::DirectorySeparatorChar)") + $sources
$driver = (& $clang @compile '-###' 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw "clang-cl failed: $driver" }
$toolset = @([regex]::Matches($driver, '(?i)[\\/]MSVC[\\/]+(\d+(?:\.\d+)+)[\\/]') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
$sdk = @([regex]::Matches($driver, '(?i)Windows Kits[\\/]+10[\\/]+include[\\/]+(\d+(?:\.\d+)+)[\\/]') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
if ($toolset.Count -ne 1) { throw "Visual Studio's C++ toolset not found by clang-cl (or ambiguous): $driver" }
if ($sdk.Count -ne 1) { throw "Windows SDK not found by clang-cl (or ambiguous): $driver" }
& $clang @compile
if ($LASTEXITCODE -ne 0) { throw "clang-cl failed with exit code $LASTEXITCODE" }

# 5. Link as LLVM's own tools link. Every LLVM*.lib except LLVMWindowsManifest (LibXml2, not added; WindowsManifest.cpp defines its
# members) and the LLVM-C import library is offered; the linker takes what is referenced.
$libraries = @(Get-ChildItem -LiteralPath (Join-Path $LlvmRoot 'lib') -Filter 'LLVM*.lib' -File |
    Where-Object { $_.Name -cnotin 'LLVMWindowsManifest.lib', 'LLVM-C.lib' } | ForEach-Object Name | Sort-Object -CaseSensitive)
# lldCOFF.lib: LLD's COFF driver (tatara_link's lld::coff::link). lldCommon.lib: lld::lldMain, LLD's error handler and its shared
# linker context, which lldCOFF uses.
$libraries += @('lldCOFF.lib', 'lldCommon.lib')
# The Windows libraries and options are read from LLVMSupport's INTERFACE_LINK_LIBRARIES in the archive's LLVMExports.cmake.
# The reader accepts only the shapes it knows and fails naming any other token, so an LLVM update links as that LLVM does or stops.
$exports = Join-Path $LlvmRoot 'lib/cmake/llvm/LLVMExports.cmake'
if (-not (Test-Path -LiteralPath $exports -PathType Leaf)) { throw "LLVM exports not found: $exports" }
$interface = [regex]::Match((Get-Content -LiteralPath $exports -Raw), '(?m)^set_target_properties\(LLVMSupport PROPERTIES\r?\n\s*INTERFACE_LINK_LIBRARIES "([^"]*)"')
if (-not $interface.Success) { throw "LLVMSupport's INTERFACE_LINK_LIBRARIES not found: $exports" }
# Generator expressions evaluated for a C++ link that is not IntelLLVM's: the IntelLLVM prefix before an option is empty; the
# non-Swift wrapper keeps its content.
$interface = $interface.Groups[1].Value.Replace('\$', '$').Replace(
    '$<$<OR:$<LINK_LANG_AND_ID:C,IntelLLVM>,$<LINK_LANG_AND_ID:CXX,IntelLLVM>,$<LINK_LANG_AND_ID:Fortran,IntelLLVM>>:-Qoption,link,>', '')
$interface = [regex]::Replace($interface, '\$<\$<NOT:\$<LINK_LANGUAGE:Swift>>:([^$<>]*)>', '$1')
$windows = foreach ($token in $interface -split ';') {
    if ($token -cmatch '^LLVM[A-Za-z0-9]+$') { continue }
    elseif ($token -cmatch '^[A-Za-z0-9_]+$') { "$token.lib" }
    elseif ($token -cmatch '^-delayload:([A-Za-z0-9_]+\.dll)$') { "/delayload:$($Matches[1])" }
    elseif ($token -cmatch '^-INCLUDE:([A-Za-z_][A-Za-z0-9_]*)$') { "/include:$($Matches[1])" }
    else { throw "Unknown token in LLVMSupport's INTERFACE_LINK_LIBRARIES ($exports): '$token'" }
}
# The allocator is the archive's rpmalloc (LLVM's integrated CRT allocator in LLVMSupport.lib), selected by the interface's
# /include:malloc, as LLVM's own tools are linked.
if ($windows -cnotcontains '/include:malloc') { throw "LLVMSupport's link interface does not carry -INCLUDE:malloc (the rpmalloc allocator): $exports" }
# One departure: diaguids.lib comes from the discovered Visual Studio, because LLVMDebugInfoPDB's interface names a path on
# LLVM's build machine.
$response = Join-Path $OutputDirectory 'link.rsp'
@("/libpath:`"$(Join-Path $LlvmRoot 'lib')`"") + $libraries + $windows + @("`"$dia`"") |
    Set-Content -LiteralPath $response -Encoding utf8NoBOM
& $lld /nologo /dll /brepro /noimplib "/out:$dll" @objects "@$response"
if ($LASTEXITCODE -ne 0) { throw "lld-link failed with exit code $LASTEXITCODE" }

# 6. Check in a child process (a DLL loaded here would stay locked until this shell exits).
$loaded = @(& (Get-Process -Id $PID).Path -NoProfile -NonInteractive -Command {
    param([string] $path)
    $ErrorActionPreference = 'Stop'
    Add-Type -TypeDefinition @'
public delegate void TataraIdentityFunction(System.IntPtr identity);
public delegate System.IntPtr TataraAbiFunction();
'@
    $library = [Runtime.InteropServices.NativeLibrary]::Load($path)
    $function = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
        [Runtime.InteropServices.NativeLibrary]::GetExport($library, 'tatara_identity'), [TataraIdentityFunction])
    $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal(2 * [IntPtr]::Size)
    $function.Invoke($buffer)
    [Runtime.InteropServices.Marshal]::PtrToStringUTF8([Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, 0))
    [Runtime.InteropServices.Marshal]::PtrToStringUTF8([Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, [IntPtr]::Size))
    $abiFunction = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
        [Runtime.InteropServices.NativeLibrary]::GetExport($library, 'tatara_abi'), [TataraAbiFunction])
    [Runtime.InteropServices.Marshal]::PtrToStringUTF8($abiFunction.Invoke())
} -args $dll)
if ($LASTEXITCODE -ne 0 -or $loaded.Count -ne 3) { throw "Loading $dll failed: $($loaded -join '; ')" }
if ($loaded[0] -cne $identity) { throw "Build identity mismatch: dll=$($loaded[0]); expected=$identity" }
if ($loaded[1] -cne $expectedVersion) { throw "LLVM version mismatch: dll=$($loaded[1]); expected=$expectedVersion" }
if ($loaded[2] -cne $abi) { throw "ABI mismatch: dll=$($loaded[2]); expected=$abi (the SHA-256 of tatara.h)" }

# 7. Report: the DLL's bytes depend on the archive, the toolset and the SDK.
[ordered]@{
    dll = $dll
    sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    buildIdentity = $identity
    abi = $abi
    llvmVersion = $expectedVersion
    msvcToolset = $toolset[0]
    windowsSdk = $sdk[0]
} | Format-Table -HideTableHeaders | Out-String | Write-Output
