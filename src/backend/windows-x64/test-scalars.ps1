[CmdletBinding()]
param(
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
    [string] $FixturePattern = '*.ll',
    [string] $FixtureDirectory = '',
    [string] $OutputDirectory = '',
    [string] $LogDirectory = '',
    [ValidateRange(1, 2147483647)] [int] $Parallel = [Math]::Min(4, [Environment]::ProcessorCount)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$fixtures = if ($FixtureDirectory) { (Resolve-Path -LiteralPath $FixtureDirectory).Path } else { Join-Path $repo 'temp/scalar-fixtures' }
$selectedFixtures = @(Get-ChildItem -LiteralPath $fixtures -Filter $FixturePattern -File | Where-Object Extension -EQ '.ll' | Sort-Object Name)
if ($selectedFixtures.Count -eq 0) { throw "No scalar fixtures match '$FixturePattern' in '$fixtures'." }
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).ProviderPath) } else { Join-Path $repo 'temp/scalar-native' }
New-Item -ItemType Directory -Force $out | Out-Null
$tools = @{}
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-dlltool', 'llvm-readobj', 'llvm-nm')) { $tools[$name] = Join-Path $LlvmBin "$name.exe" }
$profile = Read-KimiWindowsProfile
$kernel = Get-KimiInstalledKernel32 $ToolchainRoot
$archive = Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Installed backend is missing. Run setup.ps1.' }
Write-Output 'Toolchain verification: not-performed'
$allowedSymbols = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in $profile.providedSymbols) { $null = $allowedSymbols.Add($symbol) }
foreach ($line in ((Get-KimiKernel32Definition) -split "`n" | Select-Object -Skip 2)) {
    $symbol = $line.Trim()
    if ($symbol) { $null = $allowedSymbols.Add($symbol); $null = $allowedSymbols.Add("__imp_$symbol") }
}
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $out 'logs' }
New-Item -ItemType Directory -Force $logs | Out-Null
$context = @{ tools = $tools; fixtures = $fixtures; output = $out; kernel = $kernel; archive = $archive; allowedSymbols = [string[]]@($allowedSymbols); logs = $logs }
$worker = Join-Path $PSScriptRoot 'test-scalar-fixture.ps1'
if ($Parallel -eq 1) {
    $results = @($selectedFixtures | ForEach-Object { & $worker -Fixture $_ -Context $context })
}
else {
    $results = @($selectedFixtures | ForEach-Object -Parallel {
        & $using:worker -Fixture $_ -Context $using:context
    } -ThrottleLimit $Parallel)
}
$results = @($results | Sort-Object fixture)
$results | ConvertTo-Json -Depth 4 -AsArray | Set-Content -LiteralPath (Join-Path $logs 'results.json')
$runs = 0
$failures = @($results | Where-Object { -not $_.ok -or ($_.levels -join ',') -cne 'O0,O2' })
foreach ($result in $results) {
    $runs += $result.levels.Count
    Write-Output "$($result.fixture): $(if ($result.ok) { 'PASS' } else { 'FAIL' }); $($result.log)"
}
if ($results.Count -ne $selectedFixtures.Count -or $failures.Count -gt 0) { throw "Native fixture verification failed; see $logs" }
Write-Output "Passed $runs native scalar executions (O0/O2)."
