# The native evidence predicates extracted from verify.ps1; no compiler or toolchain is needed.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'scripts/verification-native.ps1')
$work = Join-Path $repo ('temp/verification-native/' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($work)
$log = Join-Path $work 'native.log'
$records = Join-Path $work 'results.json'
$utf8 = [Text.UTF8Encoding]::new($false)
$cases = 0
function Expect([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
    $script:cases++
}
function Reject([scriptblock] $action) {
    $caught = $false
    try { & $action }
    catch { $caught = $true }
    Expect $caught 'Malformed or unavailable evidence must retain its rejection.'
}
foreach ($text in @('', 'failed', 'Passed 0 native scalar executions (O0/O2).')) {
    [IO.File]::WriteAllText($log, $text, $utf8)
    $result = Get-KimiNativeLogResult $log
    Expect (-not $result.hasSuccessLine) 'Missing or zero success line became successful.'
    Expect ($result.detail -ceq "see log; $log") 'Missing success detail changed.'
}
[IO.File]::WriteAllText($log, "Passed 2 native scalar executions (O0/O2)." + [char]10 + "  Passed 4 native scalar executions (O0/O2).  " + [char]10, $utf8)
$result = Get-KimiNativeLogResult $log
Expect $result.hasSuccessLine 'The success line was lost.'
Expect ($result.detail -ceq "Passed 4 native scalar executions (O0/O2).; $log") 'The final trimmed success line must be used.'
Reject { Get-KimiNativeLogResult (Join-Path $work 'missing.log') }
$result = Get-KimiNativePendingResult $records 2 $log
Expect (-not $result.passed) 'Missing pending evidence passed.'
Expect ($result.detail -ceq "results for 0 of 2 fixtures, judged by codegen pending; $log") 'Missing pending detail changed.'
[IO.File]::WriteAllText($records, '[]', $utf8)
$result = Get-KimiNativePendingResult $records 0 $log
Expect (-not $result.passed) 'An empty selection must not pass.'
Expect ($result.detail -ceq "results for 0 of 0 fixtures, judged by codegen pending; $log") 'Empty pending detail changed.'
[IO.File]::WriteAllText($records, '[{"fixture":"a.ll","ok":false,"levels":[]},{"fixture":"b.ll","ok":true,"levels":["O0","O2"]}]', $utf8)
foreach ($selected in @(1, 2, 3)) {
    $result = Get-KimiNativePendingResult $records $selected $log
    Expect ($result.passed -eq ($selected -eq 2)) 'Pending coverage must require the selected count.'
    Expect ($result.detail -ceq "results for 2 of $selected fixtures, judged by codegen pending; $log") 'Pending coverage detail changed.'
}
[IO.File]::WriteAllText($records, '{invalid JSON', $utf8)
Reject { Get-KimiNativePendingResult $records 1 $log }
Write-Output "PASS native evidence evaluation: $cases assertions; $work"
