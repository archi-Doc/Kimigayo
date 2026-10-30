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
function Invoke-Case(
    [string] $name, [string] $selection, [int] $parallel, [bool] $expected, [int] $count, [int] $deadline
) {
    $fixtures = Split-Path -Path $selection -Parent
    $pattern = Split-Path -Path $selection -Leaf
    $logs = Join-Path $evidence $name
    $caught = $false
    try {
        & $runner -ToolchainRoot $ToolchainRoot -FixtureDirectory $fixtures -FixturePattern $pattern `
            -OutputDirectory (Join-Path $work $name) -LogDirectory $logs -Parallel $parallel `
            -ExecutionDeadlineMilliseconds $deadline *> (Join-Path $evidence "$name.log")
    }
    catch { $caught = $true; $_ | Out-String | Add-Content (Join-Path $evidence "$name.log") }
    if ($caught -eq $expected) { throw "Unexpected runner outcome: $name" }
    $records = @(Get-Content (Join-Path $logs 'results.json') -Raw | ConvertFrom-Json)
    if ($records.Count -ne $count) { throw "Missing fixture results: $name" }
    foreach ($record in $records) {
        if (-not (Test-Path -LiteralPath $record.log)) { throw "Missing fixture log: $name" }
        if ($record.status -ceq 'passed' -and ($record.levels -join ',') -cne 'O0,O2') { throw "Incomplete native coverage: $name" }
    }
    $results.Add(@{ name = $name; passed = $true; records = $records })
    return ,$records
}
$valid = Join-Path $work 'valid'
New-Fixture $valid 'one'
New-Fixture $valid 'two'
# Both execution modes must preserve UTF-8 stdout/stderr and a nonzero expected exit.
$serial = Invoke-Case 'serial' (Join-Path $valid '*.ll') 1 -expected $true -count 2 -deadline 30000
$parallel = Invoke-Case 'parallel' (Join-Path $valid '*.ll') 4 -expected $true -count 2 -deadline 30000
$null = Invoke-Case 'broad-pattern' (Join-Path $valid '*') 4 -expected $true -count 2 -deadline 30000
if (@($serial | Where-Object { $_.status -cne 'passed' }).Count -or @($parallel | Where-Object { $_.status -cne 'passed' }).Count) {
    throw 'Valid fixtures failed.'
}
$mixed = Join-Path $work 'mixed'
New-Fixture $mixed 'valid'
New-Fixture $mixed 'bad-output' -stdout 'wrong'
New-Fixture $mixed 'bad-error' -stderr 'wrong'
New-Fixture $mixed 'bad-exit' -exitCode 0
New-Fixture $mixed 'bad-ir' -source 'not LLVM IR'
New-Fixture $mixed 'invalid-utf8' -source ($ir.Replace('\E6', '\FF'))
New-Fixture $mixed 'missing-sidecar'
Remove-Item -LiteralPath (Join-Path $mixed 'missing-sidecar.stderr')
New-Fixture $mixed 'returned-divergence'
[IO.File]::WriteAllText((Join-Path $mixed 'returned-divergence.observation'), '100', $utf8)
$records = Invoke-Case 'mixed-failures' (Join-Path $mixed '*.ll') 4 -expected $false -count 8 -deadline 30000
if (@($records | Where-Object status -CEQ 'passed').Count -ne 1 -or ($records | Where-Object fixture -eq 'valid.ll').status -cne 'passed') {
    throw 'Failures were lost or affected an independent fixture.'
}
$returned = $records | Where-Object fixture -CEQ 'returned-divergence.ll'
if ($returned.status -cne 'failed' -or $returned.executions[0].result.termination -cne 'exited') { throw 'Unexpected natural exit was not a failure.' }
$invalid = $records | Where-Object fixture -CEQ 'invalid-utf8.ll'
if ($invalid.status -cne 'failed' -or $invalid.executions.Count -ne 1) { throw 'Invalid UTF-8 lost its execution or became incomplete.' }
$sample = $invalid.executions[0].result
if ($sample.violations.Count -ne 2 -or @($sample.violations | Where-Object kind -CNE 'invalidUtf8').Count -ne 0 -or
    $sample.problems.Count -ne 0 -or $null -ne $sample.unrecoveredProcessId) { throw 'Invalid UTF-8 capture/recovery facts were lost.' }
