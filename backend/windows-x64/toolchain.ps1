# Shared by the native builders; loading this file never executes LLVM.
function Resolve-KimiToolchainRoot([string] $ToolchainRoot = '') {
    if (-not $ToolchainRoot) { $ToolchainRoot = [Environment]::GetEnvironmentVariable('KIMI_TOOLCHAIN_ROOT') }
    if (-not $ToolchainRoot) { $ToolchainRoot = Join-Path $PSScriptRoot '../../toolchain' }
    if ([string]::IsNullOrWhiteSpace($ToolchainRoot) -or $ToolchainRoot -match '[\x00\r\n"]' -or $ToolchainRoot.StartsWith('-')) { throw 'Invalid toolchain root' }
    return [IO.Path]::GetFullPath($ToolchainRoot, (Get-Location).ProviderPath)
}

function Resolve-KimiBackendLibrary($Entry, [string] $ToolchainRoot, [string] $ManifestDirectory) {
    if ($Entry.PSObject.Properties['resolution']) {
        if ($Entry.resolution -cne 'toolchain' -or $Entry.PSObject.Properties['input']) { throw 'Invalid backend toolchain resolution: an input path is not permitted.' }
        return Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
    }
    if ([string]::IsNullOrWhiteSpace($Entry.input) -or $Entry.input -match '[\x00\r\n"]' -or $Entry.input.StartsWith('-')) { throw 'Invalid backend input path' }
    return [IO.Path]::GetFullPath($Entry.input, $ManifestDirectory)
}

function Read-KimiWindowsProfile {
    $profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'profile.json') -Raw | ConvertFrom-Json
    [xml] $props = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../Directory.Build.props') -Raw
    $version = $props.SelectSingleNode('/*[local-name()="Project"]/*[local-name()="PropertyGroup"]/*[local-name()="Version"]').InnerText
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'Missing Version in Directory.Build.props' }
    $profile | Add-Member -NotePropertyName packageVersion -NotePropertyValue $version -Force
    if ($profile.profile -cne 'windows-x64-v1' -or $profile.llvmVersion -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'Invalid Windows LLVM profile: expected a major.minor.patch LLVM version'
    }
    return $profile
}

function Invoke-KimiLlvmVersion([string] $ToolPath) {
    $output = & $ToolPath --version 2>&1 | Out-String
    return @{ output = $output.Trim(); exitCode = $LASTEXITCODE }
}

function Get-KimiLlvmToolIdentity {
    [CmdletBinding()]
    param([string] $ToolPath, [string] $ExpectedVersion, [switch] $AllowUnpinnedToolchain)
    try {
        $resolved = (Resolve-Path -LiteralPath $ToolPath -ErrorAction Stop).Path
        $report = Invoke-KimiLlvmVersion $resolved
    }
    catch {
        throw "Cannot obtain LLVM version: tool=$ToolPath; expected=$ExpectedVersion; $($_.Exception.Message)"
    }
    if ($report.exitCode -ne 0) {
        throw "Cannot obtain LLVM version: tool=$resolved; expected=$ExpectedVersion; exit=$($report.exitCode); output=$($report.output)"
    }
    # Accept tool version banners, not a matching number elsewhere in the output.
    # Keep suffixes (e.g. 22.1.8git) so prereleases cannot certify a release profile.
    $versions = @([regex]::Matches($report.output, '(?im)^\s*(?:.*\b(?:LLVM|clang) version|LLD)\s+([0-9]+\.[0-9]+\.[0-9]+[^\s()]*)') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($versions.Count -ne 1) {
        throw "Cannot obtain unambiguous LLVM version: tool=$resolved; expected=$ExpectedVersion; output=$($report.output)"
    }
    $actual = $versions[0]
    $matched = $actual -ceq $ExpectedVersion
    if (-not $matched) {
        $message = "LLVM version mismatch: tool=$resolved; expected=$ExpectedVersion; actual=$actual."
        if (-not $AllowUnpinnedToolchain) { throw "$message Use -AllowUnpinnedToolchain only for exploratory builds." }
        Write-Warning "$message Continuing with an unverified toolchain; this result does not validate the pinned profile."
    }
    return @{ path = $resolved; version = $report.output; actualVersion = $actual; expectedVersion = $ExpectedVersion; versionMatched = $matched; sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant() }
}
