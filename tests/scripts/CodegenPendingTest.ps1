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
# The method of a case name of XunitTest (the name before any argument list) and the case's ledger row.
function Method([string] $name) { if ($name.Contains('(')) { $name.Substring(0, $name.IndexOf('(')) } else { $name } }
function Row([string] $name) { "test`tXunitTest.$(Method $name)`tXunitTest.$name" }
# Tests are 'Name=Result' cases of XunitTest.<Name>; each native step is a hashtable of fixture -> levels, where a leading '!'
# records a failed run.
function New-Run([string] $name, [string[]] $tests, [hashtable[]] $native = @(), [string[]] $selected = $null, [string[]] $discovered = $null) {
    $directory = Join-Path $work $name
    $nodes = foreach ($test in $tests) {
        $display = 'XunitTest.' + $test.Substring(0, $test.LastIndexOf('='))
        $method = Method $display
        $separator = $method.LastIndexOf('.')
        "<test name=`"$([Security.SecurityElement]::Escape($display))`" type=`"$($method.Substring(0, $separator))`" " +
            "method=`"$($method.Substring($separator + 1))`" result=`"$($test.Substring($test.LastIndexOf('=') + 1))`" />"
    }
    Put (Join-Path $directory 'tests-Release-run.xml') "<assemblies><assembly><collection>$($nodes -join '')</collection></assembly></assemblies>"
    for ($i = 0; $i -lt $native.Count; $i++) {
        $records = foreach ($fixture in @($native[$i].Keys | Sort-Object)) {
            $levels = $native[$i][$fixture].TrimStart('!')
            [ordered]@{ fixture = $fixture; ok = $levels -ceq $native[$i][$fixture]; levels = @($levels.Split(',')); seconds = 0.1; log = "$fixture.log" }
        }
        Put (Join-Path $directory "native-$i-fixtures/results.json") (ConvertTo-Json -InputObject @($records) -Depth 4)
    }
    if ($null -ne $selected) { Put (Join-Path $directory 'selected-methods.json') (ConvertTo-Json -InputObject @($selected | ForEach-Object { "XunitTest.$_" })) }
    if ($null -ne $discovered) { Put (Join-Path $directory 'discovered-methods.json') (ConvertTo-Json -InputObject @($discovered | ForEach-Object { "XunitTest.$_" })) }
    $directory
}
function Read-Report([string] $run) { Get-Content -LiteralPath (Join-Path $run 'codegen-pending.json') -Raw | ConvertFrom-Json }
function Totals($totals, [string] $kind, [int[]] $expected, [string] $what) {
    $actual = foreach ($name in @('ledger', 'failing', 'passesNow', 'noLongerExecuted', 'outOfScope', 'added')) { $totals.$name.$kind }
    Expect (($actual -join ',') -ceq ($expected -join ',')) "$what $kind totals: expected $($expected -join ','), got $($actual -join ',')."
}
# The entries' states in report order (ordinal by key, then name), tests by name without 'XunitTest.'.
function States($report, [string] $kind, [string[]] $expected) {
    $actual = @($report.entries | Where-Object kind -CEQ $kind | ForEach-Object { "$(if ($_.name) { $_.name.Substring(10) } else { $_.key })=$($_.state)" })
    Expect (($actual -join ' | ') -ceq ($expected -join ' | ')) "Expected $kind states '$($expected -join ' | ')', got '$($actual -join ' | ')'."
}
$text = 'A.Text(s: "a b", n: [1, 2])'
$ledger = Join-Path $work 'ledger'
Put (Join-Path $ledger 'C-frame.tsv') ((@((Row 'A.Same(x: 1)'), (Row 'A.Same(x: 2)'), (Row 'A.Fixed'), (Row 'A.Theory(x: 1)'), (Row 'A.Theory(x: 2)'),
    (Row 'A.Mixed(x: 1)'), "fixture`tMainSame.ll`t1", "fixture`tMainFixed.ll`t1") -join "`n") + "`n")
