[CmdletBinding()]
param(
    [string] $LlvmBin = '',
    [string] $ToolchainRoot = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
$source = (Resolve-Path -LiteralPath $LlvmBin).Path
$profile = Read-KimiWindowsProfile
$names = @('clang', 'opt', 'llc', 'lld-link', 'llvm-nm', 'llvm-readobj', 'llvm-objdump', 'llvm-lib', 'llvm-dlltool')
# Validate the complete source tool set before changing the destination. No downloads or PATH changes.
foreach ($name in $names) {
    $path = Join-Path $source "$name.exe"
    if ($name -eq 'llvm-dlltool') { $null = Get-KimiDlltoolIdentity $path }
    elseif ($name -eq 'llvm-lib') { $null = Get-FileHash -LiteralPath $path -Algorithm SHA256 }
    else { $null = Get-KimiLlvmToolIdentity $path $profile.llvmVersion }
}
if (-not $source.Equals($ToolchainRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
    New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null
    $files = @($names | ForEach-Object { Join-Path $source "$_.exe" }) + @(Get-ChildItem -LiteralPath $source -Filter '*.dll' -File | ForEach-Object FullName)
    foreach ($file in $files) {
        Copy-Item -LiteralPath $file -Destination (Join-Path $ToolchainRoot ([IO.Path]::GetFileName($file))) -Force
    }
}
& (Join-Path $PSScriptRoot 'build.ps1') -ToolchainRoot $ToolchainRoot
$installed = Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
$candidate = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bin/verification.json') -Raw | ConvertFrom-Json
if ($candidate.status -cne 'tested-candidate' -or $candidate.unverifiedToolchain -or $candidate.artifactSha256 -cne $profile.artifactSha256 -or
    (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash.ToLowerInvariant() -cne $profile.artifactSha256) {
    throw 'Toolchain setup did not produce the adopted backend. Review the candidate report.'
}
Write-Output "Toolchain ready: $ToolchainRoot"
