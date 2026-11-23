# Integration regression for the native fixture runner: its kimi tatara builds (the configuration's Kimi.dll) and bounded run workers;
# the delayed-execution probes use the installed LLVM toolchain.
[CmdletBinding()]
param([string] $ToolchainRoot = '', [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release')
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
@_fltused = global i32 0
@text = private constant [4 x i8] c"\E6\97\A5\0A"
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport void @ExitProcess(i32) noreturn
define void @__kimi_start() noreturn uwtable {
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
    [string] $name, [string] $selection, [int] $parallel, [bool] $expected, [int] $count, [int] $deadline, [hashtable] $reuse = @{}
) {
    $fixtures = Split-Path -Path $selection -Parent
    $pattern = Split-Path -Path $selection -Leaf
    $logs = Join-Path $evidence $name
    $caught = $false
    try {
        & $runner -Configuration $Configuration -FixtureDirectory $fixtures -FixturePattern $pattern `
            -OutputDirectory (Join-Path $work $name) -LogDirectory $logs -Parallel $parallel `
            -ExecutionDeadlineMilliseconds $deadline @reuse *> (Join-Path $evidence "$name.log")
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
# A passed result is reused only while its environment key, fixture key and -ReuseHours freshness all hold.
$reuseFixtures = Join-Path $work 'reuse'
New-Fixture $reuseFixtures 'one'
New-Fixture $reuseFixtures 'two'
function Read-NativeResults([string] $name) { Get-Content (Join-Path $evidence "$name/native-results.json") -Raw | ConvertFrom-Json -DateKind String }
function Assert-Reuse([string] $name, [object[]] $records, [string[]] $reused) {
    foreach ($record in $records) {
        $isReused = $record.fixture -cin $reused
        if ([bool]$record.reused -ne $isReused -or ($isReused -and $record.builds.Count + $record.executions.Count -ne 0) -or
            (-not $isReused -and $record.builds.Count -eq 0)) { throw "Unexpected reuse of $($record.fixture): $name" }
    }
    $split = "$($records.Count - $reused.Count) fixtures run, $($reused.Count) reused"
    if (@($records | Where-Object status -CNE 'passed').Count -eq 0 -and
        -not (Select-String -LiteralPath (Join-Path $evidence "$name.log") -SimpleMatch "Passed $(2 * $records.Count) native scalar executions (O0/O2); $split")) {
        throw "Missing reuse split: $name"
    }
}
$first = Invoke-Case 'reuse-first' (Join-Path $reuseFixtures '*.ll') 2 -expected $true -count 2 -deadline 30000
$stored = Read-NativeResults 'reuse-first'
if ($stored.environmentKey -cnotmatch '^[0-9a-f]{64}$' -or $stored.fixtures.Count -ne 2 -or
    @($stored.fixtures | Where-Object { $_.key -cnotmatch '^[0-9a-f]{64}$' -or $_.status -cne 'passed' -or -not (Test-Path -LiteralPath $_.log) -or
        ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($_.verifiedAt)).TotalMinutes -gt 10 }).Count) {
    throw 'The first run did not record keyed, verified results.'
}
Assert-Reuse 'reuse-first' $first @()
$firstResults = Join-Path $evidence 'reuse-first/native-results.json'
$records = Invoke-Case 'reuse-all' (Join-Path $reuseFixtures '*.ll') 2 -expected $true -count 2 -deadline 30000 -reuse @{ ReuseResults = $firstResults }
Assert-Reuse 'reuse-all' $records @('one.ll', 'two.ll')
foreach ($entry in (Read-NativeResults 'reuse-all').fixtures) {
    $origin = $stored.fixtures | Where-Object fixture -CEQ $entry.fixture
    if ($entry.verifiedAt -cne $origin.verifiedAt -or $entry.log -cne $origin.log -or $entry.key -cne $origin.key) { throw 'A reused entry lost its verification.' }
}
# A changed sidecar re-runs only its fixture; the failure is recorded and never reused afterwards.
[IO.File]::WriteAllText((Join-Path $reuseFixtures 'two.stdout'), 'wrong', $utf8)
$records = Invoke-Case 'reuse-changed' (Join-Path $reuseFixtures '*.ll') 2 -expected $false -count 2 -deadline 30000 -reuse @{ ReuseResults = $firstResults }
Assert-Reuse 'reuse-changed' $records @('one.ll')
if (($records | Where-Object fixture -CEQ 'two.ll').status -cne 'failed' -or
    ((Read-NativeResults 'reuse-changed').fixtures | Where-Object fixture -CEQ 'two.ll').status -cne 'failed') { throw 'A changed fixture was not re-run.' }
$records = Invoke-Case 'reuse-failed' (Join-Path $reuseFixtures '*.ll') 2 -expected $false -count 2 -deadline 30000 `
    -reuse @{ ReuseResults = (Join-Path $evidence 'reuse-changed/native-results.json') }
Assert-Reuse 'reuse-failed' $records @('one.ll')
# An entry older than -ReuseHours re-runs; a longer window reuses it.
[IO.File]::WriteAllText((Join-Path $reuseFixtures 'two.stdout'), "日`n", $utf8)
$expired = Read-NativeResults 'reuse-first'
($expired.fixtures | Where-Object fixture -CEQ 'one.ll').verifiedAt = [DateTimeOffset]::UtcNow.AddHours(-25).ToString('o')
$expiredResults = Join-Path $work 'expired-results.json'
$expired | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $expiredResults
$records = Invoke-Case 'reuse-expired' (Join-Path $reuseFixtures '*.ll') 2 -expected $true -count 2 -deadline 30000 -reuse @{ ReuseResults = $expiredResults }
Assert-Reuse 'reuse-expired' $records @('two.ll')
$records = Invoke-Case 'reuse-hours' (Join-Path $reuseFixtures '*.ll') 2 -expected $true -count 2 -deadline 30000 `
    -reuse @{ ReuseResults = $expiredResults; ReuseHours = 26 }
Assert-Reuse 'reuse-hours' $records @('one.ll', 'two.ll')
# Another environment (here the deadline) re-runs everything.
$records = Invoke-Case 'reuse-environment' (Join-Path $reuseFixtures '*.ll') 2 -expected $true -count 2 -deadline 29999 -reuse @{ ReuseResults = $firstResults }
Assert-Reuse 'reuse-environment' $records @()
$mixed = Join-Path $work 'mixed'
New-Fixture $mixed 'valid'
New-Fixture $mixed 'bad-output' -stdout 'wrong'
New-Fixture $mixed 'bad-error' -stderr 'wrong'
New-Fixture $mixed 'bad-exit' -exitCode 0
New-Fixture $mixed 'bad-ir' -source 'not LLVM IR'
# IR that parses but fails the verifier (a use its definition does not dominate): opt's own input verification rejects it.
New-Fixture $mixed 'unverified-ir' -source @'
target triple = "x86_64-pc-windows-msvc"
define void @__kimi_start() {
entry:
  br label %b
a:
  %x = add i32 1, 2
  br label %b
b:
  %y = add i32 %x, 1
  ret void
}
'@
New-Fixture $mixed 'invalid-utf8' -source ($ir.Replace('\E6', '\FF'))
New-Fixture $mixed 'missing-sidecar'
Remove-Item -LiteralPath (Join-Path $mixed 'missing-sidecar.stderr')
New-Fixture $mixed 'returned-divergence'
[IO.File]::WriteAllText((Join-Path $mixed 'returned-divergence.observation'), '100', $utf8)
# Output equals its expectation only byte for byte: a missing NUL, another normalization form or a BOM is a mismatch.
New-Fixture $mixed 'nul-output' -stdout "日`0`n"
New-Fixture $mixed 'normalized-error' -source ($ir.Replace('\E6\97\A5', 'e\CC\81')) -stdout "e$([char]0x301)`n" -stderr "$([char]0xE9)`n"
New-Fixture $mixed 'bom-output' -stdout "$([char]0xFEFF)日`n"
# A kernel32 import referenced in another form than its declaration (ExitProcess without dllimport): it links through kernel32.lib's
# thunk, so only the build's object inspection rejects it.
New-Fixture $mixed 'forbidden-dependency' -source ($ir.Replace('declare dllimport void @ExitProcess', 'declare void @ExitProcess'))
$records = Invoke-Case 'mixed-failures' (Join-Path $mixed '*.ll') 4 -expected $false -count 13 -deadline 30000
if (@($records | Where-Object status -CEQ 'passed').Count -ne 1 -or ($records | Where-Object fixture -eq 'valid.ll').status -cne 'passed') {
    throw 'Failures were lost or affected an independent fixture.'
}
foreach ($case in @(@('nul-output.ll', 'stdout'), @('normalized-error.ll', 'stderr'), @('bom-output.ll', 'stdout'))) {
    $violations = @(($records | Where-Object fixture -CEQ $case[0]).executions[0].result.violations)
    if ($violations.Count -ne 1 -or $violations[0].kind -cne 'output' -or $violations[0].stream -cne $case[1]) {
        throw "An output differing only by a NUL, normalization or BOM lost its mismatch: $($case[0])"
    }
}
$returned = $records | Where-Object fixture -CEQ 'returned-divergence.ll'
if ($returned.status -cne 'failed' -or $returned.executions[0].result.termination -cne 'exited') { throw 'Unexpected natural exit was not a failure.' }
$invalid = $records | Where-Object fixture -CEQ 'invalid-utf8.ll'
if ($invalid.status -cne 'failed' -or $invalid.executions.Count -ne 1) { throw 'Invalid UTF-8 lost its execution or became incomplete.' }
$sample = $invalid.executions[0].result
if ($sample.violations.Count -ne 2 -or @($sample.violations | Where-Object kind -CNE 'invalidUtf8').Count -ne 0 -or
    $sample.problems.Count -ne 0 -or $null -ne $sample.unrecoveredProcessId) { throw 'Invalid UTF-8 capture/recovery facts were lost.' }
$badIr = $records | Where-Object fixture -CEQ 'bad-ir.ll'
if ($badIr.status -cne 'failed' -or $badIr.builds[0].recordStatus -ceq 'linked') { throw 'An observed LLVM failure became incomplete.' }
$unverified = $records | Where-Object fixture -CEQ 'unverified-ir.ll'
if ($unverified.status -cne 'failed' -or $unverified.builds.Count -ne 1 -or $unverified.builds[0].recordStatus -ceq 'linked' -or
    $unverified.executions.Count -ne 0) { throw 'IR that fails only the verifier was not rejected by its first build.' }
$dependency = $records | Where-Object fixture -CEQ 'forbidden-dependency.ll'
if ($dependency.status -cne 'failed' -or $dependency.builds[0].recordStatus -ceq 'linked' -or $dependency.problems.Count -ne 0 -or
    -not (Select-String -LiteralPath $dependency.builds[0].log -SimpleMatch 'Unsupported actual object dependency: ExitProcess U')) {
    throw 'A forbidden dependency was linked, failed otherwise or became an incomplete observation.'
}
$missingInput = $records | Where-Object fixture -CEQ 'missing-sidecar.ll'
if ($missingInput.status -cne 'incomplete' -or $missingInput.problems.Count -ne 1 -or
    $missingInput.problems[0].operation -cne 'fixture.input') { throw 'Missing input lost its owning operation.' }
# A build that ends its session (an LLVM fatal error: x86 cannot select an AArch64 intrinsic, its result kept at O2) dealt ahead of
# another fixture in one lane: the lines after it go to a later round's session, where the other fixture is built and passes.
$rounds = Join-Path $work 'fatal'
New-Fixture $rounds 'cannot-select' -source @'
target triple = "x86_64-pc-windows-msvc"
@_fltused = global i32 0
@v = global <4 x i32> zeroinitializer
declare <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32>)
declare dllimport void @ExitProcess(i32) noreturn
define void @__kimi_start() noreturn uwtable {
  %x = load volatile <4 x i32>, ptr @v
  %y = call <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32> %x)
  store volatile <4 x i32> %y, ptr @v
  call void @ExitProcess(i32 7)
  unreachable
}
'@
New-Fixture $rounds 'valid'
$records = Invoke-Case 'rounds' (Join-Path $rounds '*.ll') 1 -expected $false -count 2 -deadline 30000
$fatal = $records | Where-Object fixture -CEQ 'cannot-select.ll'
$later = $records | Where-Object fixture -CEQ 'valid.ll'
if ($fatal.status -cne 'failed' -or $fatal.builds[0].log -cnotmatch 'session-0\.log$' -or $later.status -cne 'passed' -or
    @($later.builds | Where-Object log -CMatch 'session-0\.log$').Count -ne 0) { throw 'A session ended by its build lost the next round.' }
$divergent = Join-Path $work 'divergent'
New-Fixture $divergent 'loop' -source "target triple = `"x86_64-pc-windows-msvc`"`n@_fltused = global i32 0`ndefine void @__kimi_start() noreturn uwtable {`nentry:`n  br label %loop`nloop:`n  br label %loop`n}`n" -stdout '' -stderr '' -exitCode 0
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
# The output owner takes byte[] only: a numeric string, an object[] of bytes or $null is rejected, never converted.
foreach ($argument in @('65', [object[]]@(65), $null)) {
    $rejected = $false
    try { $null = Test-KimiExpectedOutput $argument ([byte[]]@(65)) }
    catch [ArgumentException] { $rejected = $true }
    if (-not $rejected) { throw "Test-KimiExpectedOutput converted a non-byte[] argument: $argument" }
}
$tools = Resolve-KimiToolchainRoot $ToolchainRoot
$missing = Invoke-KimiFixtureProcess (Join-Path $work 'missing') @{
    stdout = [byte[]]@(); stderr = [byte[]]@(); exit = 0; observationMilliseconds = 0
} 30000
if ($missing.status -cne 'incomplete' -or $missing.termination -cne 'notStarted' -or
    $null -ne $missing.cpuMilliseconds -or $null -ne $missing.processId -or
    $missing.problems.Count -ne 1 -or $missing.problems[0].operation -cne 'launch') { throw 'Missing launch invented observation facts.' }
$results.Add(@{ name = 'missing-executable'; passed = $true; records = @($missing) })

# A linked build whose executable is lost before its run stays incomplete, never a known failure: the serial case's pairs, the O0
# executable removed, run again by the worker.
function Get-Builds([string] $output, [string] $stem) {
    @{ "$stem.ll" = @(foreach ($level in @('O0', 'O2')) {
        $pair = if ($level -ceq 'O2') { Join-Path $output $stem } else { Join-Path $output "O0/$stem" }
        [pscustomobject]@{ level = $level; manifest = "$pair.link.json"; record = "$pair.link.build.json"; log = 'none' }
    }) }
}
$serialOutput = Join-Path $work 'serial'
$context = @{ fixtures = $valid; logs = (Join-Path $evidence 'launch-gap'); builds = (Get-Builds $serialOutput 'one'); executionDeadlineMilliseconds = 30000 }
$null = New-Item -ItemType Directory -Force $context.logs
Remove-Item -LiteralPath (Join-Path $serialOutput 'O0/one.O0.exe')
$worker = Join-Path $repo 'src/backend/windows-x64/test-scalar-fixture.ps1'
$record = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($record.status -cne 'incomplete' -or $record.executions.Count -ne 1 -or
    $record.executions[0].result.problems[0].operation -cne 'launch') { throw 'A lost executable became a known failure or lost its execution.' }
$results.Add(@{ name = 'launch-gap'; passed = $true; records = @($record) })

# A pair no session attempted (it has no record) is incomplete, never a failed build.
$context.logs = Join-Path $evidence 'unattempted'
$null = New-Item -ItemType Directory -Force $context.logs
$context.builds = Get-Builds (Join-Path $work 'unattempted') 'one'
$unattempted = & $worker (Get-Item -LiteralPath (Join-Path $valid 'one.ll')) $context
if ($unattempted.status -cne 'incomplete' -or $unattempted.problems[0].operation -cne 'build' -or $unattempted.builds.Count -ne 0) {
    throw 'An unattempted build became a known failure.'
}
$results.Add(@{ name = 'unattempted'; passed = $true; records = @($unattempted) })

# A non-ASCII output folder under a console input code page that is not UTF-8 (932, which a redirected input otherwise takes): the
# sessions' UTF-8 lines and the pairs' records name the pairs exactly.
$savedInput = [Console]::InputEncoding
try {
    [Console]::InputEncoding = [Text.Encoding]::GetEncoding(932)
    $null = Invoke-Case "path-$([char]0x51FA)$([char]0x529B)" (Join-Path $valid '*.ll') 2 -expected $true -count 2 -deadline 30000
}
finally { [Console]::InputEncoding = $savedInput }

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
        $expectation = @{ stdout = $utf8.GetBytes("begin`n"); stderr = [byte[]]@(); exit = 0; observationMilliseconds = 100 }
        if ($delay -eq 400) {
            $sample = Invoke-KimiFixtureProcess $target $expectation 30000
            if ($sample.status -cne 'passed' -or $sample.termination -cne 'observed' -or $sample.cpuMilliseconds -lt 100) {
                throw 'Delay consumed the CPU observation budget.'
            }
            $expectation.stdout = $utf8.GetBytes('wrong')
            $wrong = Invoke-KimiFixtureProcess $target $expectation 30000
            if ($wrong.status -cne 'failed') { throw 'Sufficient observation hid a mismatch.' }
        }
        else {
            foreach ($output in @(('begin' + [char]10), '')) {
                $expectation.stdout = $utf8.GetBytes($output)
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
try { & $runner -Configuration $Configuration -FixtureDirectory $empty -FixturePattern '*' *> (Join-Path $evidence 'empty.log') }
catch { $caught = $true; $_ | Out-String | Add-Content (Join-Path $evidence 'empty.log') }
if (-not $caught) { throw 'An empty selection must fail.' }
$results | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'summary.json')
Write-Output ("PASS native runner: serial/parallel UTF-8, exits, mixed failures, forbidden dependency, unattempted and lost builds, rounds, " +
    "non-ASCII paths, result reuse, CPU observation, delayed/insufficient execution, broad patterns and sidecar-only rejection; $evidence")
