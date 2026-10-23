# Native fixture execution: observe one executable, compare its oracle and reap it.
# Inputs: executable stem, stdout/stderr/exit/CPU-observation oracle, launch-to-stop deadline in milliseconds.
# Output: passed/failed/incomplete plus measured CPU/wall time, termination and captured streams.
# A finite observation never proves divergence. No wall-time substitute for CPU progress or pending decisions.
function Invoke-KimiFixtureProcess(
    [string] $Target, [hashtable] $Expectation,
    [ValidateRange(1, 2147483647)] [int] $ExecutionDeadlineMilliseconds
) {
    $observation = [int]$Expectation.observationMilliseconds
    if ($observation -lt 0) { throw 'A fixture CPU observation budget cannot be negative.' }
    $start = [Diagnostics.ProcessStartInfo]::new("$Target.exe")
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $termination = 'exited'
        while (-not $process.HasExited) {
            $process.Refresh()
            $cpu = $process.TotalProcessorTime.TotalMilliseconds
            if ($process.HasExited) { break }
            # Conservatively reject a late observation even if its CPU sample reaches the budget.
            if ($timer.Elapsed.TotalMilliseconds -ge $ExecutionDeadlineMilliseconds) { $termination = 'deadline'; break }
            if ($observation -gt 0 -and $cpu -ge $observation) { $termination = 'observed'; break }
            $null = $process.WaitForExit(10)
        }
        $elapsed = $timer.Elapsed.TotalMilliseconds
        $cpu = $process.TotalProcessorTime.TotalMilliseconds
        if ($termination -ne 'exited') {
            if ($process.HasExited) { $termination = 'exited' }
            else { $process.Kill($true) }
        }
        $process.WaitForExit()
        $actual = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        $status = switch ($termination) {
            'deadline' { if ($observation -gt 0) { 'incomplete' } else { 'failed' } }
            'exited' { if ($observation -gt 0 -or $process.ExitCode -ne $Expectation.exit) { 'failed' } else { 'passed' } }
            'observed' { 'passed' }
        }
        if ($status -ceq 'passed' -and ($actual -cne $Expectation.stdout -or $errorText -cne $Expectation.stderr)) { $status = 'failed' }
        [pscustomobject]@{
            status = $status; termination = $termination
            cpuMilliseconds = $cpu; observationMilliseconds = $observation
            elapsedMilliseconds = [Math]::Round($elapsed, 3); deadlineMilliseconds = $ExecutionDeadlineMilliseconds
            exitCode = $process.ExitCode; stdout = $actual; stderr = $errorText
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
