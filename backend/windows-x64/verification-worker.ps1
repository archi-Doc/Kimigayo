# The controller assigns this process to its Windows job before sending input.
# No target or target descendant can start outside that job.
$ErrorActionPreference = 'Stop'
$request = [Console]::In.ReadLine()
if ($null -eq $request) { throw 'Verification controller closed before dispatch.' }
$request = $request | ConvertFrom-Json
$start = [Diagnostics.ProcessStartInfo]::new([string]$request.file)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in $request.arguments) { $start.ArgumentList.Add([string]$argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
    $stderr = $process.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
    # The controller owns the deadline and kills the entire job on interruption.
    $process.WaitForExit()
    if (-not [Threading.Tasks.Task]::WhenAll($stdout, $stderr).Wait(5000)) { throw 'Target output drain exceeded 5 seconds.' }
    exit $process.ExitCode
}
finally { $process.Dispose() }
