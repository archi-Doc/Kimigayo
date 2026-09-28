[CmdletBinding()]
param(
    [string] $Compiler = 'Kimi/bin/Debug/net10.0/Kimi.dll',
    [string] $ResultRoot = 'bin/test-profile-verification'
)
$ErrorActionPreference = 'Stop'
$Compiler = [IO.Path]::GetFullPath($Compiler)
$root = [IO.Path]::GetFullPath($ResultRoot)
$null = New-Item -ItemType Directory -Force -Path $root
$source = @'
$abort("startup must not run")
#Test
func passes()
    $expect(true, message: $abort("message must stay lazy"))
    $require(1 == 1)
#Test
func comparisons()
    var value = 7
    $expect(value == 8, message: "comparison")
    $expect(1.5 == 2.5)
    Console.writeLine("continued")
#Test
func stops()
    defer => Console.writeLine("outer defer must not run")
    $require(false, message: "required failure")
    Console.writeLine("must not run")
#Test
func nested()
    $expect(false, message: (message: do
        $expect(false, message: "inner")
        exit to message: "outer"
    ))
#Test
func cleanup()
    defer => $expect(false, message: "cleanup")
#Test
func messageAbort()
    $expect(false, message: $abort("message abort"))
#Test
func temporary()
    Console.writeLine(Test.tempDirectory())
    $expect(false)
#Test
func timesOut()
    $expect(false)
    loop => ()
#Test
func flood()
    var index = 0
    while index < 1000
        Console.writeLine("0123456789")
        index += 1
    $expect(false)
func shared<T>(value: T)
    $expect(1 == 2, message: "shared helper")
