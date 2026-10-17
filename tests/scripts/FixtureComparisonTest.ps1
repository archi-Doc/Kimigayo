# Regression checks for compare-fixtures.ps1 over synthetic Verify evidence; no compiler build or native toolchain is needed.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compare = Join-Path $repo 'scripts/compare-fixtures.ps1'
$work = Join-Path $repo ('temp/fixture-comparison/' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$cases = 0
$passing = [ordered]@{ ok = $true; levels = @('O0', 'O2') }
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
    Put (Join-Path $directory 'summary.json') ([ordered]@{ configuration = 'Release'; pipeline = 'legacy'; head = $name; steps = @([ordered]@{ step = 'tests'; result = 'PASS' }) } | ConvertTo-Json -Depth 3)
    $records = foreach ($stem in @($returns.Keys | Sort-Object)) {
        Put (Join-Path $directory "fixtures-Release/$stem.ll") "; $location`ndefine i32 @main() {`n  ret i32 $($returns[$stem])`n}`n"
        Put (Join-Path $directory "fixtures-Release/$stem.stdout") $(if ($stdout.ContainsKey($stem)) { $stdout[$stem] } else { "ok`n" })
        foreach ($kind in @('stderr', 'exit', 'timeout')) { Put (Join-Path $directory "fixtures-Release/$stem.$kind") $(if ($kind -eq 'stderr') { '' } else { '0' }) }
        $outcome = if ($native.ContainsKey($stem)) { $native[$stem] } else { $passing }
        [ordered]@{ fixture = "$stem.ll"; ok = $outcome.ok; levels = $outcome.levels; seconds = 0.1; log = "X:\elsewhere\native-fixtures\$stem.log" }
    }
    Put (Join-Path $directory 'native-fixtures/results.json') (ConvertTo-Json -InputObject @($records) -Depth 4)
}
function Compare-Run([string] $baseline, [string] $candidate, [int] $exitCode, [switch] $DefaultOutput) {
    $output = if ($DefaultOutput) { Join-Path $work $candidate } else { Join-Path $work "compare-$baseline-$candidate" }
    $arguments = @{ Baseline = (Join-Path $work $baseline); Candidate = (Join-Path $work $candidate) }
    if (-not $DefaultOutput) { $arguments.Output = $output }
    $global:LASTEXITCODE = -1
    & $compare @arguments *> (Join-Path $work "compare-$baseline-$candidate.log")
    Expect ($LASTEXITCODE -eq $exitCode) "Comparing $baseline with $candidate must exit $exitCode, not $LASTEXITCODE."
    Expect (Test-Path -LiteralPath (Join-Path $output 'fixture-comparison.txt')) "Comparing $baseline with $candidate must write the text report."
    return Get-Content -LiteralPath (Join-Path $output 'fixture-comparison.json') -Raw | ConvertFrom-Json
}
function Reject([string] $candidate, [string] $message) {
    try { & $compare -Baseline (Join-Path $work 'base') -Candidate (Join-Path $work $candidate) -Output (Join-Path $work "compare-$candidate") *> $null }
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
New-Run 'result' @{ one = 0; two = 0 } @{ two = [ordered]@{ ok = $false; levels = @('O0') } }
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
Write-Output "Passed $cases fixture comparison checks. Synthetic runs retained: $work"
