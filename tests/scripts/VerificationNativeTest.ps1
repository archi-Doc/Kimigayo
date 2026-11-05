# The native evidence predicates extracted from verify.ps1; no compiler or toolchain is needed.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'scripts/verification-native.ps1')
$work = Join-Path $repo ('temp/verification-native/' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($work)
$log = Join-Path $work 'native.log'
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
Write-Output "PASS native evidence evaluation: $cases assertions; $work"
