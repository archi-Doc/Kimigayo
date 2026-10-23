# Native fixture execution owns launch, CPU observation, bounded recovery and captured output.
# Inputs: executable stem, stdout/stderr/exit/CPU oracle and a launch-to-stop wall deadline.
# Output: one final passed/failed/incomplete result with observed facts, violations and operational problems.
# Byte capture completes before strict UTF-8 decoding. Incomplete recovery/capture dominates a known violation.
# Recovery has one 5000 ms budget shared by kill, wait and both streams; unavailable measurements stay null.
function Add-KimiFixtureProblem($Result, [string] $Operation, [Exception] $Exception) {
    $cause = $Exception.GetBaseException()
    $Result.problems.Add([pscustomobject]@{ operation = $Operation; type = $cause.GetType().FullName; message = $cause.Message })
}

function Start-KimiFixtureCapture(
    [Diagnostics.Process] $Process, [Threading.CancellationToken] $Cancellation, [Collections.Generic.List[object]] $Captures
) {
    foreach ($name in @('stdout', 'stderr')) {
        $stream = if ($name -ceq 'stdout') { $Process.StandardOutput.BaseStream } else { $Process.StandardError.BaseStream }
        $buffer = [IO.MemoryStream]::new()
        $capture = [pscustomobject]@{ name = $name; stream = $stream; buffer = $buffer; task = $null }
        $Captures.Add($capture)
        $capture.task = $stream.CopyToAsync($buffer, 81920, $Cancellation)
    }
}

function Read-KimiFixtureObservation([Diagnostics.Process] $Process, $Result, [Diagnostics.Stopwatch] $Timer) {
    try {
        while (-not $Process.HasExited) {
            $Process.Refresh()
            $Result.cpuMilliseconds = $Process.TotalProcessorTime.TotalMilliseconds
            if ($Process.HasExited) { break }
            if ($Timer.Elapsed.TotalMilliseconds -ge $Result.deadlineMilliseconds) { $Result.termination = 'deadline'; return }
            if ($Result.observationMilliseconds -gt 0 -and $Result.cpuMilliseconds -ge $Result.observationMilliseconds) {
                $Result.termination = 'observed'
                return
            }
            $null = $Process.WaitForExit(10)
        }
        $Result.termination = 'exited'
        $Result.cpuMilliseconds = $Process.TotalProcessorTime.TotalMilliseconds
    }
    catch { Add-KimiFixtureProblem $Result 'observation' $_.Exception }
}

function Read-KimiFixtureOutput($Capture, $Result, [Diagnostics.Stopwatch] $Recovery) {
    try {
        $remaining = [Math]::Max(0, $Result.recoveryBudgetMilliseconds - [int]$Recovery.ElapsedMilliseconds)
        $null = $Capture.task.WaitAsync([TimeSpan]::FromMilliseconds($remaining)).GetAwaiter().GetResult()
        $bytes = $Capture.buffer.ToArray()
        try { $Result[$Capture.name] = [Text.UTF8Encoding]::new($false, $true).GetString($bytes) }
        catch [Text.DecoderFallbackException] {
            $Result.violations.Add([pscustomobject]@{
                kind = 'invalidUtf8'; stream = $Capture.name; bytes = [Convert]::ToBase64String($bytes)
            })
        }
    }
    catch { Add-KimiFixtureProblem $Result ("capture." + $Capture.name) $_.Exception }
}

function Complete-KimiFixtureProcess(
    [Diagnostics.Process] $Process, [object[]] $Captures, $Result, [Threading.CancellationTokenSource] $Cancellation
) {
    $recovery = [Diagnostics.Stopwatch]::StartNew()
    try {
        try {
            if (-not $Process.HasExited) {
                if ($Result.termination -ceq 'exited') { throw 'An exited native process became live.' }
                $Process.Kill($true)
            }
            elseif ($Result.termination -ceq 'observed') { $Result.termination = 'exited' }
            $remaining = [Math]::Max(0, $Result.recoveryBudgetMilliseconds - [int]$recovery.ElapsedMilliseconds)
            if (-not $Process.WaitForExit($remaining)) { throw [TimeoutException]::new('Native process recovery exceeded its grace.') }
            $Result.exitCode = $Process.ExitCode
            $Result.unrecoveredProcessId = $null
        }
        catch { Add-KimiFixtureProblem $Result 'recovery' $_.Exception }
        foreach ($capture in $Captures) { Read-KimiFixtureOutput $capture $Result $recovery }
    }
    finally {
        try { $Cancellation.Cancel() }
        catch { Add-KimiFixtureProblem $Result 'capture.cancel' $_.Exception }
        foreach ($capture in $Captures) {
            try {
                $capture.stream.Dispose()
                if ($null -eq $capture.task -or $capture.task.IsCompleted) { $capture.buffer.Dispose() }
            }
            catch { Add-KimiFixtureProblem $Result ("capture.dispose." + $capture.name) $_.Exception }
        }
        try { $Process.Dispose() }
        catch { Add-KimiFixtureProblem $Result 'process.dispose' $_.Exception }
        $Result.recoveryElapsedMilliseconds = [Math]::Round($recovery.Elapsed.TotalMilliseconds, 3)
        if ($recovery.ElapsedMilliseconds -ge $Result.recoveryBudgetMilliseconds) {
            Add-KimiFixtureProblem $Result 'recovery' ([TimeoutException]::new('The shared native recovery budget expired.'))
        }
    }
}

