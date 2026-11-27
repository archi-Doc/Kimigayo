# Installs the pinned LLVM tools and tatara.dll from the extracted LLVM release archive into a toolchain root and records them in
# installation.json (Tatara design A4 §2.3), after the backend's candidates (build.ps1) reproduce the adopted libraries the compiler
# carries; no library is installed (Tatara B6b6), and an existing root's windows_x64 stays. The tools and tatara.dll are validated and
# built before the root changes. A file whose destination already has the same SHA-256 is not written, so an unchanged rerun writes
# nothing a running process holds; tatara.dll and the record are replaced by a move. No archive DLL is installed or recorded (no tool
# imports one; PLAN G1175, G1231), and an existing root's DLLs stay. The last step checks the root (check-toolchain.ps1).
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmRoot,
    [string] $ToolchainRoot = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
. (Join-Path $PSScriptRoot '../../Tatara/identity.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$profile = Read-KimiWindowsProfile
$source = Join-Path $LlvmRoot 'bin'
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "LLVM bin not found: $source. Pass -LlvmRoot <the LLVM $($profile.llvmVersion) archive, extracted> (its bin, lib and include)."
}
$LlvmRoot = (Resolve-Path -LiteralPath $LlvmRoot).Path
$source = (Resolve-Path -LiteralPath $source).Path
$names = Get-KimiToolchainToolNames
# Validate the complete source tool set before changing the destination. No downloads or PATH changes.
foreach ($name in $names) { $null = Get-KimiToolchainToolSha256 $name (Join-Path $source "$name.exe") }

# tatara.dll: built outside the repository before the first write to the root (it needs only the archive), its build identity this
# checkout's. build.ps1 loads the DLL in a child process and checks its build identity against the checkout's (identity.ps1); the
# checkout must be the same after the build.
$identity = Get-TataraBuildIdentity
$build = Join-Path ([IO.Path]::GetTempPath()) ('kimi-tatara-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot '../../Tatara/build.ps1') -LlvmRoot $LlvmRoot -OutputDirectory $build
if ((Get-TataraBuildIdentity) -cne $identity) { throw "src/Tatara changed during setup: tatara.dll's build identity $identity is not this checkout's." }
$built = Join-Path $build 'tatara.dll'

New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null
# A tatara.dll an earlier run renamed aside, or the temporary copy of a failed run, goes once no process maps it.
foreach ($old in Get-ChildItem -LiteralPath $ToolchainRoot -File | Where-Object Name -Match '^tatara\.dll\..+\.(old|tmp)$') {
    try { [IO.File]::Delete($old.FullName) }
    catch [UnauthorizedAccessException], [IO.IOException] { Write-Output "Still mapped, kept: $($old.FullName)" }
}
foreach ($file in @($names | ForEach-Object { Join-Path $source "$_.exe" })) {
    $destination = Join-Path $ToolchainRoot ([IO.Path]::GetFileName($file))
    if (-not (Test-KimiSameFile $file $destination)) {
        Copy-Item -LiteralPath $file -Destination $destination -Force
        Write-Output "Installed: $destination"
    }
}
& (Join-Path $PSScriptRoot 'build.ps1') -ToolchainRoot $ToolchainRoot
$candidate = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../../artifacts/backend/windows-x64/verification.json') -Raw | ConvertFrom-Json
# Both candidates are the adopted bytes (profile.json), which the compiler carries.
if ($candidate.kernel32.sha256 -cne $profile.kernel32.sha256) { throw 'Toolchain setup did not reproduce the adopted kernel32 import library. Review the candidate report.' }
if ($candidate.status -cne 'tested-candidate' -or $candidate.unverifiedToolchain -or $candidate.artifactSha256 -cne $profile.artifactSha256) {
    throw 'Toolchain setup did not produce the adopted backend. Review the candidate report.'
}
# The tools the candidates were built and tested with are the installed ones.
$identities = [ordered]@{}
foreach ($name in $names) {
    $identities[$name] = Get-KimiSha256 (Join-Path $ToolchainRoot "$name.exe")
    if ($identities[$name] -cne $candidate.tools.$name.sha256) { throw "Tool changed during setup: $name" }
}

# tatara.dll installed beside a loaded one by renaming that aside (Windows allows renaming a loaded image).
$tatara = Join-Path $ToolchainRoot 'tatara.dll'
$tataraSha256 = Get-KimiSha256 $built
if (-not (Test-KimiSameFile $built $tatara)) {
    $temporary = "$tatara.$([guid]::NewGuid().ToString('N')).tmp"
    Copy-Item -LiteralPath $built -Destination $temporary
    if (Test-Path -LiteralPath $tatara) { Move-Item -LiteralPath $tatara -Destination "$tatara.$(Get-Date -Format yyyyMMddHHmmss).old" }
    Move-KimiFile $temporary $tatara
    Write-Output "Installed: $tatara"
}
Remove-Item -LiteralPath $build -Recurse -Force

# The record last; an equal record is not written (a concurrent verify's read handle would fail the move).
$record = Join-Path $ToolchainRoot 'installation.json'
$temporary = "$record.$([guid]::NewGuid().ToString('N')).tmp"
[ordered]@{ schemaVersion = 1; profile = $profile.profile; tools = $identities; tatara = [ordered]@{ sha256 = $tataraSha256 } } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporary -Encoding utf8
if (Test-KimiSameFile $temporary $record) { Remove-Item -LiteralPath $temporary }
else {
    Move-KimiFile $temporary $record
    Write-Output "Installed: $record"
}
& (Join-Path $PSScriptRoot 'check-toolchain.ps1') -ToolchainRoot $ToolchainRoot
Write-Output "Toolchain ready: $ToolchainRoot"