Put (Join-Path $ledger 'C-scalar.tsv') ((@((Row 'A.Renamed(x: 1)'), (Row $text), (Row 'A.Gone'), (Row 'A.Explicit'), (Row 'A+AllocationTests.Warm'),
    "fixture`tMainGone.ll`t1", "fixture`tOther.ll`t1") -join "`r`n") + "`r`n")
$rows = Read-KimiCodegenLedger $ledger
Expect ($rows.rows.Count -eq 15 -and ($rows.groups -join ',') -ceq 'C-frame,C-scalar') 'The ledger is the union of its group files.'
Expect ($rows.rows[(Row $text)] -ceq 'C-scalar' -and $rows.rows["fixture`tOther.ll"] -ceq 'C-scalar') 'A row keeps its group.'
Expect ((Read-KimiCodegenLedger (Join-Path $work 'none')).rows.Count -eq 0) 'No ledger directory is an empty ledger.'
$bad = @{
    'kind' = "tst`tXunitTest.A.B`tXunitTest.A.B"; 'count' = "$(Row 'A.B')`t1"; 'fixture-count' = "fixture`tA.ll`t2"
    'fixture-name' = "fixture`tA.txt`t1"; 'unqualified' = "test`tMethod`tMethod"; 'no-name' = "test`tXunitTest.A.B"
    'other-method' = "test`tXunitTest.A.B`tXunitTest.A.C(x: 1)"; 'longer-method' = "test`tXunitTest.A.B`tXunitTest.A.Bc"
    'unclosed' = "test`tXunitTest.A.B`tXunitTest.A.B(x: 1"; 'trailing' = "$(Row 'A.B(x: 1)') "
    'blank' = "$(Row 'A.B')`n`n$(Row 'A.C')"; 'column' = "$(Row 'A.B')`treason"; 'spaces' = 'test XunitTest.A.B XunitTest.A.B'
}
foreach ($case in $bad.Keys) {
    Put (Join-Path $work "bad-$case/G.tsv") $bad[$case]
    Reject { Read-KimiCodegenLedger (Join-Path $work "bad-$case") } 'is not a pending row'
}
Put (Join-Path $work 'twice/One.tsv') "$(Row 'A.B(x: 1)')`n$(Row 'A.B(x: 2)')`n"
Put (Join-Path $work 'twice/Two.tsv') "$(Row 'A.B(x: 2)')`n"
Reject { Read-KimiCodegenLedger (Join-Path $work 'twice') } 'Two.tsv:1 lists test XunitTest.A.B XunitTest.A.B(x: 2) again'
Put (Join-Path $work 'other/G.md') "notes`n"
Reject { Read-KimiCodegenLedger (Join-Path $work 'other') } 'holds only <group>.tsv files'
$null = [IO.Directory]::CreateDirectory((Join-Path $work 'nested/G'))
Reject { Read-KimiCodegenLedger (Join-Path $work 'nested') } 'holds only <group>.tsv files'

# A Session selects every method it discovers. Each case is keyed by its full name, so a deleted failing case (Theory x: 2)
# is no longer executed, a fixed case beside a new failing one (Mixed) passes now beside an added entry, and a renamed case
# (Renamed) is no longer executed beside an added entry. A non-Pass result fails; a fixture fails in one of two native steps
# or at one level only.
$tests = @('A.Same(x: 1)=Fail', 'A.Same(x: 2)=Fail', 'A.Fixed=Pass', 'A.Theory(x: 1)=Fail', 'A.Theory(x: 3)=Pass', 'A.Mixed(x: 1)=Pass',
    'A.Mixed(x: 2)=Fail', 'A.Renamed(x: 2)=Fail', "$text=Fail", 'A.Explicit=NotRun', 'A+AllocationTests.Warm=Skip', 'A.New=Fail', 'B.Ok=Pass')
