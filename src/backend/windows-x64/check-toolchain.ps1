# Checks a development toolchain root, as setup.ps1 installed it (Tatara B6b3; decision 20 Q3 (a): Kimi reads no root, so the root's
# checks are this script's, unchanged in rule from the C# verify of B6b1): installation.json's shape (schema 1 of this profile, the tools'
# and the supporting DLLs' hashes by name, tatara.dll's hash); each tool's identity (toolchain.ps1's Get-KimiToolchainToolSha256: the
# version banner of the seven that print one, llvm-dlltool's pin) and SHA-256 against the record; the supporting DLLs' and tatara.dll's
# hashes against the record. A root's windows_x64 is not read (Tatara B6b6: the compiler carries the libraries). tatara.dll is hashed,
# not loaded. A failure throws naming the file and the remedy. Callers: setup.ps1's last step and verify.ps1 -VerifyToolchain.
[CmdletBinding()]
param([string] $ToolchainRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$profile = Read-KimiWindowsProfile
$setup = "src/backend/windows-x64/setup.ps1 -LlvmRoot <the LLVM $($profile.llvmVersion) archive, extracted>"
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
foreach ($name in Get-KimiToolchainToolNames) {
    $path = Get-InstalledFile "$name.exe"
    try { $hash = Get-KimiToolchainToolSha256 $name $path }
    catch { throw "Tool identity mismatch: $path. Run $setup after an intentional update ($($_.Exception.Message))" }
    if ($installed.tools.$name -isnot [string] -or $hash -cne $installed.tools.$name) {
        throw "Tool executable SHA-256 mismatch: $path. Run $setup after an intentional update."
    }
    Write-Output "${name}: $hash"
}
foreach ($dll in $installed.supportingDlls.PSObject.Properties) {
    if ([IO.Path]::GetFileName($dll.Name) -cne $dll.Name -or $dll.Value -isnot [string] -or (Get-KimiSha256 (Get-InstalledFile $dll.Name)) -cne $dll.Value) {
        throw "Toolchain supporting DLL SHA-256 mismatch: $($dll.Name)"
    }
}
$tatara = Get-InstalledFile 'tatara.dll'
if ((Get-KimiSha256 $tatara) -cne $installed.tatara.sha256) { throw "tatara.dll SHA-256 mismatch: $tatara. Run $setup again." }
Write-Output "tatara.dll: $($installed.tatara.sha256)"
Write-Output "Development toolchain check passed: $ToolchainRoot"
