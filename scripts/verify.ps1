# Kimigayo verification entry point. Evidence goes to artifacts/verify/<timestamp>-<mode>[-<name>]/.
#
# Unit mode (per implementation unit): Kimi + tests Release build with warnings as errors, the selected xUnit
# classes/methods joined with the common compiler checks (ArchitectureRulesTest),
# then native O0/O2 execution of the fixtures those tests regenerated and the selected milestone harnesses.
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
# -Pipeline mir (R3-R6) runs every compiler process of the run with KIMI_PIPELINE=mir; the default is legacy. Since G2 it publishes what the
# default does (DECISIONS R5-M156), so its run is the difference ledger's (KIMI_MIR_DIFF).
#   ./scripts/verify.ps1 -Class XunitTest.PipelineSwitchTest -Pipeline mir
# -Codegen mir (R5, until G4) sets KIMI_CODEGEN=mir, MIR code generation, on either pipeline (DECISIONS R5-M156); the default is legacy.
# With -Pipeline mir it is "sel".
# Its tests print theory arguments in full, and a -Codegen mir run that wrote test results ends with the pending ledger gate
# (codegen-pending.ps1): codegen-pending.json and the summary.json field codegenPending. Its test and native steps leave
# their failing cases and fixtures to the gate, which passes when they equal the ledger; -UpdatePending then writes the ledger.
#   ./scripts/verify.ps1 -Class XunitTest.PipelineSwitchTest -Pipeline mir -Codegen mir [-UpdatePending]
#
# Tests run up to -TestParallel collections at a time (default up to 8), respecting disabled
# parallelization on test collections. Use -TestParallel 1 for serial execution.
# Unit -TestPurpose Functional/Allocation narrows a focused check; Session always runs both.
# Native fixtures run up to -NativeParallel at a time (default up to 4), with individual logs.
# Milestone harnesses run -Parallel at a time (default up to 8), each in its own process, work
# directory and log; the steps are still recorded in the requested order.
#
# Never edit sources while this script runs; manifests detect changes and identify the actual inputs.
[CmdletBinding()]
param(
    [ValidateSet('Unit', 'Session')] [string] $Mode = 'Unit',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('legacy', 'mir')] [string] $Pipeline = 'legacy',
    [ValidateSet('legacy', 'mir')] [string] $Codegen = 'legacy',
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
    [switch] $UpdatePending,
    # Milestone harnesses run concurrently, each in its own process with its own work directory and log.
    [ValidateRange(1, 2147483647)] [int] $Parallel = [Math]::Min(8, [Environment]::ProcessorCount),
    [ValidateRange(1, 2147483647)] [int] $NativeParallel = [Math]::Min(4, [Environment]::ProcessorCount),
    [ValidateRange(1, 2147483647)] [int] $TestParallel = [Math]::Min(8, [Environment]::ProcessorCount)
)
$ErrorActionPreference = 'Stop'
if ($Mode -eq 'Session' -and $TestPurpose -ne 'All') { throw 'Session verification must include functional and allocation regressions (-TestPurpose All).' }
if ($Mode -eq 'Session' -and $Class.Count + $Method.Count -gt 0) { throw 'Session verification runs every test; -Class and -Method select Unit runs.' }
if ($UpdatePending -and $Codegen -ne 'mir') { throw '-UpdatePending writes the pending ledger of a -Codegen mir run.' }
. (Join-Path $PSScriptRoot 'verification-selection.ps1')
. (Join-Path $PSScriptRoot 'verification-inputs.ps1')
. (Join-Path $PSScriptRoot 'verification-native.ps1')
. (Join-Path $PSScriptRoot 'codegen-pending.ps1')
$requestedClass = @($Class | ForEach-Object { ConvertTo-KimiTestPattern $_ })
$requestedMethod = @($Method | ForEach-Object { ConvertTo-KimiTestPattern $_ -Method })
$Class = @(Join-KimiCommonTestSelections $requestedClass)
$Method = @(Join-KimiCommonTestSelections $requestedMethod -Method)
$Mode = if ($Mode -eq 'Session') { 'Session' } else { 'Unit' } # The steps and codegen-pending.ps1 compare the canonical spelling.
$Pipeline = $Pipeline.ToLowerInvariant()
$Codegen = $Codegen.ToLowerInvariant()
$previousPipeline = $env:KIMI_PIPELINE
$previousCodegen = $env:KIMI_CODEGEN
$env:KIMI_PIPELINE = $Pipeline # Tests, fixtures and milestone harnesses inherit it.
$env:KIMI_CODEGEN = $Codegen
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

