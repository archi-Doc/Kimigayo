# Native fixture execution: launch one executable, drain strict UTF-8 streams, enforce its oracle and reap it.
# Inputs: executable stem and its stdout/stderr/exit/timeout expectation row. Success returns nothing; failures throw.
# Owns process lifetime only; no compilation, fixture aggregation or codegen-pending decisions.
function Invoke-KimiFixtureProcess([string] $Target, [hashtable] $Expectation) {
    $expected = $Expectation.stdout
    $expectedError = $Expectation.stderr
    $exit = $Expectation.exit
    $timeout = $Expectation.timeout
    $executionEncoding = [Text.UTF8Encoding]::new($false, $true)
    $start = [Diagnostics.ProcessStartInfo]::new("$Target.exe")
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Kimi writes UTF-8 bytes regardless of the invoking console's code page.
    # A hidden verification worker can otherwise decode these as legacy text.
    $start.StandardOutputEncoding = $executionEncoding
    $start.StandardErrorEncoding = $executionEncoding
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($timeout -gt 0) {
            if ($process.WaitForExit($timeout)) { throw "Divergent fixture returned: $Target, exit=$($process.ExitCode)" }
            $process.Kill($true)
            $process.WaitForExit()
        }
        elseif (-not $process.WaitForExit(30000)) { throw "Timed out: $Target" }
        $actual = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if (($timeout -eq 0 -and $process.ExitCode -ne $exit) -or $actual -cne $expected -or $errorText -cne $expectedError) {
            throw "Failed: $Target, exit=$($process.ExitCode), stdout=$actual, stderr=$errorText"
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
