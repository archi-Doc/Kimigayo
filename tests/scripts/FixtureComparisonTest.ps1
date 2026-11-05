# Regression checks for compare-fixtures.ps1 over synthetic Verify evidence; no compiler build or native toolchain is needed.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compare = Join-Path $repo 'scripts/compare-fixtures.ps1'
$work = Join-Path $repo ('temp/fixture-comparison/' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$cases = 0
$passing = [ordered]@{ status = 'passed'; levels = @('O0', 'O2') }
function Put([string] $path, [string] $text) {
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $text, $utf8)
}
function Expect([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:cases++
}
# Each fixture records a source location and returns its value; runs default to passing O0 and O2 with output "ok".
function New-Run([string] $name, [hashtable] $returns, [hashtable] $native = @{}, [hashtable] $stdout = @{}, [string] $location = 'a.kimi') {
    $directory = Join-Path $work $name
    $summary = [ordered]@{ configuration = 'Release'; pipeline = 'legacy'; head = $name; workDirectory = (Join-Path $directory 'work'); steps = @([ordered]@{ step = 'tests'; result = 'PASS' }) }
    Put (Join-Path $directory 'summary.json') ($summary | ConvertTo-Json -Depth 3)
    $records = foreach ($stem in @($returns.Keys | Sort-Object)) {
        Put (Join-Path $directory "fixtures-Release/$stem.ll") "; $location`ndefine i32 @main() {`n  ret i32 $($returns[$stem])`n}`n"
        Put (Join-Path $directory "fixtures-Release/$stem.stdout") $(if ($stdout.ContainsKey($stem)) { $stdout[$stem] } else { "ok`n" })
        foreach ($kind in @('stderr', 'exit', 'observation')) {
            Put (Join-Path $directory "fixtures-Release/$stem.$kind") $(if ($kind -eq 'stderr') { '' } else { '0' })
        }
        $outcome = if ($native.ContainsKey($stem)) { $native[$stem] } else { $passing }
        [ordered]@{ fixture = "$stem.ll"; status = $outcome.status; levels = $outcome.levels; seconds = 0.1; log = "X:\elsewhere\native-fixtures\$stem.log" }
    }
    Put (Join-Path $directory 'native-fixtures/results.json') (ConvertTo-Json -InputObject @($records) -Depth 4)
}
function Compare-Run([string] $baseline, [string] $candidate, [int] $exitCode, [switch] $DefaultOutput, [switch] $O2Metrics) {
    $output = if ($DefaultOutput) { Join-Path $work $candidate } else { Join-Path $work "compare-$baseline-$candidate" }
    $arguments = @{ Baseline = (Join-Path $work $baseline); Candidate = (Join-Path $work $candidate) }
    if (-not $DefaultOutput) { $arguments.Output = $output }
    $global:LASTEXITCODE = -1
    & $compare @arguments -O2Metrics:$O2Metrics *> (Join-Path $work "compare-$baseline-$candidate.log")
    Expect ($LASTEXITCODE -eq $exitCode) "Comparing $baseline with $candidate must exit $exitCode, not $LASTEXITCODE."
    Expect (Test-Path -LiteralPath (Join-Path $output 'fixture-comparison.txt')) "Comparing $baseline with $candidate must write the text report."
    Expect ($O2Metrics.IsPresent -eq (Test-Path -LiteralPath (Join-Path $output 'o2-metrics.json'))) "Only -O2Metrics writes o2-metrics.json ($baseline, $candidate)."
    return Get-Content -LiteralPath (Join-Path $output $(if ($O2Metrics) { 'o2-metrics.json' } else { 'fixture-comparison.json' })) -Raw | ConvertFrom-Json
}
# Optimized IR as LLVM prints it: a define of one unlabeled entry block of that many instructions.
function Define([string] $name, [int] $size) { "define void @$name() {`n$("  %v = add i32 0, 0`n" * ($size - 1))  ret void`n}`n`n" }
function Put-O2([string] $run, [string] $stem, [string] $text, [string] $step = 'native') {
    Put (Join-Path $work "$run/work/$step/$stem.O2.ll") ("; ModuleID = '$stem'`n`n" + $text)
}
function Reject([string] $candidate, [string] $message, [string] $baseline = 'base', [switch] $O2Metrics) {
    try { & $compare -Baseline (Join-Path $work $baseline) -Candidate (Join-Path $work $candidate) -Output (Join-Path $work "compare-$candidate") -O2Metrics:$O2Metrics *> $null }
    catch {
        Expect ($_.Exception.Message -like "*$message*") "Unexpected rejection of ${candidate}: $($_.Exception.Message)"
        return
    }
    throw "Comparing base with $candidate must be rejected."
}
function Only($result, [string] $kind, [string] $names) {
    $lists = [ordered]@{ differing = @($result.files.differing); missing = @($result.files.missing); added = @($result.files.added); native = @($result.native.differing.fixture) }
    foreach ($list in $lists.Keys) { Expect (($lists[$list] -join ',') -ceq $(if ($list -eq $kind) { $names } else { '' })) "Expected only $kind '$names', got $list '$($lists[$list] -join ',')'." }
}
$null = New-Item -ItemType Directory -Force $work
foreach ($name in @('base', 'same', 'added', 'sidecar', 'failed', 'unrecorded')) { New-Run $name @{ one = 0; two = 0 } }
# A recorded checkout path is not normalized, and a NUL byte, which PowerShell's cultural -ceq ignores, still differs.
New-Run 'path' @{ one = 0; two = 0 } -location 'C:\work\a.kimi'
New-Run 'byte' @{ one = 1; two = "0`0" }
New-Run 'set' @{ one = 0; three = 0 }
New-Run 'result' @{ one = 0; two = 0 } @{ two = [ordered]@{ status = 'failed'; levels = @('O0') } }
# Every fixture output is compared, not only the .ll files and the sidecars the native runner reads.
Put (Join-Path $work 'added/fixtures-Release/one.matrix.tsv') "row`n"
Put (Join-Path $work 'sidecar/fixtures-Release/one.stdout') "changed`n"
Put (Join-Path $work 'failed/summary.json') '{"configuration":"Release","steps":[{"step":"tests","result":"PASS"},{"step":"native","result":"FAIL"}]}'
Remove-Item -LiteralPath (Join-Path $work 'unrecorded/native-fixtures/results.json')

$same = Compare-Run 'base' 'same' 0 -DefaultOutput
Expect ($same.identical -and $same.files.identical -eq 10 -and $same.files.irIdentical -eq 2 -and $same.native.equal -eq 2) 'Equal runs must compare identical.'
Only (Compare-Run 'base' 'path' 1) 'differing' 'one.ll,two.ll'
$byte = Compare-Run 'base' 'byte' 1
Only $byte 'differing' 'one.ll,two.ll'
Expect ($byte.files.ir -eq 2 -and $byte.files.irIdentical -eq 0) 'Differing IR must be counted.'
Only (Compare-Run 'base' 'added' 1) 'added' 'one.matrix.tsv'
Only (Compare-Run 'added' 'base' 1) 'missing' 'one.matrix.tsv'
Only (Compare-Run 'base' 'sidecar' 1) 'differing' 'one.stdout'
$set = Compare-Run 'base' 'set' 1
Expect (@($set.files.missing) -contains 'two.ll' -and @($set.files.added) -contains 'three.ll' -and $set.files.irIdentical -eq 1) 'Missing and added fixtures must be reported.'
Expect ((@($set.native.differing.fixture) -join ',') -ceq 'three.ll,two.ll') 'A fixture run natively by only one run must be reported.'
$result = Compare-Run 'base' 'result' 1
Only $result 'native' 'two.ll'
$native = @($result.native.differing)[0]
Expect ($native.baseline -ceq 'PASS O0 O2' -and $native.candidate -ceq 'FAIL O0' -and ($native.candidateLogs -join ',') -ceq 'native-fixtures/two.log') 'A native difference must name both outcomes and the run-relative log.'
Expect ((Get-Content -LiteralPath (Join-Path $work 'compare-base-result/fixture-comparison.txt')) -contains 'Result: DIFFERENT') 'The text report must state the result.'
Reject 'failed' 'not a successful Verify run'
Reject 'unrecorded' 'has no results.json'
Remove-Item -LiteralPath (Join-Path $work 'set/fixtures-Release') -Recurse
Reject 'set' 'has no fixtures-Release evidence'


# Only this historical reader accepts archived ok records. Incomplete/unknown current results are never equal evidence.
New-Run 'historical' @{ one = 0; two = 0 }
$path = Join-Path $work 'historical/native-fixtures/results.json'
$records = @(Get-Content $path -Raw | ConvertFrom-Json)
foreach ($record in $records) {
    $record.PSObject.Properties.Remove('status')
    $record | Add-Member -NotePropertyName ok -NotePropertyValue $true
}
Put $path (ConvertTo-Json -InputObject $records)
Expect (Compare-Run 'base' 'historical' 0).identical 'Historical ok and current passed outcomes must compare identically.'
New-Run 'incomplete' @{ one = 0; two = 0 } @{ two = @{ status = 'incomplete'; levels = @('O0') } }
Reject 'incomplete' 'incomplete native observation'
Reject 'incomplete' 'incomplete native observation' -baseline 'incomplete'
New-Run 'unknown' @{ one = 0; two = 0 } @{ two = @{ status = 'other'; levels = @('O0', 'O2') } }
Reject 'unknown' 'unknown native status'

# O2 metrics: four.ll fails before O2 in the candidate and five.ll is the candidate's only, so each lacks one side.
New-Run 'o2base' @{ one = 0; two = 0; three = 0; four = 0; six = 0 }
New-Run 'o2head' @{ one = 0; two = 0; three = 0; four = 0; five = 0; six = 0 } @{ four = [ordered]@{ status = 'failed'; levels = @('O0') } }
New-Run 'o2empty' @{ one = 0 }
# Two instructions in an unlabeled entry block; four (not a switch case or its ']') in three labeled blocks.
$sample = "define void @__kimi_start() #1 {`n  tail call void @__kimi_f1(i32 1)`n  ret void`n}`n`n; Function Attrs: uwtable`n" +
    "define internal fastcc i32 @__kimi_f1(i32 %x) {`nentry:`n  %a = add i32 %x, 1`n  switch i32 %a, label %b [`n    i32 0, label %5`n  ]`n`n" +
    "b:                                                ; preds = %entry`n  ret i32 %a`n`n5:`n  ret i32 0`n}`n`ndeclare void @g()`n`n"
Put-O2 'o2base' 'one' $sample
Put-O2 'o2head' 'one' ($sample + "define i32 @__kimi_f2() {`n  %r = call i32 @h()`n  br label %1`n`n1:`n  ret i32 %r`n}`n")
foreach ($stem in @('two', 'three', 'four')) { Put-O2 'o2base' $stem (Define 'f' $(if ($stem -eq 'two') { 4 } else { 2 })) }
Put-O2 'o2head' 'two' ((Define 'f' 4) + (Define 'g' 4))
Put-O2 'o2head' 'three' (Define 'f' 4)
Put-O2 'o2head' 'five' (Define 'f' 1)
# six.ll's runtime helpers (one a template define, one a spliced marker) are unchanged and its body doubles: it ranks by
# its whole module (1.0769), below one.ll (1.5), although its body grows more (2); three.ll (2, +2) ranks after two.ll
# (2, +4) by the difference.
Put-O2 'o2base' 'six' ((Define '__kimi_write_bytes' 15) + (Define '__kimi_exit' 9) + (Define '__kimi_start' 2))
Put-O2 'o2head' 'six' ((Define '__kimi_write_bytes' 15) + (Define '__kimi_exit' 9) + (Define '__kimi_start' 4))
Put-O2 'o2empty' 'one' (Define '__kimi_exit' 2)
$o2 = Compare-Run 'o2base' 'o2head' 1 -O2Metrics
Expect ((@($o2.ranked.fixture) -join ',') -ceq 'two.ll,three.ll,one.ll,six.ll') 'O2 metrics must rank by whole-module instruction growth, then by the difference, then by name.'
$one = @($o2.ranked)[2]
$defines = @($one.candidate.defines | ForEach-Object { "$($_.name)=$($_.instructions)/$($_.blocks)" }) -join ','
Expect ($defines -ceq '__kimi_start=2/1,__kimi_f1=4/3,__kimi_f2=3/2') "Each define must count its instructions and blocks, not '$defines'."
Expect ($one.growth -eq 1.5 -and $one.difference -eq 3 -and $one.baseline.instructions -eq 6 -and $one.baseline.blocks -eq 4) 'A fixture must total its defines.'
$six = @($o2.ranked)[3]
Expect ($six.growth -eq 1.0769 -and $six.difference -eq 2 -and $six.baseline.runtime.instructions -eq 24 -and $six.candidate.bodies.instructions -eq 4 -and $six.candidate.instructions -eq 28) 'Runtime helpers and bodies must be totalled apart, the growth by the whole module.'
Expect (@($o2.runtime) -ccontains '__kimi_exit' -and @($o2.runtime) -ccontains '__kimi_write_bytes' -and @($o2.runtime) -cnotcontains '__kimi_start') 'The runtime helpers must be the runtime template functions.'
$totals = $o2.totals
Expect ($totals.fixtures -eq 4 -and $totals.baseline.bodies.instructions -eq 14 -and $totals.candidate.bodies.instructions -eq 25) 'O2 totals must sum the bodies of paired fixtures.'
Expect ($totals.baseline.runtime.instructions -eq 24 -and $totals.candidate.runtime.instructions -eq 24 -and $totals.baseline.instructions -eq 38 -and $totals.candidate.instructions -eq 49 -and $totals.growth -eq 1.2895) 'O2 totals must sum their runtime helpers and grow by the whole modules.'
$unpaired = @($o2.unpaired | ForEach-Object { "$($_.fixture):$([int]($null -ne $_.baseline))$([int]($null -ne $_.candidate))" }) -join ','
Expect ($unpaired -ceq 'five.ll:01,four.ll:10') "A fixture without an O2 file in one run must be reported, not '$unpaired'."
Expect ((Get-Content -LiteralPath (Join-Path $work 'compare-o2base-o2head/fixture-comparison.txt')) -contains '  four.ll: none in candidate') 'The text report must name an unpaired fixture.'
Expect ((@((Compare-Run 'o2base' 'o2base' 0 -O2Metrics).ranked.growth | Sort-Object -Unique) -join ',') -ceq '1') 'O2 metrics must not change the comparison result.'
Reject 'same' 'has no native work directory' -O2Metrics
Reject 'o2empty' 'has no generated function' 'o2base' -O2Metrics
# Two native steps: native-<i>-fixtures records native-<i>'s outputs, and a fixture takes its first step's O2 file.
New-Run 'o2steps' @{ one = 0; two = 0 }
Rename-Item -LiteralPath (Join-Path $work 'o2steps/native-fixtures') 'native-0-fixtures'
Copy-Item -LiteralPath (Join-Path $work 'o2steps/native-0-fixtures') (Join-Path $work 'o2steps/native-1-fixtures') -Recurse
Put-O2 'o2steps' 'one' (Define 'f' 3) 'native-0'
Put-O2 'o2steps' 'one' (Define 'f' 5) 'native-1'
Put-O2 'o2steps' 'two' (Define 'f' 7) 'native-1'
$steps = @((Compare-Run 'o2steps' 'o2steps' 0 -O2Metrics).ranked | ForEach-Object { "$($_.fixture)=$($_.baseline.instructions)" }) -join ','
Expect ($steps -ceq 'one.ll=3,two.ll=7') "Each native step must read its own work directory, the first step first, not '$steps'."
Write-Output "Passed $cases fixture comparison checks. Synthetic runs retained: $work"
