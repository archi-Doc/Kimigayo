# Original milestone sources only: one build and one direct execution per optimization level.
# A benchmark passes -Source, -WorkRoot and -Runs: the same build and checked first run, then timing-only repetitions.
param([int] $Milestone, [string] $ToolchainRoot, [string] $Configuration, [string] $Expected, [string] $Source = '', [string] $WorkRoot = '', [int] $Runs = 1)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'expected-output.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$source = if ($Source) { [IO.Path]::GetFullPath($Source, (Get-Location).ProviderPath) } else { Join-Path $repo "tests/milestones/Milestone$Milestone.kimi" }
$name = [IO.Path]::GetFileNameWithoutExtension($source)
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $(if ($WorkRoot) { $WorkRoot } else { Join-Path $repo "artifacts/verify/milestone$Milestone" }) "$Configuration/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null
$report = Join-Path $work 'verification.json'
@{ status = 'incomplete' } | ConvertTo-Json | Set-Content -LiteralPath $report
$utf8 = [Text.UTF8Encoding]::new($false)
$dotnet = (Get-Command dotnet).Source
$results = [Collections.Generic.List[object]]::new()

function Invoke-Captured([string] $File, [string[]] $Arguments, [int] $ExpectedExit = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $work
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $output = [IO.MemoryStream]::new()
    $errorOutput = [IO.MemoryStream]::new()
    try {
        $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errorCopy = $process.StandardError.BaseStream.CopyToAsync($errorOutput)
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "Timed out: $File" }
        $null = $copy.GetAwaiter().GetResult()
        $null = $errorCopy.GetAwaiter().GetResult()
        $bytes = $output.ToArray()
        $errorBytes = $errorOutput.ToArray()
        if ($process.ExitCode -ne $ExpectedExit) {
            throw "Exit $($process.ExitCode), expected $ExpectedExit`: $File $Arguments`n$($utf8.GetString($bytes))`n$($utf8.GetString($errorBytes))"
        }
        return @{ stdout = $bytes; stderr = $errorBytes; exitCode = $process.ExitCode }
    }
    finally { $process.Dispose(); $output.Dispose(); $errorOutput.Dispose() }
}

function Invoke-Kimi([string[]] $Arguments, [int] $ExpectedExit = 0) {
    return Invoke-Captured $dotnet (@($compiler) + $Arguments) $ExpectedExit
}

$totalTimer = [Diagnostics.Stopwatch]::StartNew()
$status = 'incomplete'
$failure = $null
try {
    foreach ($level in @('O0', 'O2')) {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $directory = Join-Path $work $level
        New-Item -ItemType Directory -Path $directory | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        Copy-Item -LiteralPath $source -Destination $copy
        if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        $preparationSeconds = $timer.Elapsed.TotalSeconds
        $timer.Restart()
        $build = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot)
        [IO.File]::WriteAllBytes((Join-Path $directory 'build.stdout'), $build.stdout)
        [IO.File]::WriteAllBytes((Join-Path $directory 'build.stderr'), $build.stderr)
        $buildSeconds = $timer.Elapsed.TotalSeconds
        $stem = Join-Path $directory "bin/x86_64-pc-windows-msvc/$name"
        $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
        if ($record.status -cne 'linked' -or $record.optimization -cne $level -or
            $record.toolchainVerification -cne 'not-performed' -or $null -ne $record.reportedVersionsMatched -or -not $record.unverifiedToolchain) { throw "Invalid build record: $project" }
        $timer.Restart()
        $actual = Invoke-Captured "$stem.$level.exe" @()
        $runSeconds = $timer.Elapsed.TotalSeconds
        $repeats = [Collections.Generic.List[double]]::new()
        for ($run = 1; $run -lt $Runs; $run++) {
            $timer.Restart()
            $null = Invoke-Captured "$stem.$level.exe" @()
            $repeats.Add($timer.Elapsed.TotalSeconds)
        }
        [IO.File]::WriteAllBytes((Join-Path $directory 'run.stdout'), $actual.stdout)
        [IO.File]::WriteAllBytes((Join-Path $directory 'run.stderr'), $actual.stderr)
        $hex = [Convert]::ToHexString($actual.stdout)
        $errorHex = [Convert]::ToHexString($actual.stderr)
        if (-not (Test-KimiExpectedOutput $utf8.GetBytes($Expected) $actual.stdout) -or -not (Test-KimiExpectedOutput ([byte[]]@()) $actual.stderr)) {
            throw "Output mismatch: $name.$level; stdout=$hex; stderr=$errorHex"
        }
        $results.Add(@{ name = "$name.$level"; optimization = $level; execution = 'native'; stdoutHex = $hex; stderrHex = $errorHex; exitCode = $actual.exitCode
            preparationSeconds = $preparationSeconds; buildSeconds = $buildSeconds; runSeconds = $runSeconds; repeatedRunSeconds = $repeats.ToArray() })
    }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $sourceHash -or
        (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -cne $compilerHash) { throw 'Source/compiler changed during verification' }
    $status = 'passed'
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    @{
        status = $status; error = $failure
        compilerConfiguration = $Configuration; compilerSha256 = $compilerHash; source = [IO.Path]::GetRelativePath($repo, $source).Replace('\', '/')
        sourceSha256 = $sourceHash; toolchainVerification = 'not-performed'; seconds = $totalTimer.Elapsed.TotalSeconds; tests = $results
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
}
Write-Output "Passed $($results.Count) $name checks ($Configuration, original O0/O2): $report"
