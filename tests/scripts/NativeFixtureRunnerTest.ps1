# Integration regression for bounded native workers using the installed LLVM toolchain.
[CmdletBinding()]
param([string] $ToolchainRoot = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff')
$evidence = Join-Path $repo "artifacts/verify/$stamp-native-runner"
$work = Join-Path $repo "temp/native-runner/$stamp"
New-Item -ItemType Directory -Force $evidence, $work | Out-Null
$runner = Join-Path $repo 'src/backend/windows-x64/test-scalars.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)
$ir = @'
target triple = "x86_64-pc-windows-msvc"
@text = private constant [4 x i8] c"\E6\97\A5\0A"
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport void @ExitProcess(i32) noreturn
define void @__kimi_start() noreturn {
  %written = alloca i32
  %out = call ptr @GetStdHandle(i32 -11)
  %a = call i32 @WriteFile(ptr %out, ptr @text, i32 4, ptr %written, ptr null)
  %err = call ptr @GetStdHandle(i32 -12)
  %b = call i32 @WriteFile(ptr %err, ptr @text, i32 4, ptr %written, ptr null)
  call void @ExitProcess(i32 7)
  unreachable
}
'@
$results = [Collections.Generic.List[object]]::new()
function New-Fixture([string] $directory, [string] $name, [string] $source = $ir, [string] $stdout = "日`n", [string] $stderr = "日`n", [int] $exitCode = 7) {
    New-Item -ItemType Directory -Force $directory | Out-Null
    foreach ($item in @{ ll = $source; stdout = $stdout; stderr = $stderr; exit = "$exitCode" }.GetEnumerator()) {
        [IO.File]::WriteAllText((Join-Path $directory "$name.$($item.Key)"), $item.Value, $utf8)
    }
}
function Invoke-Case([string] $name, [string] $fixtures, [int] $parallel, [bool] $expected, [int] $count) {
    $logs = Join-Path $evidence $name
    $caught = $false
    try {
        & $runner -ToolchainRoot $ToolchainRoot -FixtureDirectory $fixtures -OutputDirectory (Join-Path $work $name) -LogDirectory $logs -Parallel $parallel *> (Join-Path $evidence "$name.log")
    }
    catch { $caught = $true; $_ | Out-String | Add-Content (Join-Path $evidence "$name.log") }
    if ($caught -eq $expected) { throw "Unexpected runner outcome: $name" }
    $records = @(Get-Content (Join-Path $logs 'results.json') -Raw | ConvertFrom-Json)
    if ($records.Count -ne $count) { throw "Missing fixture results: $name" }
    foreach ($record in $records) {
        if (-not (Test-Path -LiteralPath $record.log)) { throw "Missing fixture log: $name" }
        if ($record.ok -and ($record.levels -join ',') -cne 'O0,O2') { throw "Incomplete native coverage: $name" }
    }
    $results.Add(@{ name = $name; passed = $true; records = $records })
    return ,$records
}
$valid = Join-Path $work 'valid'
New-Fixture $valid 'one'
New-Fixture $valid 'two'
# Both execution modes must preserve UTF-8 stdout/stderr and a nonzero expected exit.
$serial = Invoke-Case 'serial' $valid 1 $true 2
$parallel = Invoke-Case 'parallel' $valid 4 $true 2
if (@($serial | Where-Object { -not $_.ok }).Count -or @($parallel | Where-Object { -not $_.ok }).Count) { throw 'Valid fixtures failed.' }
$mixed = Join-Path $work 'mixed'
New-Fixture $mixed 'valid'
New-Fixture $mixed 'bad-output' -stdout 'wrong'
New-Fixture $mixed 'bad-error' -stderr 'wrong'
New-Fixture $mixed 'bad-exit' -exitCode 0
New-Fixture $mixed 'bad-ir' -source 'not LLVM IR'
New-Fixture $mixed 'missing-sidecar'
Remove-Item -LiteralPath (Join-Path $mixed 'missing-sidecar.stderr')
New-Fixture $mixed 'returned-divergence'
[IO.File]::WriteAllText((Join-Path $mixed 'returned-divergence.timeout'), '100', $utf8)
$records = Invoke-Case 'mixed-failures' $mixed 4 $false 7
if (@($records | Where-Object ok).Count -ne 1 -or -not ($records | Where-Object fixture -eq 'valid.ll').ok) { throw 'Failures were lost or affected an independent fixture.' }
$divergent = Join-Path $work 'divergent'
New-Fixture $divergent 'loop' -source "target triple = `"x86_64-pc-windows-msvc`"`ndefine void @__kimi_start() noreturn {`nentry:`n  br label %loop`nloop:`n  br label %loop`n}`n" -stdout '' -stderr '' -exitCode 0
[IO.File]::WriteAllText((Join-Path $divergent 'loop.timeout'), '100', $utf8)
$null = Invoke-Case 'divergence' $divergent 2 $true 1
$empty = Join-Path $work 'empty'
New-Item -ItemType Directory $empty | Out-Null
$caught = $false
try { & $runner -ToolchainRoot $ToolchainRoot -FixtureDirectory $empty *> (Join-Path $evidence 'empty.log') }
catch { $caught = $true; $_ | Out-String | Add-Content (Join-Path $evidence 'empty.log') }
if (-not $caught) { throw 'An empty selection must fail.' }
$results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'summary.json')
Write-Output "PASS native runner: serial/parallel UTF-8, exits, mixed failures, divergence and empty selection; $evidence"