function Start-ScriptJob([string] $step, [string] $script, [string] $log, [hashtable] $parameters) {
    # Harness scripts throw on failure; the job writes its own log and Complete-ScriptJob records PASS/FAIL.
    Write-Host "RUN  $step"
    $job = Start-Job -ArgumentList $script, $log, $parameters -ScriptBlock {
        param($script, $log, $parameters)
        $ErrorActionPreference = 'Stop'
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $ok = $true
        try { & $script @parameters *> $log }
        catch { $_ | Out-String | Add-Content -LiteralPath $log; $ok = $false }
        [pscustomobject]@{ ok = $ok; seconds = $timer.Elapsed.TotalSeconds }
    }
    return [pscustomobject]@{ step = $step; log = $log; job = $job }
}

function Complete-ScriptJob($run) {
    $result = Receive-Job -Job $run.job -Wait
    Remove-Job -Job $run.job
    Add-Step $run.step ($result.ok -eq $true) $run.log $result.seconds
}

function Invoke-Build([string] $configuration) {
    $timer = Start-Step "build $configuration"
    $log = Join-Path $evidence "build-$configuration.log"
    # --no-incremental recompiles every project, so analyzers (StyleCop) report on sources another build left up to date.
    & dotnet build (Join-Path $repo $buildTarget) --no-restore --no-incremental -c $configuration -warnaserror -p:EmitCompilerGeneratedFiles=false -v quiet *> $log
    Add-Step "build $configuration" ($LASTEXITCODE -eq 0) $log $timer.Elapsed.TotalSeconds
}

function Test-LspDiscoveryAccess {
    $timer = Start-Step 'LSP discovery access'
    $log = Join-Path $evidence 'lsp-discovery-access.log'
    $ok = $true
    # Implicit-source fixtures live under the OS temp directory. SPEC 23.4 requires
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
        # The pending ledger names a sel case by its full name (R5-M66), so xUnit prints its arguments uncut.
        $names = if ($Codegen -eq 'mir') { @('-printMaxStringLength', '0', '-printMaxEnumerableLength', '0') } else { @() }
        & dotnet $dll -parallelMode $parallelMode -maxThreads $TestParallel -failSkips -result-xml $xml @names @filters *> $log
        $code = $LASTEXITCODE
    }
    finally { $env:KIMI_FIXTURE_DIRECTORY = $previousDirectory }
    $summary = Select-String -LiteralPath $log -Pattern 'Total: \d+' | Select-Object -Last 1
    $line = if ($summary) { $summary.Line.Trim() } else { 'no summary' }
    $ok = $code -eq 0
    $detail = "$line; $log"
    try {
        if ($Codegen -eq 'mir') {
            # Under sel the pending gate judges the failing cases, so the step fails on a run error its results record instead.
            $detail = "$line; failing cases judged by codegen pending; $log"
            $ok = (Test-Path -LiteralPath $xml) -and @(Select-Xml -LiteralPath $xml -XPath '//assembly[@errors!="0"]').Count -eq 0
        }
        # Zero executed tests is never evidence, and each requested selector executes a test of its own, so one that matches
        # only Explicit tests is not hidden by the common checks or another selector.
        $executed = @(Get-KimiExecutedTests $xml)
        if ($executed.Count -eq 0) { throw 'The results record no executed test.' }
        Assert-KimiTestSelections $requestedClass $requestedMethod $executed 'among the executed tests'
    }
    catch { $ok = $false; $detail = "$($_.Exception.Message) $detail" }
    Add-Step "tests $configuration $tag" $ok $detail $timer.Elapsed.TotalSeconds
}