$native = @(@{ 'MainSame.ll' = '!O0'; 'MainFixed.ll' = 'O0,O2'; 'MainHalf.ll' = 'O0'; 'Twice.ll' = '!O0,O2' }, @{ 'Twice.ll' = 'O0,O2' })
$all = @($tests | ForEach-Object { Method $_.Substring(0, $_.LastIndexOf('=')) } | Sort-Object -Unique)
$session = New-Run 'session' $tests $native $all $all
$summary = Write-KimiCodegenPending $session 'Session' 'Release' @('Main*.ll', 'Twice*.ll') $ledger
$report = Read-Report $session
States $report 'test' @('A+AllocationTests.Warm=pending', 'A.Explicit=noLongerExecuted', 'A.Fixed=passesNow', 'A.Gone=noLongerExecuted',
    'A.Mixed(x: 1)=passesNow', 'A.Mixed(x: 2)=added', 'A.New=added', 'A.Renamed(x: 1)=noLongerExecuted', 'A.Renamed(x: 2)=added',
    'A.Same(x: 1)=pending', 'A.Same(x: 2)=pending', "$text=pending", 'A.Theory(x: 1)=pending', 'A.Theory(x: 2)=noLongerExecuted')
States $report 'fixture' @('MainFixed.ll=passesNow', 'MainGone.ll=noLongerExecuted', 'MainHalf.ll=added', 'MainSame.ll=pending', 'Other.ll=outOfScope',
    'Twice.ll=added')
Totals $report.totals 'tests' @(11, 8, 2, 4, 0, 3) 'Session'
Totals $report.totals 'fixtures' @(4, 3, 1, 1, 1, 2) 'Session'
Totals $report.groups.'C-frame' 'tests' @(6, 3, 2, 1, 0, 0) 'C-frame'
Totals $report.groups.'C-scalar' 'tests' @(5, 2, 0, 3, 0, 0) 'C-scalar'
Totals $report.groups.'C-frame' 'fixtures' @(2, 1, 1, 0, 0, 0) 'C-frame'
Totals $report.groups.'C-scalar' 'fixtures' @(2, 0, 0, 1, 1, 0) 'C-scalar'
$pending = @($report.entries | Where-Object name -CEQ "XunitTest.$text")[0]
$added = @($report.entries | Where-Object name -CEQ 'XunitTest.A.Mixed(x: 2)')[0]
Expect ($pending.group -ceq 'C-scalar' -and $pending.key -ceq 'XunitTest.A.Text') 'An entry records its method and group.'
Expect ($null -eq $added.group -and $added.key -ceq 'XunitTest.A.Mixed') 'An unlisted failure has no group.'
Expect ($report.executed.tests -eq 12 -and $report.executed.fixtures -eq 4 -and $report.run.mode -ceq 'Session') 'The executed cases exclude Not Run tests.'
Expect ($summary.report -ceq (Join-Path $session 'codegen-pending.json') -and $summary.totals.failing.tests -eq 8 -and $summary.executed.tests -eq 12) `
    'The summary field repeats the totals.'
# The gate: the run passes when no entry passes now, is no longer executed or was added.
Expect ($summary.differences -eq 13) "The Session differs from the ledger in 13 entries, not $($summary.differences)."

# A Unit run covers its selected methods and the methods its assembly no longer has; without native results no fixture is covered.
$unit = New-Run 'unit' @('A.Same(x: 1)=Fail', 'A.Same(x: 2)=Fail', 'A.Theory(x: 3)=Pass', 'A.Explicit=NotRun') @() @('A.Same', 'A.Explicit', 'A.Theory') `
    @('A.Same', 'A.Explicit', 'A.Theory', 'A.Fixed', 'A.Mixed', 'A.Renamed', 'A.Text', 'A+AllocationTests.Warm')
$summary = Write-KimiCodegenPending $unit 'Unit' 'Release' @('Main*.ll') $ledger
$report = Read-Report $unit
States $report 'test' @('A+AllocationTests.Warm=outOfScope', 'A.Explicit=noLongerExecuted', 'A.Fixed=outOfScope', 'A.Gone=noLongerExecuted',
    'A.Mixed(x: 1)=outOfScope', 'A.Renamed(x: 1)=outOfScope', 'A.Same(x: 1)=pending', 'A.Same(x: 2)=pending', "$text=outOfScope",
    'A.Theory(x: 1)=noLongerExecuted', 'A.Theory(x: 2)=noLongerExecuted')
