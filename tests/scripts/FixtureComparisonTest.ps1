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
    Put (Join-Path $directory 'summary.json') ([ordered]@{ configuration = 'Release'; pipeline = 'legacy'; head = $name } | ConvertTo-Json)
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
$null = New-Item -ItemType Directory -Force $work
New-Run 'base' @{ one = 0; two = 0 }
New-Run 'same' @{ one = 0; two = 0 }
# A recorded checkout path is not normalized, and a NUL byte, which PowerShell's cultural -ceq ignores, still differs.
New-Run 'path' @{ one = 0; two = 0 } -location 'C:\work\a.kimi'
New-Run 'byte' @{ one = 1; two = "0`0" }
New-Run 'set' @{ one = 0; three = 0 }
New-Run 'result' @{ one = 0; two = 0 } @{ two = [ordered]@{ ok = $false; levels = @('O0') } } @{ one = "changed`n" }
Remove-Item -LiteralPath (Join-Path $work 'result/fixtures-Release/two.timeout')

$same = Compare-Run 'base' 'same' 0 -DefaultOutput
Expect ($same.identical -and $same.ir.identical -eq 2 -and $same.native.equal -eq 2) 'Equal runs must compare identical.'
$path = Compare-Run 'base' 'path' 1
Expect (($path.ir.differing -join ',') -ceq 'one.ll,two.ll') 'A recorded checkout path must be a difference.'
$byte = Compare-Run 'base' 'byte' 1
Expect (-not $byte.identical -and ($byte.ir.differing -join ',') -ceq 'one.ll,two.ll' -and $byte.ir.identical -eq 0) 'A byte difference must be reported for its fixture.'
$set = Compare-Run 'base' 'set' 1
Expect (($set.ir.missing -join ',') -ceq 'two.ll' -and ($set.ir.added -join ',') -ceq 'three.ll' -and $set.ir.identical -eq 1) 'Missing and added fixtures must be reported.'
Expect (($set.native.differing.fixture -join ',') -ceq 'three.ll,two.ll') 'A fixture run natively by only one run must be reported.'
$result = Compare-Run 'base' 'result' 1
Expect ($result.ir.identical -eq 2 -and ($result.sidecars.differing -join ',') -ceq 'one.stdout,two.timeout') 'Changed and one-sided sidecars must be reported.'
$native = @($result.native.differing)
Expect ($native.Count -eq 1 -and $native[0].fixture -ceq 'two.ll' -and $native[0].baseline -ceq 'PASS O0 O2' -and $native[0].candidate -ceq 'FAIL O0') 'A native result difference must be reported with both outcomes.'
Expect (($native[0].candidateLogs -join ',') -ceq 'native-fixtures/two.log') 'A native difference must name the run-relative log.'
Expect ((Get-Content -LiteralPath (Join-Path $work 'compare-base-result/fixture-comparison.txt')) -contains 'Result: DIFFERENT') 'The text report must state the result.'
Remove-Item -LiteralPath (Join-Path $work 'set/fixtures-Release') -Recurse
try { & $compare -Baseline (Join-Path $work 'base') -Candidate (Join-Path $work 'set') -Output (Join-Path $work 'compare-missing') *> $null; throw 'A run without fixture evidence must be rejected.' }
catch { Expect ($_.Exception.Message -like '*has no fixtures-Release evidence*') "Unexpected rejection: $($_.Exception.Message)" }
Write-Output "Passed $cases fixture comparison checks. Synthetic runs retained: $work"