function Test-Selection([string] $configuration, [string[]] $filters) {
    $timer = Start-Step 'test selection'
    $dll = Join-Path $repo "tests/xUnitTest/bin/$configuration/net10.0/xUnitTest.dll"
    $catalog = Join-Path $evidence 'discovered-methods.json'
    $selected = Join-Path $evidence 'selected-methods.json'
    try {
        & dotnet $dll -noLogo -list methods/json > $catalog
        if ($LASTEXITCODE -ne 0) { throw "Test discovery failed; see $catalog" }
        $methods = @(Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json)
        Assert-KimiTestSelections $Class $Method $methods 'in the freshly built assembly'
        # The runner applies the actual class/method intersection and trait rules.
        & dotnet $dll -noLogo -list methods/json @filters > $selected
        if ($LASTEXITCODE -ne 0) { throw "Selected test discovery failed; see $selected" }
        $count = @(Get-Content -LiteralPath $selected -Raw | ConvertFrom-Json).Count
        if ($count -eq 0) { throw 'No test methods match the combined class, method and purpose filters.' }
        Add-Step 'test selection' $true "$count selected methods; $selected" $timer.Elapsed.TotalSeconds
    }
    catch { Add-Step 'test selection' $false $_.Exception.Message $timer.Elapsed.TotalSeconds }
}

$head = (& git -C $repo rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repo status --porcelain --untracked-files=all)
$inputStability = 'not-performed'
$beforeInputs = $null
$timer = Start-Step 'input manifest'
try {
    $beforeInputs = Get-KimiVerificationInputs $repo
    $beforeInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'inputs-before.json')
    Add-Step 'input manifest' $true "$($beforeInputs.files.Count) source/configuration files, $($beforeInputs.dependencies.Count) NuGet inputs" $timer.Elapsed.TotalSeconds
}
catch { Add-Step 'input manifest' $false $_.Exception.Message $timer.Elapsed.TotalSeconds }
$scriptRuns = @()
if (-not $failed) {
    # The script regressions need no compiler build; they run while it compiles.
    $scriptRuns = @('VerificationSelectionTest', 'VerificationInputsTest' | ForEach-Object {
        Start-ScriptJob $_ (Join-Path $repo "tests/scripts/$_.ps1") (Join-Path $evidence "$_.log") @{}
    })
}
$filters = @()
foreach ($c in $Class) {
    foreach ($pattern in (Get-KimiTestAlternatives $c)) { $filters += @('-class', $pattern) }
}
foreach ($m in $Method) {
    foreach ($pattern in (Get-KimiTestAlternatives $m -Method)) { $filters += @('-method', $pattern) }
}
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

if (-not $failed) { Invoke-Build $Configuration }
foreach ($run in $scriptRuns) { Complete-ScriptJob $run }
$toolchainVerification = 'not-performed'
if (-not $failed -and $VerifyToolchain) {
    $timer = Start-Step 'toolchain verify'
    $log = Join-Path $evidence 'toolchain.log'
    & dotnet (Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll") toolchain verify --Report (Join-Path $evidence 'toolchain.json') *> $log
    $ok = $LASTEXITCODE -eq 0
    $toolchainVerification = if ($ok) { 'passed' } else { 'failed' }
    Add-Step 'toolchain verify' $ok $log $timer.Elapsed.TotalSeconds
}
$runsTests = $Mode -eq 'Session' -or $hasTestSelection -or $TestPurpose -ne 'All'
# The pending gate compares a sel run's results with the methods it selected, every method in a Session.
if (-not $failed -and $runsTests -and ($Mode -eq 'Unit' -or $Codegen -eq 'mir')) { Test-Selection $Configuration $filters }
if (-not $failed -and $runsTests) { Invoke-Tests $Configuration $filters $(if ($Mode -eq 'Session') { 'full' } else { 'focused' }) }

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
        $result = Get-KimiNativeLogResult $log
        $ok = $ok -and $result.hasSuccessLine
        $detail = $result.detail
        $matched = @(if (Test-Path -LiteralPath $fixtureDirectory) { Get-ChildItem -LiteralPath $fixtureDirectory -Filter $pattern -File | Where-Object Extension -EQ '.ll' })
        if ($Codegen -eq 'mir') {
            # Under sel the pending gate judges the fixtures' outcomes; the step needs a result for every fixture it selected.
            $records = Join-Path $evidence "native$suffix-fixtures/results.json"
            $result = Get-KimiNativePendingResult $records $matched.Count $log
            $ok = $result.passed
            $detail = $result.detail
        }
        Add-Step "native $pattern" $ok $detail $timer.Elapsed.TotalSeconds
        $matched | Get-FileHash | ForEach-Object { $hashes.Add("$($_.Hash),$([IO.Path]::GetFileName($_.Path))") }
    }

    if ($hashes.Count -gt 0) { $hashes | Sort-Object -Unique | Set-Content (Join-Path $evidence 'fixture-hashes.csv') }
}