$badIr = $records | Where-Object fixture -CEQ 'bad-ir.ll'
if ($badIr.status -cne 'failed' -or $badIr.tools[0].exitCode -eq 0) { throw 'An observed LLVM failure became incomplete.' }
$missingInput = $records | Where-Object fixture -CEQ 'missing-sidecar.ll'
if ($missingInput.status -cne 'incomplete' -or $missingInput.problems.Count -ne 1 -or
    $missingInput.problems[0].operation -cne 'fixture.input') { throw 'Missing input lost its owning operation.' }
$divergent = Join-Path $work 'divergent' 
New-Fixture $divergent 'loop' -source "target triple = `"x86_64-pc-windows-msvc`"`ndefine void @__kimi_start() noreturn {`nentry:`n  br label %loop`nloop:`n  br label %loop`n}`n" -stdout '' -stderr '' -exitCode 0
[IO.File]::WriteAllText((Join-Path $divergent 'loop.observation'), '100', $utf8)
$records = Invoke-Case 'divergence' (Join-Path $divergent '*.ll') 2 -expected $true -count 1 -deadline 30000
foreach ($execution in $records[0].executions) {
    if ($execution.result.cpuMilliseconds -lt 100 -or $execution.result.termination -cne 'observed') { throw 'Empty output passed without CPU observation.' }
}

# A CPU budget that cannot fit the deadline produces incomplete for both serial and parallel workers.
$insufficient = Join-Path $work 'insufficient'
New-Fixture $insufficient 'empty' -source ([IO.File]::ReadAllText((Join-Path $divergent 'loop.ll'))) -stdout '' -stderr '' -exitCode 0
[IO.File]::WriteAllText((Join-Path $insufficient 'empty.observation'), '2147483647', $utf8)
foreach ($workers in @(1, 4)) {
    $records = Invoke-Case "incomplete-$workers" (Join-Path $insufficient '*.ll') $workers -expected $false -count 1 -deadline 50
    if ($records[0].status -cne 'incomplete' -or $records[0].levels.Count -ne 0) { throw 'Insufficient CPU observation passed or became a mismatch.' }
    $sample = $records[0].executions[0].result
    if ($sample.termination -cne 'deadline' -or $sample.cpuMilliseconds -ge $sample.observationMilliseconds) {
        throw 'Missing insufficient-observation evidence.'
    }
}
# Real sleeping native processes exercise scheduling delay without adding imports to the production profile.
. (Join-Path $repo 'src/backend/windows-x64/toolchain.ps1')
. (Join-Path $repo 'src/backend/windows-x64/test-scalar-process.ps1')
$tools = Resolve-KimiToolchainRoot $ToolchainRoot
$missing = Invoke-KimiFixtureProcess (Join-Path $work 'missing') @{
    stdout = ''; stderr = ''; exit = 0; observationMilliseconds = 0
} 30000
if ($missing.status -cne 'incomplete' -or $missing.termination -cne 'notStarted' -or
    $null -ne $missing.cpuMilliseconds -or $null -ne $missing.processId -or
    $missing.problems.Count -ne 1 -or $missing.problems[0].operation -cne 'launch') { throw 'Missing launch invented observation facts.' }
$results.Add(@{ name = 'missing-executable'; passed = $true; records = @($missing) })

# A real successful link followed by loss of our executable stays incomplete, never a known failure.
. (Join-Path $repo 'src/backend/windows-x64/kernel32.ps1')
$chainLogs = Join-Path $evidence 'launch-gap/native-0-fixtures'
$chainWork = Join-Path $work 'launch-gap'
$null = New-Item -ItemType Directory -Force $chainLogs, $chainWork
$wrapper = Join-Path $chainWork 'link-and-retain.ps1'
$wrapperSource = @'
$ErrorActionPreference = 'Stop'
& 'TOOLCHAIN/lld-link.exe' @args
if ($LASTEXITCODE -ne 0) { throw "Real linker failed: $LASTEXITCODE" }
$output = @($args | Where-Object { $_.StartsWith('/out:', [StringComparison]::OrdinalIgnoreCase) })
if ($output.Count -ne 1) { throw 'Expected one linker output.' }
$source = (Resolve-Path -LiteralPath $output[0].Substring(5)).Path
$root = (Resolve-Path -LiteralPath 'WORK').Path.TrimEnd('\') + '\'
$destination = [IO.Path]::GetFullPath($source + '.retained')
if (-not $source.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    -not $destination.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $destination)) { throw 'Generated executable escaped the isolated test or would be overwritten.' }
foreach ($path in @($source, $root)) {
    if (((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point rejected.' }
}
$hash = (Get-FileHash -LiteralPath $source).Hash
Move-Item -LiteralPath $source -Destination $destination
if ((Get-FileHash -LiteralPath $destination).Hash -cne $hash) { throw 'Retained executable changed.' }
'@
[IO.File]::WriteAllText($wrapper, $wrapperSource.Replace('TOOLCHAIN', $tools).Replace('WORK', $chainWork), $utf8)
$workerTools = @{}
foreach ($name in @('opt', 'llc', 'llvm-nm')) { $workerTools[$name] = Join-Path $tools "$name.exe" }
$workerTools['lld-link'] = $wrapper
$allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in (Read-KimiWindowsProfile).providedSymbols) { $null = $allowed.Add($symbol) }
foreach ($line in ((Get-KimiKernel32Definition) -split [char]10 | Select-Object -Skip 2)) {
    $symbol = $line.Trim()
    if ($symbol) { $null = $allowed.Add($symbol); $null = $allowed.Add("__imp_$symbol") }
}
$context = @{
    tools = $workerTools; fixtures = $valid; output = $chainWork; kernel = (Get-KimiInstalledKernel32 $tools)
    archive = Join-Path $tools 'windows_x64/kimi_backend_windows_x64_v1.lib'
    allowedSymbols = [string[]]@($allowed); logs = $chainLogs; executionDeadlineMilliseconds = 30000
}
$worker = Join-Path $repo 'src/backend/windows-x64/test-scalar-fixture.ps1'
$record = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($record.status -cne 'incomplete' -or $record.executions.Count -ne 1 -or
    $record.executions[0].result.problems[0].operation -cne 'launch') { throw 'A lost executable became a known failure or lost its execution.' }
$results.Add(@{ name = 'launch-gap'; passed = $true; records = @($record) })

# Failure to write tool evidence remains incomplete at the fixture boundary, never an input or LLVM failure.
$context.logs = Join-Path $evidence 'evidence-write'
$null = New-Item -ItemType Directory -Force $context.logs
$context.output = Join-Path $chainWork 'blocked-output'
$null = New-Item -ItemType Directory -Force (Join-Path $context.output 'one.O0.undefined.txt')
$blocked = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($blocked.status -cne 'incomplete' -or $blocked.problems.Count -ne 1 -or
    $blocked.problems[0].operation -cne 'fixture' -or $blocked.executions.Count -ne 0 -or
    @($blocked.tools | Where-Object exitCode -NE 0).Count -ne 0) { throw 'Evidence write failure was misclassified.' }
$results.Add(@{ name = 'evidence-write'; passed = $true; records = @($blocked) })

# A tool that never launches is incomplete; a successfully observed forbidden dependency is a definite failure.
$context.output = Join-Path $chainWork 'boundary-output'
$context.logs = Join-Path $evidence 'tool-launch'
$null = New-Item -ItemType Directory -Force $context.output, $context.logs
$context.tools.opt = Join-Path $chainWork 'missing-opt.exe'
$unlaunchedTool = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($unlaunchedTool.status -cne 'incomplete' -or $unlaunchedTool.problems[0].operation -cne 'tool.launch' -or
    $unlaunchedTool.tools.Count -ne 0) { throw 'An unlaunched LLVM tool became an observed nonzero exit.' }
$results.Add(@{ name = 'tool-launch'; passed = $true; records = @($unlaunchedTool) })
$context.tools.opt = Join-Path $tools 'opt.exe'
$context.allowedSymbols = [string[]]@()
$context.logs = Join-Path $evidence 'dependency'
$null = New-Item -ItemType Directory -Force $context.logs
$dependency = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($dependency.status -cne 'failed' -or $dependency.violations[0].kind -cne 'dependency' -or
    $dependency.problems.Count -ne 0) { throw 'A forbidden dependency became an incomplete observation.' }
$results.Add(@{ name = 'dependency'; passed = $true; records = @($dependency) })

# An unwritable log destination aborts the step instead of publishing a failed fixture.
$context.logs = Join-Path $work 'evidence-file'
[IO.File]::WriteAllText($context.logs, 'not a directory', $utf8)
$rejected = $false
try { $null = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context }
catch { $rejected = $true }
if (-not $rejected) { throw 'Unavailable evidence became a completed fixture result.' }

$probe = Join-Path $work 'probe'
$definition = @('LIBRARY KERNEL32.dll', 'EXPORTS', 'Sleep', 'GetStdHandle', 'WriteFile', 'ExitProcess') -join [char]10
[IO.File]::WriteAllText("$probe.def", $definition, $utf8)
& (Join-Path $tools 'llvm-dlltool.exe') -m i386:x86-64 -d "$probe.def" -l "$probe.lib"
if ($LASTEXITCODE -ne 0) { throw 'Cannot build test-only imports.' }
$template = @'
target triple = "x86_64-pc-windows-msvc"
@text = private constant [6 x i8] c"begin\0A"
declare dllimport void @Sleep(i32)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
define void @__kimi_start() noreturn {
entry:
  call void @Sleep(i32 DELAY)
  %written = alloca i32
  %out = call ptr @GetStdHandle(i32 -11)
  %a = call i32 @WriteFile(ptr %out, ptr @text, i32 6, ptr %written, ptr null)
  br label %loop
loop:
  br label %loop
}
'@
foreach ($level in @('O0', 'O2')) {
    foreach ($delay in @(400, 60000)) {
        $target = "$probe-$delay-$level"
        [IO.File]::WriteAllText("$target.ll", $template.Replace('DELAY', "$delay"), $utf8)
        & (Join-Path $tools 'opt.exe') -S "-passes=default<$level>" "$target.ll" -o "$target.optimized.ll"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot optimize delayed probe.' }
        & (Join-Path $tools 'llc.exe') "-$level" -filetype=obj "$target.optimized.ll" -o "$target.obj"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot compile delayed probe.' }
        & (Join-Path $tools 'lld-link.exe') "$target.obj" "$probe.lib" /entry:__kimi_start /subsystem:console /nodefaultlib "/out:$target.exe"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot link delayed probe.' }
        $expectation = @{ stdout = 'begin' + [char]10; stderr = ''; exit = 0; observationMilliseconds = 100 }
        if ($delay -eq 400) {
            $sample = Invoke-KimiFixtureProcess $target $expectation 30000
            if ($sample.status -cne 'passed' -or $sample.termination -cne 'observed' -or $sample.cpuMilliseconds -lt 100) {
                throw 'Delay consumed the CPU observation budget.'
            }
            $expectation.stdout = 'wrong'
            $wrong = Invoke-KimiFixtureProcess $target $expectation 30000
            if ($wrong.status -cne 'failed') { throw 'Sufficient observation hid a mismatch.' }
        }
        else {
            foreach ($output in @(('begin' + [char]10), '')) {
                $expectation.stdout = $output
                $sample = Invoke-KimiFixtureProcess $target $expectation 100
                if ($sample.status -cne 'incomplete' -or $sample.termination -cne 'deadline') { throw 'Sleeping process became a success or mismatch.' }
                $results.Add(@{ name = "delayed-$level-$($output.Length)"; passed = $true; records = @($sample) })
            }
            $expectation.observationMilliseconds = 0
            $sample = Invoke-KimiFixtureProcess $target $expectation 100
            if ($sample.status -cne 'failed' -or $sample.termination -cne 'deadline') {
                throw 'Expected completion deadline was classified as divergent observation.'
            }
        }
        $results.Add(@{ name = "probe-$delay-$level"; passed = $true; records = @($sample) })
    }
}

$empty = Join-Path $work 'empty'
New-Item -ItemType Directory $empty | Out-Null
[IO.File]::WriteAllText((Join-Path $empty 'orphan.stdout'), 'sidecar only', $utf8)
$caught = $false
try { & $runner -ToolchainRoot $ToolchainRoot -FixtureDirectory $empty -FixturePattern '*' *> (Join-Path $evidence 'empty.log') }
catch { $caught = $true; $_ | Out-String | Add-Content (Join-Path $evidence 'empty.log') }
if (-not $caught) { throw 'An empty selection must fail.' }
$results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'summary.json')
Write-Output ("PASS native runner: serial/parallel UTF-8, exits, mixed failures, CPU observation, " +
    "delayed/insufficient execution, broad patterns and sidecar-only rejection; $evidence")