function Get-KimiFixtureProcessStatus($Result, [hashtable] $Expectation) {
    if ($Result.termination -ceq 'exited') {
        if ($Result.observationMilliseconds -gt 0) {
            $Result.violations.Add([pscustomobject]@{ kind = 'unexpectedExit'; actual = $Result.exitCode })
        }
        elseif ($null -ne $Result.exitCode -and $Result.exitCode -ne $Expectation.exit) {
            $Result.violations.Add([pscustomobject]@{ kind = 'exitCode'; expected = $Expectation.exit; actual = $Result.exitCode })
        }
    }
    elseif ($Result.termination -ceq 'deadline' -and $Result.observationMilliseconds -eq 0) {
        $Result.violations.Add([pscustomobject]@{ kind = 'completionDeadline'; deadlineMilliseconds = $Result.deadlineMilliseconds })
    }
    if ($Result.termination -cin @('exited', 'observed')) {
        foreach ($name in @('stdout', 'stderr')) {
            if ($null -ne $Result[$name] -and $Result[$name] -cne $Expectation[$name]) {
                $Result.violations.Add([pscustomobject]@{ kind = 'output'; stream = $name; expected = $Expectation[$name]; actual = $Result[$name] })
            }
        }
    }
    if ($Result.problems.Count -gt 0 -or $Result.termination -ceq 'notStarted' -or
        ($Result.termination -ceq 'deadline' -and $Result.observationMilliseconds -gt 0)) { return 'incomplete' }
    if ($Result.violations.Count -gt 0) { return 'failed' }
    'passed'
}

function Invoke-KimiFixtureProcess(
    [string] $Target, [hashtable] $Expectation,
    [ValidateRange(1, 2147483647)] [int] $ExecutionDeadlineMilliseconds
) {
    $result = [ordered]@{
        status = $null; termination = 'notStarted'; processId = $null; unrecoveredProcessId = $null
        cpuMilliseconds = $null; observationMilliseconds = [int]$Expectation.observationMilliseconds
        elapsedMilliseconds = $null; deadlineMilliseconds = $ExecutionDeadlineMilliseconds
        recoveryBudgetMilliseconds = 5000; recoveryElapsedMilliseconds = $null; exitCode = $null; stdout = $null; stderr = $null
        violations = [Collections.Generic.List[object]]::new(); problems = [Collections.Generic.List[object]]::new()
    }
    $process = $null
    $captures = [Collections.Generic.List[object]]::new()
    $operation = 'launch'
    $cancellation = [Threading.CancellationTokenSource]::new()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($result.observationMilliseconds -lt 0) { throw 'A fixture CPU observation budget cannot be negative.' }
        $start = [Diagnostics.ProcessStartInfo]::new("$Target.exe")
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        $result.processId = $result.unrecoveredProcessId = $process.Id
        $result.termination = 'unavailable'
        $operation = 'capture.start'
        Start-KimiFixtureCapture $process $cancellation.Token $captures
        Read-KimiFixtureObservation $process $result $timer
    }
    catch { Add-KimiFixtureProblem $result $operation $_.Exception }
    $result.elapsedMilliseconds = [Math]::Round($timer.Elapsed.TotalMilliseconds, 3)
    if ($null -ne $process) { Complete-KimiFixtureProcess $process $captures $result $cancellation }
    try { $cancellation.Dispose() }
    catch { Add-KimiFixtureProblem $result 'capture.dispose' $_.Exception }
    $result.status = Get-KimiFixtureProcessStatus $result $Expectation
    [pscustomobject]$result
}
