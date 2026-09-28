[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'These containment checks require Windows.' }
$harness = Join-Path $PSScriptRoot 'invoke-verification.ps1'
$root = Join-Path ([IO.Path]::GetFullPath('TestResults')) ('verification-check-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$shell = Join-Path $PSHOME 'pwsh.exe'
function Invoke-Check([string] $name, [string] $code, [bool] $success, [int] $deadline = 30) {
    $records = Join-Path $root $name
    $failed = $false
    try { & $harness -FilePath $shell -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', $code) -TimeoutSeconds $deadline -ResultRoot $records | Out-Null }
    catch { $failed = $true }
    if ($failed -eq $success) { throw "Unexpected verification outcome: $name" }
    $record = @(Get-ChildItem -LiteralPath $records -Directory)
    if ($record.Count -ne 1) { throw "Missing unique result: $name" }
    $result = Get-Content -LiteralPath (Join-Path $record[0].FullName 'result.json') -Raw | ConvertFrom-Json
    if (($result.result -eq 'PASS') -ne $success) { throw "Incorrect result record: $name" }
    return $record[0].FullName
}

$record = @(Invoke-Check 'streams' '[Console]::Out.Write("hello"); [Console]::Error.Write("error")' $true)[-1]
if ([IO.File]::ReadAllText((Join-Path $record 'stdout.log')) -cne 'hello' -or [IO.File]::ReadAllText((Join-Path $record 'stderr.log')) -cne 'error') { throw 'Output bytes changed.' }
$record = @(Invoke-Check 'nonzero' 'exit 7' $false)[-1]
$result = Get-Content -LiteralPath (Join-Path $record 'result.json') -Raw | ConvertFrom-Json
if ($result.exitCode -ne 7) { throw 'Nonzero exit code was lost.' }
$record = @(Invoke-Check 'deadline' '[Console]::Out.WriteLine($PID); Start-Sleep -Seconds 60' $false 2)[-1]
$taskProcessId = [int][IO.File]::ReadAllText((Join-Path $record 'stdout.log')).Trim()
if (Get-Process -Id $taskProcessId -ErrorAction SilentlyContinue) { throw 'Timed-out target survived job closure.' }

# The target creates a background descendant, then exits before that child.
# Assignment before dispatch ensures that this grandchild is in the same job.
$pidFile = Join-Path $root 'descendant.txt'
$child = Join-Path $root 'child.ps1'
@'
param([string] $PidFile)
[IO.File]::WriteAllText($PidFile, [string]$PID)
Start-Sleep -Seconds 60
'@ | Set-Content -LiteralPath $child -Encoding utf8
$target = Join-Path $root 'target.ps1'
@'
param([string] $Child, [string] $PidFile)
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $Child, '-PidFile', $PidFile)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path -LiteralPath $PidFile)) {
    if ($timer.Elapsed.TotalSeconds -gt 5) { throw 'Descendant did not start.' }
    Start-Sleep -Milliseconds 10
}
$process.Dispose()
'@ | Set-Content -LiteralPath $target -Encoding utf8
$failed = $false
try { & $harness -FilePath $shell -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $target, '-Child', $child, '-PidFile', $pidFile) -TimeoutSeconds 30 -ResultRoot (Join-Path $root 'descendant') | Out-Null }
catch { $failed = $true }
if (-not $failed) { throw 'A descendant retaining output pipes was reported as success.' }
$record = @(Get-ChildItem -LiteralPath (Join-Path $root 'descendant') -Directory)[0].FullName
if (-not [IO.File]::ReadAllText((Join-Path $record 'stderr.log')).Contains('Target output drain exceeded 5 seconds.')) { throw 'The intended retained-pipe check was not reached.' }
$taskProcessId = [int][IO.File]::ReadAllText($pidFile)
if (Get-Process -Id $taskProcessId -ErrorAction SilentlyContinue) { throw 'Descendant survived its parent and job closure.' }
Write-Output 'Passed verification streams, nonzero exit, deadline and exited-parent descendant checks.'
