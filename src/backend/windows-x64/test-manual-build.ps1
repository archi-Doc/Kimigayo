[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$original = Get-Content -LiteralPath $manifestPath -Raw
$testPath = Join-Path (Split-Path -Parent $manifestPath) ('negative-' + [guid]::NewGuid().ToString('N') + '.link.json')
function Expect-Failure([object] $data, [string] $message) {
    $data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $testPath -Encoding utf8
    $failed = $false
    try { & (Join-Path $PSScriptRoot 'manual-build.ps1') -Manifest $testPath -Configuration $Configuration | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $message) { throw "Unexpected rejection: $($_.Exception.Message)" }
        $failed = $true
    }
    if (-not $failed) { throw "Expected rejection: $message" }
}
try {
    $data = $original | ConvertFrom-Json
    $data.schemaVersion = 3
    Expect-Failure $data "'schemaVersion' is not this compiler's"
    $data.schemaVersion = 4.5
    Expect-Failure $data "'schemaVersion' is not this compiler's"
    $data = $original | ConvertFrom-Json
    $data.externalDeclarations = @(@{ symbol = 'observer_notify'; dllimport = 'yes' })
    Expect-Failure $data "'externalDeclarations.dllimport' is missing or not a JSON Boolean"
    $data = $original | ConvertFrom-Json
    $data.PSObject.Properties.Remove('externalDeclarations')
    Expect-Failure $data "'externalDeclarations' is missing or not a JSON Array"
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kimi_backend') | Add-Member -NotePropertyName input -NotePropertyValue 'override.lib'
    Expect-Failure $data "'libraries' entry 'kimi_backend' is not this compiler's"
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kimi_backend').resolution = 'unknown'
    Expect-Failure $data "'libraries' entry 'kimi_backend' is not this compiler's"
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kernel32').definitionSha256 = '0' * 64
    Expect-Failure $data "'libraries' entry 'kernel32' is not this compiler's"
    $data = $original | ConvertFrom-Json
    ($data.libraries | Where-Object name -CEQ 'kernel32') | Add-Member -NotePropertyName input -NotePropertyValue 'kernel32.lib'
    Expect-Failure $data "'libraries' entry 'kernel32' is not this compiler's"
    $data = $original | ConvertFrom-Json
    $data.irSha256 = '0' * 64
    Expect-Failure $data 'IR/manifest SHA-256.*mismatch'
    $data = $original | ConvertFrom-Json
    $data.backendSupport.artifactSha256 = '0' * 64
    Expect-Failure $data "'backendSupport.artifactSha256' is not this compiler's"
    $data = $original | ConvertFrom-Json
    $data.backendSupport.packageVersion = 'unreviewed'
    Expect-Failure $data "'backendSupport.packageVersion' is not this compiler's"
    $data = $original | ConvertFrom-Json
    $data.codegen.cpu = 'native'
    Expect-Failure $data "'codegen.cpu' is not this compiler's"
    # A removed member (Tatara B6b5): a manifest an older emit wrote for a project's LlvmBin asks to re-emit.
    $data = $original | ConvertFrom-Json
    $data | Add-Member -NotePropertyName toolchain -NotePropertyValue @{ llvmBin = '../tools' }
    Expect-Failure $data "'toolchain.llvmBin' was removed: Kimi carries its toolchain. Re-emit the manifest."
    $data = $original | ConvertFrom-Json
    $data.optimization = 'O3'
    Expect-Failure $data 'supported Windows Application profile'
    Write-Output 'Manual-build integrity and toolchain rejection tests passed.'
}
finally {
    # Both paths are exact files derived from a fresh GUID alongside the explicit manifest.
    Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($testPath, '.build.json')) -Force -ErrorAction SilentlyContinue
}
