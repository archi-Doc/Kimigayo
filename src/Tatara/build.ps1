# Builds tatara.dll from an extracted LLVM release archive and checks it (design: Tatara A1, section 2.3).
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
    return $relative -eq '.' -or -not ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative))
}
if (Test-Inside $OutputDirectory $PSScriptRoot) { throw "Output directory is inside src/Tatara: $OutputDirectory" }
if (Test-Inside $OutputDirectory $repository) {
    & git -C $repository check-ignore -q -- ([IO.Path]::GetRelativePath($repository, $OutputDirectory).Replace('\', '/'))
    if ($LASTEXITCODE -ne 0) { throw "Output directory is inside the repository and not ignored by Git: $OutputDirectory" }
}

# 3. Tools outside the archive: Visual Studio's C++ tools and the DIA SDK; clang-cl and lld-link find the toolset and the SDK.
$vswhere = (Get-Command vswhere.exe -ErrorAction SilentlyContinue).Source
if (-not $vswhere) { $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe' }
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw "vswhere not found: $vswhere" }
$visualStudio = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'Visual Studio with the C++ x64 tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64) not found by vswhere' }
$dia = Join-Path $visualStudio 'DIA SDK/lib/amd64/diaguids.lib'
if (-not (Test-Path -LiteralPath $dia -PathType Leaf)) { throw "DIA SDK not found: $dia" }

# The build identity: SHA-256 (lower-case hex) over every .h, .cpp and .ps1 file under src/Tatara, recursively, in ordinal
# order of the path relative to src/Tatara written with '/'; for each file the path's UTF-8 bytes, a NUL, then the file's bytes.
$inputs = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object { $_.Extension -cin '.h', '.cpp', '.ps1' }) {
    $inputs.Add([IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace('\', '/'), $file.FullName)
}
$hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
foreach ($entry in $inputs.GetEnumerator()) {
    $hash.AppendData([Text.Encoding]::UTF8.GetBytes($entry.Key + [char]0))
    $hash.AppendData([IO.File]::ReadAllBytes($entry.Value))
}
$identity = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()

# 4. Compile with the archive's configuration: static release CRT, no RTTI, no exceptions, its LLVM_DEFINITIONS.
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$object = Join-Path $OutputDirectory 'tatara.obj'
$dll = Join-Path $OutputDirectory 'tatara.dll'
Remove-Item -LiteralPath $object, $dll -ErrorAction SilentlyContinue
$compile = @('/nologo', '/c', '/MT', '/GR-', '/EHs-c-', '/O2', '/std:c++17', '/Brepro') + $definitions +
    @("-DTATARA_BUILD_IDENTITY=$identity", '-I', (Join-Path $LlvmRoot 'include'), "/Fo$object", (Join-Path $PSScriptRoot 'tatara.cpp'))
$driver = (& $clang @compile '-###' 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw "clang-cl failed: $driver" }
$toolset = @([regex]::Matches($driver, '(?i)[\\/]MSVC[\\/]+(\d+(?:\.\d+)+)[\\/]') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
$sdk = @([regex]::Matches($driver, '(?i)Windows Kits[\\/]+10[\\/]+include[\\/]+(\d+(?:\.\d+)+)[\\/]') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
if ($toolset.Count -ne 1) { throw "Visual Studio's C++ toolset not found by clang-cl (or ambiguous): $driver" }
if ($sdk.Count -ne 1) { throw "Windows SDK not found by clang-cl (or ambiguous): $driver" }
& $clang @compile
if ($LASTEXITCODE -ne 0) { throw "clang-cl failed with exit code $LASTEXITCODE" }

# 5. Link: every LLVM*.lib except LLVMWindowsManifest (LibXml2, not added) and the LLVM-C import library is offered; the linker
# takes what is referenced, so an LLVM update changes no list.
$libraries = @(Get-ChildItem -LiteralPath (Join-Path $LlvmRoot 'lib') -Filter 'LLVM*.lib' -File |
    Where-Object { $_.Name -cnotin 'LLVMWindowsManifest.lib', 'LLVM-C.lib' } | ForEach-Object Name | Sort-Object -CaseSensitive)
$response = Join-Path $OutputDirectory 'link.rsp'
@("/libpath:`"$(Join-Path $LlvmRoot 'lib')`"") + $libraries +
    @('psapi.lib', 'shell32.lib', 'ole32.lib', 'oleaut32.lib', 'uuid.lib', 'advapi32.lib', 'ws2_32.lib', 'ntdll.lib', 'delayimp.lib', 'version.lib', "`"$dia`"") |
    Set-Content -LiteralPath $response -Encoding utf8NoBOM
& $lld /nologo /dll /brepro /noimplib "/out:$dll" $object "@$response"
if ($LASTEXITCODE -ne 0) { throw "lld-link failed with exit code $LASTEXITCODE" }

# 6. Check in a child process (a DLL loaded here would stay locked until this shell exits).
$loaded = @(& (Get-Process -Id $PID).Path -NoProfile -NonInteractive -Command {
    param([string] $path)
    $ErrorActionPreference = 'Stop'
    Add-Type -TypeDefinition 'public delegate void TataraIdentityFunction(System.IntPtr identity);'
    $library = [Runtime.InteropServices.NativeLibrary]::Load($path)
    $function = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
        [Runtime.InteropServices.NativeLibrary]::GetExport($library, 'tatara_identity'), [TataraIdentityFunction])
    $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal(2 * [IntPtr]::Size)
    $function.Invoke($buffer)
    [Runtime.InteropServices.Marshal]::PtrToStringUTF8([Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, 0))
    [Runtime.InteropServices.Marshal]::PtrToStringUTF8([Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, [IntPtr]::Size))
} -args $dll)
if ($LASTEXITCODE -ne 0 -or $loaded.Count -ne 2) { throw "Loading $dll failed: $($loaded -join '; ')" }
if ($loaded[0] -cne $identity) { throw "Build identity mismatch: dll=$($loaded[0]); expected=$identity" }
if ($loaded[1] -cne $expectedVersion) { throw "LLVM version mismatch: dll=$($loaded[1]); expected=$expectedVersion" }

# 7. Report: the DLL's bytes depend on the archive, the toolset and the SDK.
[ordered]@{
    dll = $dll
    sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    buildIdentity = $identity
    llvmVersion = $expectedVersion
    msvcToolset = $toolset[0]
    windowsSdk = $sdk[0]
} | Format-Table -HideTableHeaders | Out-String | Write-Output
