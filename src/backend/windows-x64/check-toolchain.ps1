# Checks a development toolchain root, as setup.ps1 installed it (Tatara B6b3; decision 20 Q3 (a): Kimi reads no root, so the root's
# checks are this script's, unchanged in rule from the C# verify of B6b1): installation.json's shape (schema 1 of this profile, the tools'
# and the supporting DLLs' hashes by name, tatara.dll's hash); each tool's SHA-256 against the record (llvm-dlltool's also against
# profile.json's) after its version banner (the seven that print one); the supporting DLLs' and tatara.dll's hashes against the record;
# and the root's windows_x64 until B6b6 removes it: the backend's and kernel32's SHA-256 against the catalog (profile.json), kernel32's
# generation record and definition, and its content (kernel32.ps1's inspection). tatara.dll is hashed, not loaded. A failure throws
# naming the file and the remedy. Callers: setup.ps1's last step and verify.ps1 -VerifyToolchain.
[CmdletBinding()]
param([string] $ToolchainRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$profile = Read-KimiWindowsProfile
$setup = "src/backend/windows-x64/setup.ps1 -LlvmRoot <the LLVM $($profile.llvmVersion) archive, extracted>"
function Get-Sha256([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-InstalledFile([string] $Name) {
    $path = Join-Path $ToolchainRoot $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Toolchain file not found: $path. Run $setup for the selected toolchain." }
    return $path
}

$installed = Get-Content -LiteralPath (Get-InstalledFile 'installation.json') -Raw | ConvertFrom-Json
if (-not ($installed -is [pscustomobject] -and $installed.schemaVersion -is [long] -and $installed.schemaVersion -eq 1 -and
    $installed.profile -is [string] -and $installed.profile -ceq $profile.profile -and $installed.tools -is [pscustomobject] -and
    $installed.supportingDlls -is [pscustomobject] -and $installed.tatara -is [pscustomobject] -and $installed.tatara.sha256 -is [string])) {
    throw "Invalid toolchain installation record. Run $setup again."
}
foreach ($name in @('clang', 'opt', 'llc', 'lld-link', 'llvm-nm', 'llvm-readobj', 'llvm-objdump', 'llvm-lib', 'llvm-dlltool')) {
    $path = Get-InstalledFile "$name.exe"
    if ($name -cnotin 'llvm-lib', 'llvm-dlltool') { $null = Get-KimiLlvmToolIdentity $path $profile.llvmVersion }
    $hash = Get-Sha256 $path
    if ($installed.tools.$name -isnot [string] -or $hash -cne $installed.tools.$name -or ($name -ceq 'llvm-dlltool' -and $hash -cne $profile.kernel32.dlltoolSha256)) {
        throw "Tool executable SHA-256 mismatch: $path. Run $setup after an intentional update."
    }
    Write-Output "${name}: $hash"
}
foreach ($dll in $installed.supportingDlls.PSObject.Properties) {
    if ([IO.Path]::GetFileName($dll.Name) -cne $dll.Name -or $dll.Value -isnot [string] -or (Get-Sha256 (Get-InstalledFile $dll.Name)) -cne $dll.Value) {
        throw "Toolchain supporting DLL SHA-256 mismatch: $($dll.Name)"
    }
}
$tatara = Get-InstalledFile 'tatara.dll'
if ((Get-Sha256 $tatara) -cne $installed.tatara.sha256) { throw "tatara.dll SHA-256 mismatch: $tatara. Run $setup again." }
Write-Output "tatara.dll: $($installed.tatara.sha256)"

$backend = Get-InstalledFile "windows_x64/kimi_backend_windows_x64_v1.lib"
if ((Get-Sha256 $backend) -cne $profile.artifactSha256) { throw "Adopted backend SHA-256 mismatch: $backend" }
$kernel = Get-InstalledFile 'windows_x64/kernel32.lib'
$generation = Get-Content -LiteralPath (Get-InstalledFile 'windows_x64/kernel32.json') -Raw | ConvertFrom-Json
$kernelHash = Get-Sha256 $kernel
$conditions = [ordered]@{ generator = 'llvm-dlltool'; generatorSha256 = $profile.kernel32.dlltoolSha256; target = 'x86_64-pc-windows-msvc'; dll = 'KERNEL32.dll'
    definitionSha256 = $profile.kernel32.definitionSha256; sha256 = $kernelHash }
foreach ($condition in $conditions.GetEnumerator()) {
    if ($generation -isnot [pscustomobject] -or $generation.($condition.Key) -isnot [string] -or $generation.($condition.Key) -cne $condition.Value) {
        throw "Installed kernel32 generation conditions or SHA-256 mismatch. Run $setup again."
    }
}
if ($kernelHash -cne $profile.kernel32.sha256) { throw "Adopted kernel32 SHA-256 mismatch: $kernel" }
if ((Get-Sha256 (Get-InstalledFile 'windows_x64/kernel32.def')) -cne $profile.kernel32.definitionSha256) { throw 'Installed kernel32 definition SHA-256 mismatch.' }
Test-KimiKernel32Library (Join-Path $ToolchainRoot 'llvm-dlltool.exe') (Join-Path $ToolchainRoot 'llvm-readobj.exe') $kernel
Write-Output "Development toolchain check passed: $ToolchainRoot"
