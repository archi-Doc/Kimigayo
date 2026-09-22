# Kimigayo verification entry point. Evidence goes to bin/verify/<timestamp>-<mode>[-<name>]/.
#
# Unit mode (per implementation unit): Debug build with warnings as errors, the selected xUnit
# classes/methods, then native O0/O2 execution of the fixtures those tests regenerated and the
# selected milestone harnesses.
#   ./verify.ps1 -Class XunitTest.ForeignEmissionTest -Fixtures 'ForeignPointer*.ll'
#   ./verify.ps1 -Method XunitTest.ForeignEmissionTest.PointerSubplacesAccessOnlyTheirStoredParts
#   ./verify.ps1 -Milestone 18
#
# Session mode (once at the end of a session): Debug and Release builds and full suites, then
# the selected native fixtures and milestone harnesses with the Release compiler.
#   ./verify.ps1 -Mode Session -Fixtures 'ForeignPointer*.ll' -Milestone 1,15,18
#
# Never edit sources while this script runs; it records the commit and dirty state it verified.
[CmdletBinding()]
param(
    [ValidateSet('Unit', 'Session')] [string] $Mode = 'Unit',
    [string[]] $Class = @(),
    [string[]] $Method = @(),
    [string] $Fixtures = '',
    [int[]] $Milestone = @(),
    [string] $Name = ''
)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$label = if ($Name) { "$stamp-$($Mode.ToLowerInvariant())-$Name" } else { "$stamp-$($Mode.ToLowerInvariant())" }
$evidence = Join-Path $repo "bin/verify/$label"
New-Item -ItemType Directory -Force $evidence | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$failed = $false

function Add-Step([string] $step, [bool] $ok, [string] $detail) {
    $script:steps.Add([ordered]@{ step = $step; result = if ($ok) { 'PASS' } else { 'FAIL' }; detail = $detail })
    if (-not $ok) { $script:failed = $true }
    Write-Host ("{0,-4} {1}: {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $step, $detail)
}

function Invoke-Script([scriptblock] $block, [string] $log) {
    # Harness scripts throw on failure; record that as a failed step instead of stopping.
    try { & $block *> $log; return $true }
    catch { $_ | Out-String | Add-Content -LiteralPath $log; return $false }
}

function Invoke-Build([string] $configuration) {
    $log = Join-Path $evidence "build-$configuration.log"
    & dotnet build (Join-Path $repo 'Kimigayo.slnx') --no-restore -c $configuration --disable-build-servers -m:1 -warnaserror -p:EmitCompilerGeneratedFiles=false -v quiet *> $log
    Add-Step "build $configuration" ($LASTEXITCODE -eq 0) $log
    return $LASTEXITCODE -eq 0
}

function Invoke-Tests([string] $configuration, [string[]] $filters, [string] $tag) {
    $log = Join-Path $evidence "tests-$configuration-$tag.log"
    $xml = Join-Path $evidence "tests-$configuration-$tag.xml"
    $dll = Join-Path $repo "xUnitTest/bin/$configuration/net10.0/xUnitTest.dll"
    & dotnet $dll -parallelMode none -failSkips -result-xml $xml @filters *> $log
    $code = $LASTEXITCODE
    $summary = Select-String -LiteralPath $log -Pattern 'Total: (\d+), Errors: (\d+), Failed: (\d+)' | Select-Object -Last 1
    $total = if ($summary) { [int]$summary.Matches[0].Groups[1].Value } else { 0 }
    # Zero executed tests is never evidence (for example, a mistyped filter).
    Add-Step "tests $configuration $tag" ($code -eq 0 -and $total -gt 0) "$(if ($summary) { $summary.Line.Trim() } else { 'no summary' }); $log"
}

$head = (& git -C $repo rev-parse --short HEAD).Trim()
$dirty = [bool](& git -C $repo status --porcelain --untracked-files=no)
$configurations = if ($Mode -eq 'Session') { @('Debug', 'Release') } else { @('Debug') }
$filters = @()
foreach ($c in $Class) { $filters += @('-class', $c) }
foreach ($m in $Method) { $filters += @('-method', $m) }
if ($Mode -eq 'Session') { $filters = @() }

foreach ($configuration in $configurations) {
    if (-not (Invoke-Build $configuration)) { continue }
    if ($Mode -eq 'Session') { Invoke-Tests $configuration @() 'full' }
    elseif ($filters.Count -gt 0) { Invoke-Tests $configuration $filters 'focused' }
}

$native = if ($Mode -eq 'Session') { 'Release' } else { 'Debug' }
if (-not $failed -and $Fixtures) {
    # Fixtures come from the tests run above; run them only when those tests passed.
    $log = Join-Path $evidence 'native.log'
    $ok = Invoke-Script { & (Join-Path $repo 'backend/windows-x64/test-scalars.ps1') -FixturePattern $Fixtures -OutputDirectory (Join-Path $evidence 'native') } $log
    $line = Select-String -LiteralPath $log -Pattern 'Passed \d+ native' | Select-Object -Last 1
    Add-Step "native $Fixtures" ($ok -and $null -ne $line) "$(if ($line) { $line.Line.Trim() } else { 'see log' }); $log"
    Get-FileHash (Join-Path $repo "bin/scalar-fixtures/$Fixtures") | ForEach-Object { "$($_.Hash),$([IO.Path]::GetFileName($_.Path))" } |
        Set-Content (Join-Path $evidence 'fixture-hashes.csv')
}

foreach ($number in $Milestone) {
    if ($failed) { break }
    $log = Join-Path $evidence "milestone$number-$native.log"
    $ok = Invoke-Script { & (Join-Path $repo "backend/windows-x64/test-milestone$number.ps1") -Configuration $native } $log
    Add-Step "milestone $number ($native)" $ok $log
}

[ordered]@{ mode = $Mode; head = $head; dirty = $dirty; started = $stamp; steps = $steps } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'summary.json')
Write-Host "Evidence: $evidence (HEAD $head$(if ($dirty) { ', uncommitted changes' }))"
if ($failed) { exit 1 }