States $report 'fixture' @('MainFixed.ll=outOfScope', 'MainGone.ll=outOfScope', 'MainSame.ll=outOfScope', 'Other.ll=outOfScope')
Totals $report.totals 'tests' @(11, 2, 0, 4, 5, 0) 'Unit'
Expect ($summary.differences -eq 4) 'Out-of-scope rows do not differ.'
# A selected method whose results record no case (a partial run) hides its rows, so the report refuses it; Not Run is recorded.
$partial = New-Run 'partial' @('A.Same(x: 1)=Fail', 'A.Explicit=NotRun') @() @('A.Same', 'A.Explicit', 'A.Fixed') @('A.Same', 'A.Explicit', 'A.Fixed')
Reject { Write-KimiCodegenPending $partial 'Unit' 'Release' @() $ledger } 'record no case of 1 selected methods (a partial run): XunitTest.A.Fixed'

# DECISIONS R5-M73's filing rule: an exact class before any family, the first unit in plan order, K13a's class from its Change
# column, the outermost class, a family by ordinal prefix, a fixture by its stem, else C-frame.
$filing = [ordered]@{
    'GenericGenerationDiagnosticTest.M' = 'K-edges'; 'GenericDestructorEmissionTest.M' = 'C-drop'; 'GenericFooTest.M' = 'call'
    'GenericGenerationDiagnosticTest+AllocationTests.M' = 'K-edges'; 'OwnershipStorageLimitTest.M' = 'C-drop'
    'FunctionCodegenTest.M(x: 1)' = 'call'; 'PipelineObjectTest.M' = 'object'
    'StaticFooTest.M' = 'C7b'; 'Utf8ConsoleTest.M' = 'format'; 'TestProtocolTest.M' = 'test'; 'scalarTest.M' = 'C-frame'; 'OtherTest.M' = 'C-frame'
}
foreach ($name in $filing.Keys) { Expect ((Get-KimiCodegenGroup (Row $name)) -ceq $filing[$name]) "$name is filed under $($filing[$name])." }
Expect ((Get-KimiCodegenGroup "fixture`tScalarAdd.ll`t1") -ceq 'C-scalar' -and (Get-KimiCodegenGroup "fixture`tFrameLocals.ll") -ceq 'C-frame') 'A fixture is filed by its stem.'
# Every tracked row lies where the rule files it.
$tracked = Read-KimiCodegenLedger (Join-Path $repo 'tests/xUnitTest/TestData/codegen-pending')
$misfiled = @($tracked.rows.Keys | Where-Object { (Get-KimiCodegenGroup $_) -cne $tracked.rows[$_] })
Expect ($misfiled.Count -eq 0) "The filing rule moves $($misfiled.Count) tracked rows: $(($misfiled | Select-Object -First 3) -join ' | ')"

