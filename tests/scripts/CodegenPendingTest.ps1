# Regression checks for codegen-pending.ps1 over synthetic Verify evidence; no compiler build or native toolchain is needed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/verification-selection.ps1')
. (Join-Path $PSScriptRoot '../../scripts/codegen-pending.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $repo ('temp/codegen-pending/' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$cases = 0
function Put([string] $path, [string] $text) {
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $text, $utf8)
}
function Expect([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:cases++
}
function Reject([scriptblock] $action, [string] $message) {
    try { & $action }
    catch {
        Expect ($_.Exception.Message -like "*$message*") "Unexpected rejection: $($_.Exception.Message)"
        return
    }
    throw "Expected rejection containing '$message'."
}
# Tests are 'Type.Method=Result' cases (a theory repeats its method); each native step is a hashtable of fixture -> levels, where a
# leading '!' records a failed run.
function New-Run([string] $name, [string[]] $tests, [hashtable[]] $native = @(), [string[]] $selected = $null, [string[]] $discovered = $null) {
    $directory = Join-Path $work $name
    $nodes = foreach ($test in $tests) {
        $method, $result = $test.Split('=')
        $separator = $method.LastIndexOf('.')
        "<test type=`"$($method.Substring(0, $separator))`" method=`"$($method.Substring($separator + 1))`" result=`"$result`" />"
    }
    Put (Join-Path $directory 'tests-Release-run.xml') "<assemblies><assembly><collection>$($nodes -join '')</collection></assembly></assemblies>"
    for ($i = 0; $i -lt $native.Count; $i++) {
        $records = foreach ($fixture in @($native[$i].Keys | Sort-Object)) {
            $levels = $native[$i][$fixture].TrimStart('!')
            [ordered]@{ fixture = $fixture; ok = $levels -ceq $native[$i][$fixture]; levels = @($levels.Split(',')); seconds = 0.1; log = "$fixture.log" }
        }
        Put (Join-Path $directory "native-$i-fixtures/results.json") (ConvertTo-Json -InputObject @($records) -Depth 4)
    }
    if ($null -ne $selected) { Put (Join-Path $directory 'selected-methods.json') (ConvertTo-Json -InputObject @($selected)) }
    if ($null -ne $discovered) { Put (Join-Path $directory 'discovered-methods.json') (ConvertTo-Json -InputObject @($discovered)) }
    $directory
}
function Totals($totals, [string] $kind, [int[]] $expected, [string] $what) {
    $actual = foreach ($name in @('ledger', 'failing', 'passesNow', 'noLongerExecuted', 'outOfScope', 'added')) { $totals.$name.$kind }
    Expect (($actual -join ',') -ceq ($expected -join ',')) "$what $kind totals: expected $($expected -join ','), got $($actual -join ',')."
}
function States($report, [string] $kind, [string] $expected) {
    $actual = @($report.entries | Where-Object kind -CEQ $kind | ForEach-Object { "$($_.key)=$($_.state)" }) -join ' '
    Expect ($actual -ceq $expected) "Expected $kind states '$expected', got '$actual'."
}
$ledger = Join-Path $work 'ledger'
Put (Join-Path $ledger 'C-frame.tsv') ("test`tXunitTest.A.Same`t2`ntest`tXunitTest.A.Fewer`t3`ntest`tXunitTest.A.Fixed`t1`n" +
    "fixture`tMainSame.ll`t1`nfixture`tMainFixed.ll`t1`n")
Put (Join-Path $ledger 'C-scalar.tsv') ("test`tXunitTest.A.More`t1`r`ntest`tXunitTest.A.Gone`t2`r`ntest`tXunitTest.A.Explicit`t1`r`n" +
    "test`tXunitTest.A+AllocationTests.Warm`t1`r`nfixture`tMainGone.ll`t1`r`nfixture`tOther.ll`t1`r`n")
$rows = Read-KimiCodegenLedger $ledger
Expect ($rows.rows.Count -eq 11 -and ($rows.groups -join ',') -ceq 'C-frame,C-scalar') 'The ledger is the union of its group files.'
Expect ($rows.rows['test XunitTest.A.Gone'].group -ceq 'C-scalar' -and $rows.rows['test XunitTest.A.Fewer'].count -eq 3) 'A row keeps its group and count.'
Expect ((Read-KimiCodegenLedger (Join-Path $work 'none')).rows.Count -eq 0) 'No ledger directory is an empty ledger.'
$bad = @{
    'kind' = "tst`tXunitTest.A.B`t1"; 'zero' = "test`tXunitTest.A.B`t0"; 'fixture-count' = "fixture`tA.ll`t2"; 'fixture-name' = "fixture`tA.txt`t1"
    'unqualified' = "test`tMethod`t1"; 'blank' = "test`tXunitTest.A.B`t1`n`ntest`tXunitTest.A.C`t1"; 'column' = "test`tXunitTest.A.B`t1`treason"
    'spaces' = 'test XunitTest.A.B 1'
}
foreach ($case in $bad.Keys) {
    Put (Join-Path $work "bad-$case/G.tsv") $bad[$case]
    Reject { Read-KimiCodegenLedger (Join-Path $work "bad-$case") } 'is not a pending row'
}
Put (Join-Path $work 'twice/One.tsv') "test`tXunitTest.A.B`t1`n"
Put (Join-Path $work 'twice/Two.tsv') "test`tXunitTest.A.B`t2`n"
Reject { Read-KimiCodegenLedger (Join-Path $work 'twice') } 'lists test XunitTest.A.B again'
Put (Join-Path $work 'other/G.md') "notes`n"
Reject { Read-KimiCodegenLedger (Join-Path $work 'other') } 'holds only <group>.tsv files'
$null = [IO.Directory]::CreateDirectory((Join-Path $work 'nested/G'))
Reject { Read-KimiCodegenLedger (Join-Path $work 'nested') } 'holds only <group>.tsv files'

# A Session runs every test: each state, theory cases counted per method, a non-Pass result failing, a fixture failing in one of
# two native steps or at one level only.
$tests = @('XunitTest.A.Same=Fail', 'XunitTest.A.Same=Fail', 'XunitTest.A.Fewer=Pass', 'XunitTest.A.Fewer=Fail', 'XunitTest.A.Fewer=Pass',
    'XunitTest.A.Fixed=Pass', 'XunitTest.A.More=Fail', 'XunitTest.A.More=Fail', 'XunitTest.A.New=Fail', 'XunitTest.A.New=Pass',
    'XunitTest.A.Explicit=NotRun', 'XunitTest.A+AllocationTests.Warm=Skip', 'XunitTest.B.Ok=Pass')
$native = @(@{ 'MainSame.ll' = '!O0'; 'MainFixed.ll' = 'O0,O2'; 'MainHalf.ll' = 'O0'; 'Twice.ll' = '!O0,O2' }, @{ 'Twice.ll' = 'O0,O2' })
$session = New-Run 'session' $tests $native
$summary = Write-KimiCodegenPending $session 'Session' 'Release' @('Main*.ll', 'Twice*.ll') $ledger
$report = Get-Content -LiteralPath (Join-Path $session 'codegen-pending.json') -Raw | ConvertFrom-Json
States $report 'test' ('XunitTest.A+AllocationTests.Warm=pending XunitTest.A.Explicit=noLongerExecuted XunitTest.A.Fewer=passesNow ' +
    'XunitTest.A.Fixed=passesNow XunitTest.A.Gone=noLongerExecuted XunitTest.A.More=added XunitTest.A.New=added XunitTest.A.Same=pending')
States $report 'fixture' 'MainFixed.ll=passesNow MainGone.ll=noLongerExecuted MainHalf.ll=added MainSame.ll=pending Other.ll=outOfScope Twice.ll=added'
Totals $report.totals 'tests' @(11, 7, 3, 3, 0, 2) 'Session'
Totals $report.totals 'fixtures' @(4, 3, 1, 1, 1, 2) 'Session'
Totals $report.groups.'C-frame' 'tests' @(6, 3, 3, 0, 0, 0) 'C-frame'
Totals $report.groups.'C-scalar' 'tests' @(5, 3, 0, 3, 0, 1) 'C-scalar'
Totals $report.groups.'C-frame' 'fixtures' @(2, 1, 1, 0, 0, 0) 'C-frame'
Totals $report.groups.'C-scalar' 'fixtures' @(2, 0, 0, 1, 1, 0) 'C-scalar'
$fewer = @($report.entries | Where-Object key -CEQ 'XunitTest.A.Fewer')[0]
$new = @($report.entries | Where-Object key -CEQ 'XunitTest.A.New')[0]
Expect ($fewer.group -ceq 'C-frame' -and $fewer.ledger -eq 3 -and $fewer.executed -eq 3 -and $fewer.failing -eq 1) 'An entry records its group and case counts.'
Expect ($null -eq $new.group -and $new.ledger -eq 0 -and $new.executed -eq 2 -and $new.failing -eq 1) 'An unlisted failure has no group.'
Expect ($report.executed.tests -eq 12 -and $report.executed.fixtures -eq 4 -and $report.run.mode -ceq 'Session') 'The executed cases exclude Not Run tests.'
Expect ($summary.report -ceq (Join-Path $session 'codegen-pending.json') -and $summary.totals.failing.tests -eq 7 -and $summary.executed.tests -eq 12) `
    'The summary field repeats the totals.'

# A Unit run covers its selected methods and the methods its assembly no longer has; without native results no fixture is covered.
$unit = New-Run 'unit' @('XunitTest.A.Same=Fail', 'XunitTest.A.Same=Fail', 'XunitTest.A.Explicit=NotRun') @() `
    @('XunitTest.A.Same', 'XunitTest.A.Explicit', 'XunitTest.A.Fewer') `
    @('XunitTest.A.Same', 'XunitTest.A.Explicit', 'XunitTest.A.Fewer', 'XunitTest.A.Fixed', 'XunitTest.A.More', 'XunitTest.A+AllocationTests.Warm')
$null = Write-KimiCodegenPending $unit 'Unit' 'Release' @('Main*.ll') $ledger
$report = Get-Content -LiteralPath (Join-Path $unit 'codegen-pending.json') -Raw | ConvertFrom-Json
States $report 'test' ('XunitTest.A+AllocationTests.Warm=outOfScope XunitTest.A.Explicit=noLongerExecuted XunitTest.A.Fewer=noLongerExecuted ' +
    'XunitTest.A.Fixed=outOfScope XunitTest.A.Gone=noLongerExecuted XunitTest.A.More=outOfScope XunitTest.A.Same=pending')
States $report 'fixture' 'MainFixed.ll=outOfScope MainGone.ll=outOfScope MainSame.ll=outOfScope Other.ll=outOfScope'
Totals $report.totals 'tests' @(11, 2, 0, 6, 3, 0) 'Unit'

# Without a ledger every failure is added; missing evidence throws.
$null = Write-KimiCodegenPending $session 'Session' 'Release' @() (Join-Path $work 'none')
$report = Get-Content -LiteralPath (Join-Path $session 'codegen-pending.json') -Raw | ConvertFrom-Json
Totals $report.totals 'tests' @(0, 7, 0, 0, 0, 7) 'Empty ledger'
Expect (@($report.ledger.groups).Count -eq 0 -and @($report.groups.PSObject.Properties).Count -eq 0) 'An empty ledger has no groups.'
Remove-Item -LiteralPath (Join-Path $unit 'selected-methods.json')
Reject { Write-KimiCodegenPending $unit 'Unit' 'Release' @() $ledger } 'has no selected-methods.json'
Remove-Item -LiteralPath (Join-Path $session 'tests-Release-run.xml')
Reject { Write-KimiCodegenPending $session 'Session' 'Release' @() $ledger } 'records no test results'
Write-Output "Passed $cases pending ledger checks. Synthetic runs retained: $work"
