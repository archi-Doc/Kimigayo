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

# Setup's and the backend builder's writes: Test-KimiSameFile (a missing or different destination is written, an equal one is not) and
# Move-KimiFile (one move over an existing destination, retried while another handle briefly holds it, bounded when the hold lasts).
Add-Type -TypeDefinition 'public static class KimiHold { public static void ReleaseAfter(System.IDisposable handle, int milliseconds) { System.Threading.Tasks.Task.Delay(milliseconds).ContinueWith(_ => handle.Dispose()); } }'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('kimi-toolchain-policy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
try {
    $source = Join-Path $directory 'source'
    $destination = Join-Path $directory 'destination'
    [IO.File]::WriteAllText($source, 'new')
    if (Test-KimiSameFile $source $destination) { throw 'A missing destination is the same file' }
    [IO.File]::WriteAllText($destination, 'old')
    if (Test-KimiSameFile $source $destination) { throw 'A different destination is the same file' }
    [IO.File]::WriteAllText($destination, 'new')
    if (-not (Test-KimiSameFile $source $destination)) { throw 'An equal destination is not the same file' }
    [IO.File]::WriteAllText($destination, 'old')
    $held = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    [KimiHold]::ReleaseAfter($held, 100)
    Move-KimiFile $source $destination
    if ((Test-Path -LiteralPath $source) -or [IO.File]::ReadAllText($destination) -cne 'new') { throw 'A briefly held destination was not replaced' }
    [IO.File]::WriteAllText($source, 'newer')
    $held = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Assert-Failure { Move-KimiFile $source $destination } 'denied' }
    finally { $held.Dispose() }
    if ([IO.File]::ReadAllText($destination) -cne 'new' -or [IO.File]::ReadAllText($source) -cne 'newer') { throw 'A failed move changed a file' }
}
finally { Remove-Item -LiteralPath $directory -Recurse -Force }
Write-Output 'Toolchain policy tests passed (matching, mismatching, prerelease, warning, unreadable and failed probes; same-file and replacing moves).'
