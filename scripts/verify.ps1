# Kimigayo verification entry point. Evidence goes to artifacts/verify/<timestamp>-<mode>[-<name>]/.
#
# Unit mode (per implementation unit): Kimi + tests Release build with warnings as errors, the selected xUnit
# classes/methods, then native O0/O2 execution of the fixtures those tests regenerated and the
# selected milestone harnesses.
#   ./scripts/verify.ps1 -Class XunitTest.ForeignEmissionTest -Fixtures 'ForeignPointer*.ll'
#   ./scripts/verify.ps1 -Class XunitTest.IterationAdapterTest -Fixtures 'IterationAdapter*.ll','LendingIterator*.ll'
#   ./scripts/verify.ps1 -Method XunitTest.ForeignEmissionTest.PointerSubplacesAccessOnlyTheirStoredParts
#   ./scripts/verify.ps1 -Milestone 18
#
# Diagnostic snapshot (docs/dev/DIAGNOSTICS.md §9.1): checks the corpus through the check entry, writes
# diagnostic-snapshot.json and, with a baseline, fails on differences of kinds not listed as allowed.
#   ./scripts/verify.ps1 -DiagnosticSnapshot
#   ./scripts/verify.ps1 -Class XunitTest.CheckServiceTest -DiagnosticSnapshot -DiagnosticBaseline artifacts/verify/<run>/diagnostic-snapshot.json -DiagnosticAllowed order,attribution
#
# Session mode (once at the end of a session): whole-solution Release build and full suite, then
# the selected native fixtures and milestone harnesses with the same compiler configuration.
#   ./scripts/verify.ps1 -Mode Session -Fixtures 'ForeignPointer*.ll' -Milestone 1,15,18
#
# Both modes use one compiler configuration. Select Debug explicitly when needed:
#   ./scripts/verify.ps1 -Configuration Debug -Class XunitTest.ForeignEmissionTest -Fixtures 'ForeignPointer*.ll'
#   ./scripts/verify.ps1 -Mode Session -Configuration Debug
# Native O0/O2 coverage is independent of the compiler configuration and is unchanged.
#
# Tests run up to -TestParallel collections at a time (default up to 4), respecting disabled
# parallelization on test classes. Use -TestParallel 1 for serial execution.
# Unit -TestPurpose Functional/Allocation narrows a focused check; Session always runs both.
# Native fixtures run up to -NativeParallel at a time (default up to 4), with individual logs.
# Milestone harnesses run -Parallel at a time (default up to 8), each in its own process, work
# directory and log; the steps are still recorded in the requested order.
#
# Never edit sources while this script runs; it records the commit and dirty state it verified.
[CmdletBinding()]
param(
    [ValidateSet('Unit', 'Session')] [string] $Mode = 'Unit',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string[]] $Class = @(),
    [string[]] $Method = @(),
    [ValidateSet('All', 'Functional', 'Allocation')] [string] $TestPurpose = 'All',
    # One or more fixture patterns; each runs as its own native step over the same fixture directory.
    [string[]] $Fixtures = @(),
    [int[]] $Milestone = @(),
    [switch] $DiagnosticSnapshot,
    [string] $DiagnosticBaseline = '',
    [string[]] $DiagnosticAllowed = @(),
    [string] $Name = '',
    [switch] $VerifyToolchain,
    # Milestone harnesses run concurrently, each in its own process with its own work directory and log.
    [ValidateRange(1, 2147483647)] [int] $Parallel = [Math]::Min(8, [Environment]::ProcessorCount),
    [ValidateRange(1, 2147483647)] [int] $NativeParallel = [Math]::Min(4, [Environment]::ProcessorCount),
    [ValidateRange(1, 2147483647)] [int] $TestParallel = [Math]::Min(4, [Environment]::ProcessorCount)
)
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'Session' -and $TestPurpose -ne 'All') { throw 'Session verification must include functional and allocation regressions (-TestPurpose All).' }
$repo = Split-Path -Parent $PSScriptRoot
$buildTarget = if ($Mode -eq 'Session') { 'Kimigayo.slnx' } else { 'tests/xUnitTest/xUnitTest.csproj' }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff')
$label = if ($Name) { "$stamp-$($Mode.ToLowerInvariant())-$Name" } else { "$stamp-$($Mode.ToLowerInvariant())" }
$evidence = Join-Path $repo "artifacts/verify/$label"
$work = Join-Path $repo "temp/verify/$label"
New-Item -ItemType Directory $evidence | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$failed = $false
$totalTimer = [Diagnostics.Stopwatch]::StartNew()

