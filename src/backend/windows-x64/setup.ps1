# Installs the pinned LLVM tools, the backend and tatara.dll from the extracted LLVM release archive into a toolchain root and records
# them in installation.json (Tatara design A4 §2.3). A file whose destination already has the same SHA-256 is not written, so an
# unchanged rerun writes nothing a running process holds; tatara.dll and the record are replaced by a move.
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
$names = @('clang', 'opt', 'llc', 'lld-link', 'llvm-nm', 'llvm-readobj', 'llvm-objdump', 'llvm-lib', 'llvm-dlltool')
# Validate the complete source tool set before changing the destination. No downloads or PATH changes.
foreach ($name in $names) {
    $path = Join-Path $source "$name.exe"
    if ($name -eq 'llvm-dlltool') { $null = Get-KimiDlltoolIdentity $path }
    elseif ($name -eq 'llvm-lib') { $null = Get-FileHash -LiteralPath $path -Algorithm SHA256 }
    else { $null = Get-KimiLlvmToolIdentity $path $profile.llvmVersion }
}

function Get-Sha256([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

# Whether the destination already holds the source's bytes; such a destination is never written.
function Test-SameFile([string] $Source, [string] $Destination) {
    return (Test-Path -LiteralPath $Destination -PathType Leaf) -and (Get-Sha256 $Destination) -ceq (Get-Sha256 $Source)
}

New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null
# A tatara.dll an earlier run renamed aside goes once no process maps it.
foreach ($old in Get-ChildItem -LiteralPath $ToolchainRoot -Filter 'tatara.dll.*.old' -File) {
    try { [IO.File]::Delete($old.FullName) }
    catch [UnauthorizedAccessException], [IO.IOException] { Write-Output "Still mapped, kept: $($old.FullName)" }
}
$dllFiles = @(Get-ChildItem -LiteralPath $source -Filter '*.dll' -File | ForEach-Object FullName)
foreach ($file in @($names | ForEach-Object { Join-Path $source "$_.exe" }) + $dllFiles) {
    $destination = Join-Path $ToolchainRoot ([IO.Path]::GetFileName($file))
    if (-not (Test-SameFile $file $destination)) {
        Copy-Item -LiteralPath $file -Destination $destination -Force
        Write-Output "Installed: $destination"
    }
}
& (Join-Path $PSScriptRoot 'build.ps1') -ToolchainRoot $ToolchainRoot
$installed = Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
$candidate = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../../artifacts/backend/windows-x64/verification.json') -Raw | ConvertFrom-Json
if ($candidate.status -cne 'tested-candidate' -or $candidate.unverifiedToolchain -or $candidate.artifactSha256 -cne $profile.artifactSha256 -or
    (Get-Sha256 $installed) -cne $profile.artifactSha256) {
    throw 'Toolchain setup did not produce the adopted backend. Review the candidate report.'
}
# The backend builder publishes the already-tested shared imports and generation conditions.
$identities = [ordered]@{}
foreach ($name in $names) {
    $identities[$name] = Get-Sha256 (Join-Path $ToolchainRoot "$name.exe")
    if ($identities[$name] -cne $candidate.tools.$name.sha256) { throw "Tool changed during setup: $name" }
}
$dlls = [ordered]@{}
foreach ($file in $dllFiles) {
    $dlls[[IO.Path]::GetFileName($file)] = Get-Sha256 (Join-Path $ToolchainRoot ([IO.Path]::GetFileName($file)))
}

# tatara.dll: built outside the repository, its build identity this checkout's, installed beside a loaded one by renaming that aside.
# build.ps1 loads the DLL in a child process and checks its build identity against the checkout's (identity.ps1); the checkout must
# be the same after the build.
$identity = Get-TataraBuildIdentity
$build = Join-Path ([IO.Path]::GetTempPath()) ('kimi-tatara-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot '../../Tatara/build.ps1') -LlvmRoot $LlvmRoot -OutputDirectory $build
if ((Get-TataraBuildIdentity) -cne $identity) { throw "src/Tatara changed during setup: tatara.dll's build identity $identity is not this checkout's." }
$built = Join-Path $build 'tatara.dll'
$tatara = Join-Path $ToolchainRoot 'tatara.dll'
$tataraSha256 = Get-Sha256 $built
if (-not (Test-SameFile $built $tatara)) {
    $temporary = "$tatara.$([guid]::NewGuid().ToString('N')).tmp"
    Copy-Item -LiteralPath $built -Destination $temporary
    if (Test-Path -LiteralPath $tatara) { Move-Item -LiteralPath $tatara -Destination "$tatara.$(Get-Date -Format yyyyMMddHHmmss).old" }
    Move-Item -LiteralPath $temporary -Destination $tatara
    Write-Output "Installed: $tatara"
}
Remove-Item -LiteralPath $build -Recurse -Force

# The record last; an equal record is not written (a concurrent verify's read handle would fail the move).
$record = Join-Path $ToolchainRoot 'installation.json'
$temporary = "$record.$([guid]::NewGuid().ToString('N')).tmp"
[ordered]@{ schemaVersion = 1; profile = $profile.profile; tools = $identities; supportingDlls = $dlls; tatara = [ordered]@{ sha256 = $tataraSha256 } } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporary -Encoding utf8
if (Test-SameFile $temporary $record) { Remove-Item -LiteralPath $temporary }
else {
    Move-Item -LiteralPath $temporary -Destination $record -Force
    Write-Output "Installed: $record"
}
Write-Output "Toolchain ready: $ToolchainRoot"
