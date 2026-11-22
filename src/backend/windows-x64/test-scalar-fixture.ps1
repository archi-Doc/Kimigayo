# One fixture owns its optimization-level results: each level's build, read from its pair's record (test-scalars.ps1's kimi tatara
# sessions built them), and its executable's run; workers share only the run context. A build whose record is not linked is a failure,
# as a failed native build is (its text is in the session's log); a missing input, a pair with no record (its line was not attempted)
# or missing evidence is incomplete verification. Process outcomes are already classified by their owner; this worker never interprets
# exception messages.
[CmdletBinding()]
param([IO.FileInfo] $Fixture, [hashtable] $Context)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-scalar-process.ps1')
$log = Join-Path $Context.logs ($Fixture.BaseName + '.log')
$timer = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{
    fixture = $Fixture.Name; status = $null; seconds = $null; log = $log
    levels = [Collections.Generic.List[string]]::new(); executions = [Collections.Generic.List[object]]::new()
    builds = [Collections.Generic.List[object]]::new(); problems = [Collections.Generic.List[object]]::new()
}
try {
    & {
        try {
            $stem = [IO.Path]::Combine($Context.fixtures, $Fixture.BaseName)
            $expected = [IO.File]::ReadAllBytes("$stem.stdout")
            $exit = [int][IO.File]::ReadAllText("$stem.exit")
            $expectedError = [IO.File]::ReadAllBytes("$stem.stderr")
            $observation = if (Test-Path -LiteralPath "$stem.observation") { [int][IO.File]::ReadAllText("$stem.observation") } else { 0 }
        }
        catch {
            Add-KimiFixtureProblem $result 'fixture.input' $_.Exception
            return
        }
        foreach ($build in $Context.builds[$Fixture.Name]) {
            if (-not [IO.File]::Exists($build.record)) {
                Add-KimiFixtureProblem $result 'build' ([InvalidOperationException]::new("No session attempted $($build.manifest); see $($build.log)"))
                return
            }
            $record = Get-Content -LiteralPath $build.record -Raw | ConvertFrom-Json
            $result.builds.Add([pscustomobject]@{ level = $build.level; manifest = $build.manifest; status = $record.status; log = $build.log })
            "build $($build.level): $($record.status); $($build.manifest); session log $($build.log)"
            if ($record.status -cne 'linked') { return }
            $target = Join-Path (Split-Path -Parent $build.record) ([IO.Path]::GetFileNameWithoutExtension($record.executable))
            $execution = Invoke-KimiFixtureProcess $target @{
                stdout = $expected; stderr = $expectedError; exit = $exit; observationMilliseconds = $observation
            } $Context.executionDeadlineMilliseconds
            $result.executions.Add([pscustomobject]@{ level = $build.level; result = $execution })
            $execution | ConvertTo-Json -Depth 8
            if ($execution.status -cne 'passed') { return }
            $result.levels.Add($build.level)
        }
    } *> $log
}
catch {
    Add-KimiFixtureProblem $result 'fixture' $_.Exception
    # If evidence cannot be retained, propagate the incomplete step instead of manufacturing a fixture failure.
    $_ | Out-String | Add-Content -LiteralPath $log
}
$incomplete = $result.problems.Count -gt 0 -or @($result.executions | Where-Object { $_.result.status -ceq 'incomplete' }).Count -gt 0
$failed = @($result.builds | Where-Object status -CNE 'linked').Count -gt 0 -or
    @($result.executions | Where-Object { $_.result.status -ceq 'failed' }).Count -gt 0
$result.status = if ($incomplete) { 'incomplete' } elseif ($failed) { 'failed' }
elseif (($result.levels -join ',') -ceq 'O0,O2') { 'passed' } else { 'incomplete' }
$result.seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
[pscustomobject]$result
