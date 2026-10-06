[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
    [string] $MismatchedLlvmBin = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$original = Get-Content -LiteralPath $manifestPath -Raw
$testPath = Join-Path (Split-Path -Parent $manifestPath) ('negative-' + [guid]::NewGuid().ToString('N') + '.link.json')
function Expect-Failure([object] $data, [string] $bin, [string] $message) {
    $data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $testPath -Encoding utf8
    $failed = $false
    try { & (Join-Path $PSScriptRoot 'manual-build.ps1') -Manifest $testPath -ToolchainRoot $ToolchainRoot -LlvmBin $bin -Configuration $Configuration | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $message) { throw "Unexpected rejection: $($_.Exception.Message)" }
        $failed = $true
    }
    if (-not $failed) { throw "Expected rejection: $message" }
}
try {
    $data = $original | ConvertFrom-Json
    $data.schemaVersion = 1
    Expect-Failure $data $LlvmBin 'schema 3 is required'
    $data.schemaVersion = 2
    Expect-Failure $data $LlvmBin 'schema 3 is required'
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kimi_backend') | Add-Member -NotePropertyName input -NotePropertyValue 'override.lib'
    Expect-Failure $data $LlvmBin 'Invalid backend toolchain resolution'
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kimi_backend').resolution = 'unknown'
    Expect-Failure $data $LlvmBin 'Invalid backend toolchain resolution'
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kernel32').definitionSha256 = '0' * 64
    Expect-Failure $data $LlvmBin 'Invalid generated kernel32 identity'
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kernel32') | Add-Member -NotePropertyName input -NotePropertyValue 'kernel32.lib'
    Expect-Failure $data $LlvmBin 'Invalid generated kernel32 identity'
    $data = $original | ConvertFrom-Json
    $data.irSha256 = '0' * 64
    Expect-Failure $data $LlvmBin 'IR/manifest SHA-256.*mismatch'
    $data = $original | ConvertFrom-Json
    $data.backendSupport.artifactSha256 = '0' * 64
    Expect-Failure $data $LlvmBin 'Invalid backend supply identity'
    $data = $original | ConvertFrom-Json
    $data.backendSupport.packageVersion = 'unreviewed'
    Expect-Failure $data $LlvmBin 'Invalid backend supply identity'
    $data = $original | ConvertFrom-Json
    $data.expectedUndefinedSymbols = @(@{ symbol = 'missing'; provider = 'unknown' })
    Expect-Failure $data $LlvmBin 'Invalid backend supply identity or runtime dependency'
    $data = $original | ConvertFrom-Json
    $data.codegen.cpu = 'native'
    Expect-Failure $data $LlvmBin 'supported Windows Application profile'
    if ($MismatchedLlvmBin) {
        $data = $original | ConvertFrom-Json
        $data.irSha256 = '0' * 64
        Expect-Failure $data $MismatchedLlvmBin 'IR/manifest SHA-256.*mismatch'
        $record = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($testPath, '.build.json')) -Raw | ConvertFrom-Json
        if ($record.toolchainVerification -cne 'not-performed' -or $null -ne $record.reportedVersionsMatched -or -not $record.unverifiedToolchain) { throw 'Incorrect unverified build record' }
    }
    Write-Output 'Manual-build integrity and toolchain rejection tests passed.'
}
finally {
    # Both paths are exact files derived from a fresh GUID alongside the explicit manifest.
    Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($testPath, '.build.json')) -Force -ErrorAction SilentlyContinue
}