if (-not $failed -and $Milestone.Count -gt 0) {
    $runs = [Collections.Generic.List[object]]::new()
    foreach ($number in $Milestone) {
        while (@($runs | Where-Object { $_.job.State -eq 'Running' }).Count -ge $Parallel) { Start-Sleep -Milliseconds 500 }
        $runs.Add((Start-ScriptJob "milestone $number ($native)" (Join-Path $repo "src/backend/windows-x64/test-milestone$number.ps1") (Join-Path $evidence "milestone$number-$native.log") @{ Configuration = $native }))
    }

    foreach ($run in $runs) { Complete-ScriptJob $run }
}

$codegenPending = $null
$ledger = Join-Path $repo 'tests/xUnitTest/TestData/codegen-pending'
if ($Codegen -eq 'mir' -and @(Get-ChildItem -LiteralPath $evidence -Filter 'tests-*.xml' -File).Count -gt 0) {
    # The sel failing set against the pending ledger (R5 E10, E11), also after failed tests: the run passes when they are equal.
    $timer = Start-Step 'codegen pending'
    try {
        $codegenPending = Write-KimiCodegenPending $evidence $Mode $Configuration $Fixtures $ledger
        $totals = $codegenPending.totals
        $line = @($totals.Keys | ForEach-Object { "$_ $($totals[$_].tests)/$($totals[$_].fixtures)" }) -join ', '
        $detail = "$($codegenPending.differences) entries differ from the ledger; cases/fixtures: $line; $($codegenPending.report)"
        Add-Step 'codegen pending' ($codegenPending.differences -eq 0) $detail $timer.Elapsed.TotalSeconds
    }
    catch { Add-Step 'codegen pending' $false $_.Exception.Message $timer.Elapsed.TotalSeconds }
}

if ($null -ne $beforeInputs) {
    $timer = Start-Step 'input stability'
    try {
        $afterInputs = Get-KimiVerificationInputs $repo
        $afterInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'inputs-after.json')
        Assert-KimiVerificationInputsStable $beforeInputs $afterInputs
        $inputStability = 'passed'
        Add-Step 'input stability' $true 'Source/configuration and NuGet inputs unchanged' $timer.Elapsed.TotalSeconds
    }
    catch {
        $inputStability = 'failed'
        Add-Step 'input stability' $false $_.Exception.Message $timer.Elapsed.TotalSeconds
    }
}
if ($UpdatePending) {
    # After the input observations, so the evidence records the ledger it compared, and only when the gate's differences are
    # the run's one failure: a failed or partial run would drop rows it did not observe.
    $timer = Start-Step 'update pending'
    try {
        $other = @($steps | Where-Object { $_.result -ceq 'FAIL' -and $_.step -cne 'codegen pending' } | ForEach-Object { $_.step })
        if ($null -eq $codegenPending -or $other.Count -gt 0) { throw "The ledger is not updated: $(if ($other) { "failed $($other -join ', ')" } else { 'no pending report' })." }
        $changed = @(Update-KimiCodegenLedger $ledger $codegenPending.report)
        Add-Step 'update pending' $true $(if ($changed) { "$($changed -join ', '); verify the written ledger again" } else { 'no change' }) $timer.Elapsed.TotalSeconds
    }
    catch { Add-Step 'update pending' $false $_.Exception.Message $timer.Elapsed.TotalSeconds }
}
$env:KIMI_PIPELINE = $previousPipeline
$env:KIMI_CODEGEN = $previousCodegen
$summary = [ordered]@{ mode = $Mode; configuration = $Configuration; pipeline = $Pipeline; codegen = $Codegen; buildTarget = $buildTarget; testPurpose = $TestPurpose; head = $head; dirty = $dirty; inputStability = $inputStability; started = $stamp; workDirectory = $work; toolchainVerification = $toolchainVerification; testParallel = $TestParallel; nativeParallel = $NativeParallel; milestoneParallel = $Parallel; seconds = [Math]::Round($totalTimer.Elapsed.TotalSeconds, 3); steps = $steps }
if ($Codegen -eq 'mir') { $summary.codegenPending = $codegenPending }
$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'summary.json')
Write-Host "Evidence: $evidence (HEAD $head$(if ($dirty) { ', uncommitted changes' }))"
if ($failed) { exit 1 }