#Test
func generic() => shared(1)
'@
$reports = @()
foreach ($optimization in @('O0', 'O2')) {
    $project = Join-Path $root $optimization
    $null = New-Item -ItemType Directory -Force -Path $project
    $source | Set-Content -LiteralPath (Join-Path $project 'tests.kimi') -Encoding utf8
    @"
Targets = { "x86_64-pc-windows-msvc" }
OutputKind = "Library"
Optimization = "$optimization"
TestSources = { "tests.kimi" }
Test = { Timeout = "2s", RecoveryGrace = "3s", LogBytes = 256 }
"@ | Set-Content -LiteralPath (Join-Path $project "$optimization.kimiproj") -Encoding utf8
    $store = Join-Path $project 'bin/test-results'
    $before = if (Test-Path -LiteralPath $store) { @(Get-ChildItem -LiteralPath $store -Directory).Count } else { 0 }
    & dotnet $Compiler test $project --format json --list 1> (Join-Path $project 'list.json') 2> (Join-Path $project 'list.stderr')
    if ($LASTEXITCODE -ne 0) { throw "Listing failed: $project" }
    $list = Get-Content -LiteralPath (Join-Path $project 'list.json') -Raw | ConvertFrom-Json
    if ($list.summary.selected -ne 10) { throw 'Incorrect discovery count' }
    $after = if (Test-Path -LiteralPath $store) { @(Get-ChildItem -LiteralPath $store -Directory).Count } else { 0 }
    if ($before -ne $after) { throw 'Listing created artifacts' }
    & dotnet $Compiler test $project --format json --jobs 3 1> (Join-Path $project 'run.json') 2> (Join-Path $project 'run.stderr')
    if ($LASTEXITCODE -ne 1) { throw "Unexpected runner exit $LASTEXITCODE for $optimization" }
    $run = Get-Content -LiteralPath (Join-Path $project 'run.json') -Raw | ConvertFrom-Json
    if ($run.errors.Count -ne 0 -or $run.summary.passed -ne 1 -or $run.summary.failed -ne 9) { throw 'Wrong run summary' }
    foreach ($case in $run.cases) {
        if ($case.managementErrors.Count -ne 0) { throw ($case.managementErrors -join ', ') }
    }
    $cases = @{}
    foreach ($case in $run.cases) { $cases[$case.name] = $case }
    if ($cases['stops'].termination -ne 'requireAbort') { throw 'Missing require Abort event' }
    if ((Get-Item -LiteralPath $cases['stops'].stdout).Length -ne 0) { throw 'Require ran enclosing cleanup or its continuation' }
    if ($cases['messageAbort'].termination -ne 'abort' -or $cases['messageAbort'].failureCount -ne '1') { throw 'Lost failure before message Abort' }
    if ($cases['timesOut'].termination -ne 'timeout' -or $cases['timesOut'].failureCount -ne '1') { throw 'Lost failure before timeout' }
    if ($cases['cleanup'].failures[0].phase -ne 'cleanup') { throw 'Incorrect cleanup phase' }
    if ($cases['nested'].failures[0].message -ne 'outer' -or $cases['nested'].failures[1].message -ne 'inner') { throw 'Nested message identity lost' }
    if ($cases['comparisons'].failures[0].values[0].bits -ne '00000000000000000000000000000007') { throw 'Integer snapshot lost' }
    if ($cases['comparisons'].failures[1].values[0].type -ne 'float') { throw 'Float snapshot lost' }
    if ($cases['generic'].failures[0].values[0].bits -ne '00000000000000000000000000000001') { throw 'Shared snapshot lost' }
    if ((Get-Content -LiteralPath $cases['comparisons'].stdout -Raw).Trim() -ne 'continued') { throw 'Expect did not continue' }
    if ((Get-Item -LiteralPath $cases['flood'].stdout).Length -gt 256 -or [long]$cases['flood'].omittedLogBytes -eq 0) { throw 'Log budget failed' }
    $temporary = (Get-Content -LiteralPath $cases['temporary'].stdout -Raw).Trim()
    if (Test-Path -LiteralPath $temporary) { throw 'Temporary directory leaked' }
    & dotnet $Compiler test $project --format json --filter nested --case-diagnostic-count 0 --case-diagnostic-bytes 0 --case-log-bytes 0 1> (Join-Path $project 'zero.json') 2> (Join-Path $project 'zero.stderr')
    if ($LASTEXITCODE -ne 1) { throw 'Zero-budget run did not fail' }
    $zero = Get-Content -LiteralPath (Join-Path $project 'zero.json') -Raw | ConvertFrom-Json
    $selected = $zero.cases | Where-Object selected
    if ($selected.failureCount -ne '2' -or $selected.failures.Count -ne 0) { throw 'Zero-budget failure latching failed' }
    if ($selected.artifactId -ne $cases['nested'].artifactId -or $selected.caseId -ne $cases['nested'].caseId) { throw 'Runtime selection or budgets changed compilation identity' }
    $reports += [ordered]@{ optimization = $optimization; selected = 10; passed = 1; failed = 9; zeroBudget = 'PASS' }
}
@'
Projects = { "O0/O0.kimiproj", "O2/O2.kimiproj" }
'@ | Set-Content -LiteralPath (Join-Path $root 'tests.kimisln') -Encoding utf8
& dotnet $Compiler test (Join-Path $root 'tests.kimisln') --list --filter passes --format json 1> (Join-Path $root 'solution.json') 2> (Join-Path $root 'solution.stderr')
if ($LASTEXITCODE -ne 0) { throw 'Solution discovery failed' }
$solution = Get-Content -LiteralPath (Join-Path $root 'solution.json') -Raw | ConvertFrom-Json
if ($solution.summary.selected -ne 2 -or $solution.projects.Count -ne 2) { throw 'Solution did not include both projects' }
foreach ($case in ($solution.cases | Where-Object selected)) {
    $owner = if ($case.projectId -eq 'O0.kimiproj') { 'O0' } else { 'O2' }
    $single = Get-Content -LiteralPath (Join-Path $root "$owner/list.json") -Raw | ConvertFrom-Json
    if ($case.caseId -ne ($single.cases | Where-Object name -eq passes).caseId) { throw 'Solution changed case identity' }
}
& dotnet $Compiler test (Join-Path $root 'tests.kimisln') --filter passes --jobs 1 --format json 1> (Join-Path $root 'solution-run.json') 2> (Join-Path $root 'solution-run.stderr')
if ($LASTEXITCODE -ne 0) { throw 'Solution execution failed' }
$solutionRun = Get-Content -LiteralPath (Join-Path $root 'solution-run.json') -Raw | ConvertFrom-Json
if ($solutionRun.summary.passed -ne 2) { throw 'Solution did not execute both projects' }
@'
#Test
func invalidUnselected() => Missing.api()
'@ | Add-Content -LiteralPath (Join-Path $root 'O2/tests.kimi') -Encoding utf8
& dotnet $Compiler test (Join-Path $root 'tests.kimisln') --filter passes --format json 1> (Join-Path $root 'barrier.json') 2> (Join-Path $root 'barrier.stderr')
if ($LASTEXITCODE -ne 2) { throw 'Unselected invalid test escaped verification' }
$barrier = Get-Content -LiteralPath (Join-Path $root 'barrier.json') -Raw | ConvertFrom-Json
if (@($barrier.cases | Where-Object started).Count -ne 0) { throw 'Cases ran before all projects were verified' }
$reports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding utf8
Write-Output "PASS: O0/O2 semantics, cleanup, snapshots, isolation, timeout, budgets, solution discovery/execution and all-project verification barrier. Evidence: $root"