function Start-Step([string] $step) {
    Write-Host "RUN  $step"
    return [Diagnostics.Stopwatch]::StartNew()
}

function Add-Step([string] $step, [bool] $ok, [string] $detail, [double] $seconds) {
    $script:steps.Add([ordered]@{ step = $step; result = if ($ok) { 'PASS' } else { 'FAIL' }; detail = $detail; seconds = [Math]::Round($seconds, 3) })
    if (-not $ok) { $script:failed = $true }
    Write-Host ("{0,-4} {1} ({2:N1}s): {3}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $step, $seconds, $detail)
}

function Invoke-Script([scriptblock] $block, [string] $log) {
    # Harness scripts throw on failure; record that as a failed step instead of stopping.
    try { & $block *> $log; return $true }
    catch { $_ | Out-String | Add-Content -LiteralPath $log; return $false }
}

function Invoke-Build([string] $configuration) {
    $timer = Start-Step "build $configuration"
    $log = Join-Path $evidence "build-$configuration.log"
    # --no-incremental recompiles every project, so analyzers (StyleCop) report on sources another build left up to date.
    & dotnet build (Join-Path $repo $buildTarget) --no-restore --no-incremental -c $configuration --disable-build-servers -m:1 -warnaserror -p:EmitCompilerGeneratedFiles=false -v quiet *> $log
    $ok = $LASTEXITCODE -eq 0
    Add-Step "build $configuration" $ok $log $timer.Elapsed.TotalSeconds
    return $ok
}

function Test-LspDiscoveryAccess {
    $timer = Start-Step 'LSP discovery access'
    $log = Join-Path $evidence 'lsp-discovery-access.log'
    $ok = $true
    # Implicit-source fixtures live under the OS temp directory. SPEC 23.4.3 requires
    # listing every ancestor; an unreadable listing cannot mean "no project".
    $directory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    while ($directory) {
        try {
            $null = [IO.Directory]::GetFiles($directory, '*.kimiproj', [IO.SearchOption]::TopDirectoryOnly)
            "PASS $directory" | Add-Content -LiteralPath $log
        }
        catch {
            $ok = $false
            "FAIL ${directory}: $($_.Exception.GetBaseException().Message)" | Add-Content -LiteralPath $log
        }
        $directory = [IO.Path]::GetDirectoryName($directory)
    }
    $detail = if ($ok) { $log } else {
        "LSP tests require listing the temp directory and every ancestor. Run verification in a process with that access (outside a restricting sandbox). See $log"
    }
    Add-Step 'LSP discovery access' $ok $detail $timer.Elapsed.TotalSeconds
    return $ok
}

function Invoke-Tests([string] $configuration, [string[]] $filters, [string] $tag) {
    $timer = Start-Step "tests $configuration $tag (up to $TestParallel collections)"
    $log = Join-Path $evidence "tests-$configuration-$tag.log"
    $xml = Join-Path $evidence "tests-$configuration-$tag.xml"
    $dll = Join-Path $repo "tests/xUnitTest/bin/$configuration/net10.0/xUnitTest.dll"
    # Only fixtures generated by this configuration in this run are native evidence.
    $fixtureDirectory = Join-Path $evidence "fixtures-$configuration"
    New-Item -ItemType Directory $fixtureDirectory | Out-Null
    $previousDirectory = $env:KIMI_FIXTURE_DIRECTORY
    try {
        $env:KIMI_FIXTURE_DIRECTORY = $fixtureDirectory
        $parallelMode = if ($TestParallel -eq 1) { 'none' } else { 'collections' }
        & dotnet $dll -parallelMode $parallelMode -maxThreads $TestParallel -failSkips -result-xml $xml @filters *> $log
        $code = $LASTEXITCODE
    }
    finally { $env:KIMI_FIXTURE_DIRECTORY = $previousDirectory }
    $summary = Select-String -LiteralPath $log -Pattern 'Total: (\d+), Errors: (\d+), Failed: (\d+)' | Select-Object -Last 1
    $total = if ($summary) { [int]$summary.Matches[0].Groups[1].Value } else { 0 }
    # Zero executed tests is never evidence (for example, a mistyped filter).
    Add-Step "tests $configuration $tag" ($code -eq 0 -and $total -gt 0) "$(if ($summary) { $summary.Line.Trim() } else { 'no summary' }); $log" $timer.Elapsed.TotalSeconds
}

$head = (& git -C $repo rev-parse --short HEAD).Trim()
$dirty = [bool](& git -C $repo status --porcelain --untracked-files=no)
$filters = @()
foreach ($c in $Class) {
    $filters += @('-class', $c)
    # A trailing wildcard already includes nested classes; appending would put it in the middle.
    if (-not $c.EndsWith('*') -and -not $c.EndsWith('+AllocationTests')) { $filters += @('-class', "$c+AllocationTests") }
}
foreach ($m in $Method) {
    $filters += @('-method', $m)
    # Preserve selections after allocation tests move into a nested, isolated class.
    $separator = $m.LastIndexOf('.')
    if ($separator -ge 0) { $filters += @('-method', ($m.Insert($separator, '+AllocationTests'))) }
}
if ($Mode -eq 'Session') { $filters = @() }
$hasTestSelection = $filters.Count -gt 0
if ($TestPurpose -eq 'Functional') { $filters += @('-trait-', 'Purpose=Allocation') }
elseif ($TestPurpose -eq 'Allocation') { $filters += @('-trait', 'Purpose=Allocation') }

# Check only selections that can include the implicit-source LSP fixtures. Method
# filters use their declaring-class pattern; unqualified patterns may match any class.
$lspClasses = @('XunitTest.CheckSchedulerTest', 'XunitTest.LspProtocolTest', 'XunitTest.LspProjectDiagnosticTest', 'XunitTest.LspProcessTest')
$classPatterns = @($Class) + @($Method | ForEach-Object {
    if ($_.Contains('.')) { $_.Substring(0, $_.LastIndexOf('.')) } else { '*' }
})
$needsLspAccess = $Mode -eq 'Session' -or (-not $hasTestSelection -and $TestPurpose -ne 'All')
foreach ($pattern in $classPatterns) {
    foreach ($testClass in $lspClasses) {
        if ($testClass -like $pattern) { $needsLspAccess = $true }
    }
}
if ($needsLspAccess) { $null = Test-LspDiscoveryAccess }

$toolchainVerification = 'not-performed'
if (-not $failed -and (Invoke-Build $Configuration)) {
    if ($VerifyToolchain) {
        $timer = Start-Step 'toolchain verify'
        $log = Join-Path $evidence 'toolchain.log'
        & dotnet (Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll") toolchain verify --Report (Join-Path $evidence 'toolchain.json') *> $log
        $ok = $LASTEXITCODE -eq 0
        $toolchainVerification = if ($ok) { 'passed' } else { 'failed' }
        Add-Step 'toolchain verify' $ok $log $timer.Elapsed.TotalSeconds
    }
}
if (-not $failed) {
    if ($Mode -eq 'Session') { Invoke-Tests $Configuration @() 'full' }
    elseif ($hasTestSelection -or $TestPurpose -ne 'All') { Invoke-Tests $Configuration $filters 'focused' }
}

if (-not $failed -and $DiagnosticSnapshot) {
    $timer = Start-Step "diagnostic snapshot $Configuration"
    $log = Join-Path $evidence "diagnostic-snapshot-$Configuration.log"
    $snapshot = Join-Path $evidence 'diagnostic-snapshot.json'
    $dll = Join-Path $repo "tests/xUnitTest/bin/$Configuration/net10.0/xUnitTest.dll"
    $saved = @($env:KIMI_DIAGNOSTIC_SNAPSHOT, $env:KIMI_DIAGNOSTIC_BASELINE, $env:KIMI_DIAGNOSTIC_ALLOWED)
    try {
        $env:KIMI_DIAGNOSTIC_SNAPSHOT = $snapshot
        $env:KIMI_DIAGNOSTIC_BASELINE = if ($DiagnosticBaseline) { (Resolve-Path -LiteralPath $DiagnosticBaseline).Path } else { '' }
        $env:KIMI_DIAGNOSTIC_ALLOWED = $DiagnosticAllowed -join ','
        & dotnet $dll -method 'XunitTest.DiagnosticSnapshotTest.RequestedSnapshotMatchesTheBaseline' -failSkips *> $log
        $code = $LASTEXITCODE
    }
    finally { $env:KIMI_DIAGNOSTIC_SNAPSHOT, $env:KIMI_DIAGNOSTIC_BASELINE, $env:KIMI_DIAGNOSTIC_ALLOWED = $saved }
    $differences = [IO.Path]::ChangeExtension($snapshot, '.differences.txt')
    $detail = if (Test-Path -LiteralPath $differences) { "$(@(Get-Content -LiteralPath $differences).Count) differences; $differences" } else { $snapshot }
    Add-Step "diagnostic snapshot $Configuration" ($code -eq 0 -and (Test-Path -LiteralPath $snapshot)) "$detail; $log" $timer.Elapsed.TotalSeconds
}

$native = $Configuration
if (-not $failed -and $Fixtures.Count -gt 0) {
    # Fixtures come from the tests run above; run them only when those tests passed.
    $fixtureDirectory = Join-Path $evidence "fixtures-$native"
    $hashes = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $Fixtures.Count; $i++) {
        $pattern = $Fixtures[$i]
        $timer = Start-Step "native $pattern"
        $suffix = if ($Fixtures.Count -eq 1) { '' } else { "-$i" }
        $log = Join-Path $evidence "native$suffix.log"
        $ok = Invoke-Script { & (Join-Path $repo 'src/backend/windows-x64/test-scalars.ps1') -FixturePattern $pattern -FixtureDirectory $fixtureDirectory -OutputDirectory (Join-Path $work "native$suffix") -LogDirectory (Join-Path $evidence "native$suffix-fixtures") -Parallel $NativeParallel } $log
        $line = Select-String -LiteralPath $log -Pattern 'Passed [1-9]\d* native' | Select-Object -Last 1
        Add-Step "native $pattern" ($ok -and $null -ne $line) "$(if ($line) { $line.Line.Trim() } else { 'see log' }); $log" $timer.Elapsed.TotalSeconds
        if (Test-Path -LiteralPath $fixtureDirectory) {
            Get-ChildItem -LiteralPath $fixtureDirectory -Filter $pattern -File | Get-FileHash | ForEach-Object { $hashes.Add("$($_.Hash),$([IO.Path]::GetFileName($_.Path))") }
        }
    }

    if ($hashes.Count -gt 0) { $hashes | Sort-Object -Unique | Set-Content (Join-Path $evidence 'fixture-hashes.csv') }
}

if (-not $failed -and $Milestone.Count -gt 0) {
    # Harness scripts throw on failure; each job records PASS/FAIL after writing its own log.
    $jobs = [ordered]@{}
    foreach ($number in $Milestone) {
        while (@($jobs.Values | Where-Object { $_.State -eq 'Running' }).Count -ge $Parallel) { Start-Sleep -Milliseconds 500 }
        Write-Host "RUN  milestone $number ($native)"
        $log = Join-Path $evidence "milestone$number-$native.log"
        $script = Join-Path $repo "src/backend/windows-x64/test-milestone$number.ps1"
        $jobs["$number"] = Start-Job -Name "milestone$number" -ArgumentList $script, $native, $log -ScriptBlock {
            param($script, $configuration, $log)
            $ErrorActionPreference = 'Stop'
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $ok = $true
            try { & $script -Configuration $configuration *> $log }
            catch { $_ | Out-String | Add-Content -LiteralPath $log; $ok = $false }
            [pscustomobject]@{ ok = $ok; seconds = $timer.Elapsed.TotalSeconds }
        }
    }

    foreach ($number in $Milestone) {
        $job = $jobs["$number"]
        $result = Receive-Job -Job $job -Wait
        Remove-Job -Job $job
        Add-Step "milestone $number ($native)" ($result.ok -eq $true) (Join-Path $evidence "milestone$number-$native.log") $result.seconds
    }
}

[ordered]@{ mode = $Mode; configuration = $Configuration; buildTarget = $buildTarget; testPurpose = $TestPurpose; head = $head; dirty = $dirty; started = $stamp; workDirectory = $work; toolchainVerification = $toolchainVerification; testParallel = $TestParallel; nativeParallel = $NativeParallel; milestoneParallel = $Parallel; seconds = [Math]::Round($totalTimer.Elapsed.TotalSeconds, 3); steps = $steps } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'summary.json')
Write-Host "Evidence: $evidence (HEAD $head$(if ($dirty) { ', uncommitted changes' }))"
if ($failed) { exit 1 }