# -UpdatePending's write: pending and out-of-scope rows stay in their files, rows that pass now or are no longer executed
# leave (an emptied file is removed), added rows are filed by the rule; each file is sorted with LF ends, and an unchanged
# file is not written. On the written ledger the run has no difference, and writing again changes nothing.
$update = Join-Path $work 'update'
Put (Join-Path $update 'C-frame.tsv') ((@((Row 'A.Keep(x: 1)'), (Row 'A.Pass'), "fixture`tGone.ll`t1") -join "`n") + "`n")
Put (Join-Path $update 'C-scalar.tsv') "$(Row 'ScalarTest.Fixed')`r`n"
Put (Join-Path $update 'call.tsv') "$(Row 'FunctionCodegenTest.Out')`n"
$old = [datetime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
[IO.File]::SetLastWriteTimeUtc((Join-Path $update 'call.tsv'), $old)
$methods = @('A.Keep', 'A.Pass', 'A.New', 'ScalarTest.Fixed', 'OwnershipStorageLimitTest+AllocationTests.Warm')
$changes = New-Run 'update-run' @('A.New(s: "b")=Fail', 'A.Keep(x: 1)=Fail', 'A.Pass=Pass', 'ScalarTest.Fixed=Pass', 'OwnershipStorageLimitTest+AllocationTests.Warm=Fail') `
    @(@{ 'DictionaryNew.ll' = '!O0' }) $methods ($methods + 'FunctionCodegenTest.Out')
$summary = Write-KimiCodegenPending $changes 'Unit' 'Release' @('*.ll') $update
$written = @(Update-KimiCodegenLedger $update $summary.report)
Expect ($summary.differences -eq 6 -and ($written -join ' | ') -ceq 'C-drop.tsv (1 rows) | C-frame.tsv (2 rows) | C-scalar.tsv removed | library-2.tsv (1 rows)') `
    "Unexpected update: $($written -join ' | ')"
function Text([string] $file) { [IO.File]::ReadAllText((Join-Path $update $file)) }
Expect ((Text 'C-frame.tsv') -ceq "$(Row 'A.Keep(x: 1)')`n$(Row 'A.New(s: "b")')`n") 'Kept and added rows are sorted with LF ends.'
Expect ((Text 'C-drop.tsv') -ceq "$(Row 'OwnershipStorageLimitTest+AllocationTests.Warm')`n" -and (Text 'library-2.tsv') -ceq "fixture`tDictionaryNew.ll`t1`n") `
    'Added rows are filed by the rule.'
Expect (-not (Test-Path -LiteralPath (Join-Path $update 'C-scalar.tsv')) -and [IO.File]::GetLastWriteTimeUtc((Join-Path $update 'call.tsv')) -eq $old) `
    'An emptied file is removed and an unchanged one is not written.'
$hashes = (Get-ChildItem -LiteralPath $update | Get-FileHash | ForEach-Object { "$($_.Path)=$($_.Hash)" }) -join ' '
$summary = Write-KimiCodegenPending $changes 'Unit' 'Release' @('*.ll') $update
Expect ($summary.differences -eq 0 -and $summary.totals.outOfScope.tests -eq 1) 'The written ledger equals the run.'
Expect (@(Update-KimiCodegenLedger $update $summary.report).Count -eq 0 -and
    ((Get-ChildItem -LiteralPath $update | Get-FileHash | ForEach-Object { "$($_.Path)=$($_.Hash)" }) -join ' ') -ceq $hashes) 'Writing an equal ledger changes nothing.'
# A case name that is not its type.method cannot become a row.
Put (Join-Path $work 'custom/codegen-pending.json') (ConvertTo-Json -Depth 3 @{ entries = @(@{ kind = 'test'; key = 'XunitTest.A.B'; name = 'Custom'; state = 'added' }) })
Reject { Update-KimiCodegenLedger (Join-Path $work 'custom-ledger') (Join-Path $work 'custom/codegen-pending.json') } 'cannot be a pending row'

# A name executed twice cannot identify a row; without a ledger every failure is added; missing evidence throws.
$twice = New-Run 'twice-run' @('A.Same(x: 1)=Fail', 'A.Same(x: 1)=Pass')
Reject { Write-KimiCodegenPending $twice 'Session' 'Release' @() $ledger } 'executed XunitTest.A.Same(x: 1) twice'
$null = Write-KimiCodegenPending $session 'Session' 'Release' @() (Join-Path $work 'none')
$report = Read-Report $session
Totals $report.totals 'tests' @(0, 8, 0, 0, 0, 8) 'Empty ledger'
Expect (@($report.ledger.groups).Count -eq 0 -and @($report.groups.PSObject.Properties).Count -eq 0) 'An empty ledger has no groups.'
Remove-Item -LiteralPath (Join-Path $unit 'selected-methods.json')
Reject { Write-KimiCodegenPending $unit 'Unit' 'Release' @() $ledger } 'has no selected-methods.json'
Remove-Item -LiteralPath (Join-Path $session 'tests-Release-run.xml')
Reject { Write-KimiCodegenPending $session 'Session' 'Release' @() $ledger } 'records no test results'
Write-Output "Passed $cases pending ledger checks. Synthetic runs retained: $work"
