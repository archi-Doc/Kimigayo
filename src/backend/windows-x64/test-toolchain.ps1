# Policy tests: mock only the process boundary; exercise parsing, diagnostics and identities.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$profile = Read-KimiWindowsProfile
if (-not $profile.llvmVersion) { throw 'Missing catalog version' }
$expected = '99.7.3'
$script:report = @{}
function Invoke-KimiLlvmVersion([string] $ToolPath) { return $script:report }
function Assert-Failure([scriptblock] $Action, [string] $Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected failure: $Message"
}

foreach ($banner in @("LLVM version $expected", "clang version $expected (release)", "LLD $expected", "Vendor LLVM version $expected`n  Optimized build.")) {
    $script:report = @{ output = $banner; exitCode = 0 }
    $warnings = @()
    $identity = Get-KimiLlvmToolIdentity $PSCommandPath $expected -WarningVariable warnings
    if (-not $identity.versionMatched -or $identity.actualVersion -cne $expected -or $warnings.Count -ne 0 -or $identity.sha256.Length -ne 64) { throw "Matching banner failed: $banner" }
}
foreach ($actual in @('99.7.4', '99.6.3', '100.7.3', '99.7.3git', '99.7.3-rc1', '99.7.30', '99.7.3.1')) {
    $script:report = @{ output = "LLVM version $actual`nInstallation path mentions $expected"; exitCode = 0 }
    Assert-Failure { Get-KimiLlvmToolIdentity $PSCommandPath $expected } "actual=$actual"
    $warnings = @()
    $identity = Get-KimiLlvmToolIdentity $PSCommandPath $expected -AllowUnpinnedToolchain -WarningVariable warnings -WarningAction SilentlyContinue
    if ($identity.versionMatched -or $identity.actualVersion -cne $actual -or $warnings.Count -ne 1 -or
        $warnings[0].Message -notlike "*expected=$expected; actual=$actual*" -or $warnings[0].Message -notlike '*unverified*') { throw "Exploratory policy failed: $actual" }
}
foreach ($banner in @('', "Build path C:/llvm-$expected/bin", "LLVM version $expected`nclang version 98.0.0", 'LLVM version unknown')) {
    $script:report = @{ output = $banner; exitCode = 0 }
    Assert-Failure { Get-KimiLlvmToolIdentity $PSCommandPath $expected -AllowUnpinnedToolchain } 'Cannot obtain unambiguous LLVM version'
}
$script:report = @{ output = "LLVM version $expected"; exitCode = 1 }
Assert-Failure { Get-KimiLlvmToolIdentity $PSCommandPath $expected -AllowUnpinnedToolchain } 'exit=1'
Assert-Failure { Get-KimiLlvmToolIdentity (Join-Path $PSScriptRoot 'missing-tool.exe') $expected -AllowUnpinnedToolchain } 'Cannot obtain LLVM version'
Write-Output 'Toolchain policy tests passed (matching, mismatching, prerelease, warning, unreadable and failed probes).'
