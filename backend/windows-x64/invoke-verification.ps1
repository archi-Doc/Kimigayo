[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FilePath,
    [string[]] $ArgumentList = @(),
    [string[]] $InputPath = @(),
    [ValidateRange(1, 86400)] [int] $TimeoutSeconds = 900,
    [string] $ResultRoot = 'TestResults/verification'
)
$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetFullPath($ResultRoot)) ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
$null = New-Item -ItemType Directory -Path $directory
$job = $null
if ($IsWindows) {
    if (-not ('KimiVerificationJob' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'VerificationJob.cs') }
}
$start = [Diagnostics.ProcessStartInfo]::new($FilePath)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($IsWindows) {
    $start.FileName = (Join-Path $PSHOME 'pwsh.exe')
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'verification-worker.ps1'))) { $start.ArgumentList.Add($argument) }
    $start.RedirectStandardInput = $true
}
else {
    foreach ($argument in $ArgumentList) { $start.ArgumentList.Add($argument) }
}
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$stdout = [IO.File]::Create((Join-Path $directory 'stdout.log'))
$stderr = [IO.File]::Create((Join-Path $directory 'stderr.log'))
$timer = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{ file = $FilePath; arguments = $ArgumentList; workingDirectory = (Get-Location).Path; timeoutSeconds = $TimeoutSeconds; result = 'FAIL'; exitCode = $null }
$started = $false
try {
    $result.inputs = @(foreach ($path in $InputPath) {
        $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
        [ordered]@{ path = $hash.Path; sha256 = $hash.Hash }
    })
    if ($IsWindows) { $job = [KimiVerificationJob]::new() }
    $started = $process.Start()
    if (-not $started) { throw 'Verification process did not start.' }
    if ($null -ne $job) {
        $job.Assign($process)
        $process.StandardInput.WriteLine((@{ file = $FilePath; arguments = $ArgumentList } | ConvertTo-Json -Compress))
        $process.StandardInput.Close()
    }
    $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $errorTask = $process.StandardError.BaseStream.CopyToAsync($stderr)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Verification exceeded $TimeoutSeconds seconds." }
    $result.exitCode = $process.ExitCode
    # Close even after the root exits: descendants can otherwise retain pipes.
    if ($null -ne $job) { $job.Dispose() }
    if (-not [Threading.Tasks.Task]::WhenAll($outputTask, $errorTask).Wait(5000)) { throw 'Verification output drain exceeded 5 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Verification exited with code $($process.ExitCode)." }
    foreach ($inputFile in $result.inputs) {
        if ((Get-FileHash -LiteralPath $inputFile.path -Algorithm SHA256).Hash -cne $inputFile.sha256) { throw "Verification input changed: $($inputFile.path)" }
    }
    $result.result = 'PASS'
}
finally {
    if ($null -ne $job) { $job.Dispose() }
    if ($started -and -not $process.HasExited) {
        $process.Kill($true)
        $null = $process.WaitForExit(5000)
    }
    $process.Dispose()
    $stdout.Dispose()
    $stderr.Dispose()
    $result.elapsedSeconds = $timer.Elapsed.TotalSeconds
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'result.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $directory 'stdout.log')
    Get-Content -LiteralPath (Join-Path $directory 'stderr.log')
    Write-Output "Verification record: $directory"
}
