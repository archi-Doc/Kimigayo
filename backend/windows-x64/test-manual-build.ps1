[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [Parameter(Mandatory)] [string] $LlvmBin,
    [string] $MismatchedLlvmBin = ''
)
$ErrorActionPreference = 'Stop'
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$original = Get-Content -LiteralPath $manifestPath -Raw
$testPath = Join-Path (Split-Path -Parent $manifestPath) ('negative-' + [guid]::NewGuid().ToString('N') + '.link.json')
function Expect-Failure([object] $data, [string] $bin, [string] $message) {
    $data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $testPath -Encoding utf8
    $failed = $false
    try { & (Join-Path $PSScriptRoot 'manual-build.ps1') -Manifest $testPath -LlvmBin $bin | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $message) { throw "Unexpected rejection: $($_.Exception.Message)" }
        $failed = $true
    }
    if (-not $failed) { throw "Expected rejection: $message" }
}
try {
    $data = $original | ConvertFrom-Json
    $data.irSha256 = '0' * 64
    Expect-Failure $data $LlvmBin 'IR/manifest SHA-256 mismatch'
    $data = $original | ConvertFrom-Json
    $data.backendSupport.artifactSha256 = '0' * 64
    Expect-Failure $data $LlvmBin 'Invalid backend supply identity'
    $data = $original | ConvertFrom-Json
    $data.backendSupport.packageVersion = 'unreviewed'
    Expect-Failure $data $LlvmBin 'Invalid backend supply identity'
    $data = $original | ConvertFrom-Json
    $data.expectedUndefinedSymbols = @(@{ symbol = 'missing'; provider = 'unknown' })
    Expect-Failure $data $LlvmBin 'Unknown anticipated backend dependency'
    $data = $original | ConvertFrom-Json
    $data.codegen.cpu = 'native'
    Expect-Failure $data $LlvmBin 'supported windows-x64-v1'
    if ($MismatchedLlvmBin) {
        Expect-Failure ($original | ConvertFrom-Json) $MismatchedLlvmBin 'must be LLVM 22.1.8'
    }
    Write-Output 'Manual-build integrity and toolchain rejection tests passed.'
}
finally {
    # Both paths are exact files derived from a fresh GUID alongside the explicit manifest.
    Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($testPath, '.build.json')) -Force -ErrorAction SilentlyContinue
}
