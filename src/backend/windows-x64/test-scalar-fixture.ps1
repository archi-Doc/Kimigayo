# One fixture owns compilation, dependency checks and optimization-level results; workers share only the toolchain context.
# Observed tool nonzero/dependency violations are failures. Missing inputs, launch or evidence are incomplete verification.
# Process outcomes are already classified by their owner; this worker never interprets exception messages.
[CmdletBinding()]
param([IO.FileInfo] $Fixture, [hashtable] $Context)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-scalar-process.ps1')
$tools = $Context.tools
$fixtures = $Context.fixtures
$out = $Context.output
$kernel = $Context.kernel
$archive = $Context.archive
$allowedSymbols = [Collections.Generic.HashSet[string]]::new([string[]]$Context.allowedSymbols, [StringComparer]::Ordinal)
$log = Join-Path $Context.logs ($Fixture.BaseName + '.log')
$timer = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{
    fixture = $Fixture.Name; status = $null; seconds = $null; log = $log
    levels = [Collections.Generic.List[string]]::new(); executions = [Collections.Generic.List[object]]::new()
    tools = [Collections.Generic.List[object]]::new(); violations = [Collections.Generic.List[object]]::new()
    problems = [Collections.Generic.List[object]]::new()
}
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $output = @(& $exe @arguments)
        $exitCode = $LASTEXITCODE
    }
    catch {
        Add-KimiFixtureProblem $result 'tool.launch' $_.Exception
        return $false
    }
    $result.tools.Add([pscustomobject]@{ executable = $exe; arguments = $arguments; exitCode = $exitCode; output = $output })
    foreach ($line in $output) { Write-Information $line -InformationAction Continue }
    $exitCode -eq 0
}
try {
    & {
        try {
            $stem = [IO.Path]::Combine($fixtures, $Fixture.BaseName)
            $expected = [IO.File]::ReadAllBytes("$stem.stdout")
            $exit = [int][IO.File]::ReadAllText("$stem.exit")
            $expectedError = [IO.File]::ReadAllBytes("$stem.stderr")
            $observation = if (Test-Path -LiteralPath "$stem.observation") { [int][IO.File]::ReadAllText("$stem.observation") } else { 0 }
        }
        catch {
            Add-KimiFixtureProblem $result 'fixture.input' $_.Exception
            return
        }
        # opt verifies its input module and, without -disable-verify, the optimized one, so the O2 pipeline is the input's and
        # the optimized IR's verification (no separate verify launch); llc verifies each input again.
        $levels = @('O0', 'O2')
        $targets = @{}
        foreach ($level in $levels) { $targets[$level] = Join-Path $out ($Fixture.BaseName + '.' + $level) }
        if (-not (Invoke-Tool $tools.opt @('-S', '-passes=default<O2>', $Fixture.FullName, '-o', "$($targets.O2).ll"))) { return }
        foreach ($level in $levels) {
            $target = $targets[$level]
            $ir = if ($level -eq 'O2') { "$target.ll" } else { $Fixture.FullName }
            $arguments = @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2',
                '-relocation-model=pic', '-code-model=small', $ir, '-o', "$target.obj")
            if (-not (Invoke-Tool $tools.llc $arguments)) { return }
        }
        # One llvm-nm reads both objects; --print-file-name prefixes each line with its object's path. Every line must name exactly
        # one of them: a line that names neither (a path the console decoded differently) is missing evidence, never dropped.
        $objects = @(foreach ($level in $levels) { "$($targets[$level]).obj" })
        if (-not (Invoke-Tool $tools['llvm-nm'] (@('--undefined-only', '--format=posix', '--print-file-name') + $objects))) { return }
        $undefinedBy = @{}
        foreach ($level in $levels) { $undefinedBy[$level] = [Collections.Generic.List[string]]::new() }
        foreach ($line in @($result.tools[-1].output)) {
            $text = [string]$line
            $owners = @($levels | Where-Object { $text.StartsWith("$($targets[$_]).obj: ", [StringComparison]::Ordinal) })
            if ($owners.Count -ne 1) {
                Add-KimiFixtureProblem $result 'tool.output' ([InvalidOperationException]::new("llvm-nm line names no object of this fixture: $text"))
                return
            }
            $undefinedBy[$owners[0]].Add($text.Substring("$($targets[$owners[0]]).obj: ".Length))
        }
        foreach ($level in $levels) {
            $target = $targets[$level]
            $undefined = $undefinedBy[$level]
            $undefined | Set-Content -LiteralPath "$target.undefined.txt" -Encoding utf8
            foreach ($line in $undefined) {
                $symbol = ($line -split '\s+', 2)[0]
                if (-not $allowedSymbols.Contains($symbol)) {
                    $result.violations.Add([pscustomobject]@{ kind = 'dependency'; target = $target; symbol = $symbol })
                    return
                }
            }
            $arguments = @("$target.obj", $archive, $kernel.path, '/entry:__kimi_start', '/subsystem:console',
                '/nodefaultlib', '/Brepro', "/out:$target.exe")
            if (-not (Invoke-Tool $tools['lld-link'] $arguments)) { return }
            $execution = Invoke-KimiFixtureProcess $target @{
                stdout = $expected; stderr = $expectedError; exit = $exit; observationMilliseconds = $observation
            } $Context.executionDeadlineMilliseconds
            $result.executions.Add([pscustomobject]@{ level = $level; result = $execution })
            $execution | ConvertTo-Json -Depth 8
            if ($execution.status -cne 'passed') { return }
            $result.levels.Add($level)
        }
    } *> $log
}
catch {
    Add-KimiFixtureProblem $result 'fixture' $_.Exception
    # If evidence cannot be retained, propagate the incomplete step instead of manufacturing a fixture failure.
    $_ | Out-String | Add-Content -LiteralPath $log
}
$incomplete = $result.problems.Count -gt 0 -or @($result.executions | Where-Object { $_.result.status -ceq 'incomplete' }).Count -gt 0
$failed = $result.violations.Count -gt 0 -or @($result.tools | Where-Object exitCode -NE 0).Count -gt 0 -or
    @($result.executions | Where-Object { $_.result.status -ceq 'failed' }).Count -gt 0
$result.status = if ($incomplete) { 'incomplete' } elseif ($failed) { 'failed' }
elseif (($result.levels -join ',') -ceq 'O0,O2') { 'passed' } else { 'incomplete' }
$result.seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
[pscustomobject]$result
