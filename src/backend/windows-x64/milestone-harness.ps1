# Original milestone sources only: one build and one direct execution per optimization level.
param([int] $Milestone, [string] $ToolchainRoot, [string] $Configuration, [string] $Expected)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$source = Join-Path $repo "tests/milestones/Milestone$Milestone.kimi"
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "artifacts/verify/milestone$Milestone/$Configuration/$([guid]::NewGuid().ToString('N'))"
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
    try {
        $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "Timed out: $File" }
        $null = $copy.GetAwaiter().GetResult()
        $stderr = $errors.GetAwaiter().GetResult()
        $bytes = $output.ToArray()
        if ($process.ExitCode -ne $ExpectedExit) {
            throw "Exit $($process.ExitCode), expected $ExpectedExit`: $File $Arguments`n$($utf8.GetString($bytes))`n$stderr"
        }
        return @{ stdout = $bytes; stderr = $stderr; exitCode = $process.ExitCode }
    }
    finally { $process.Dispose(); $output.Dispose() }
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
        $name = "Milestone$Milestone"
        $copy = Join-Path $directory "$name.kimi"
        Copy-Item -LiteralPath $source -Destination $copy
        if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        $preparationSeconds = $timer.Elapsed.TotalSeconds
        $timer.Restart()
        $build = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot)
        [IO.File]::WriteAllBytes((Join-Path $directory 'build.stdout'), $build.stdout)
        [IO.File]::WriteAllText((Join-Path $directory 'build.stderr'), $build.stderr, $utf8)
        $buildSeconds = $timer.Elapsed.TotalSeconds
        $stem = Join-Path $directory "bin/x86_64-pc-windows-msvc/$name"
        $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
        if ($record.status -cne 'linked' -or $record.optimization -cne $level -or
            $record.toolchainVerification -cne 'not-performed' -or $null -ne $record.reportedVersionsMatched -or -not $record.unverifiedToolchain) { throw "Invalid build record: $project" }
        $timer.Restart()
        $actual = Invoke-Captured "$stem.$level.exe" @()
        $runSeconds = $timer.Elapsed.TotalSeconds
        [IO.File]::WriteAllBytes((Join-Path $directory 'run.stdout'), $actual.stdout)
        [IO.File]::WriteAllText((Join-Path $directory 'run.stderr'), $actual.stderr, $utf8)
        $hex = [Convert]::ToHexString($actual.stdout)
        if ($hex -cne [Convert]::ToHexString($utf8.GetBytes($Expected)) -or $actual.stderr -cne '') { throw "Output mismatch: $name.$level; stdout=$hex; stderr=$($actual.stderr)" }
        $results.Add(@{ name = "$name.$level"; optimization = $level; execution = 'native'; stdoutHex = $hex; stderr = $actual.stderr; exitCode = $actual.exitCode
            preparationSeconds = $preparationSeconds; buildSeconds = $buildSeconds; runSeconds = $runSeconds })
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
        compilerConfiguration = $Configuration; compilerSha256 = $compilerHash; source = "tests/milestones/Milestone$Milestone.kimi"
        sourceSha256 = $sourceHash; toolchainVerification = 'not-performed'; seconds = $totalTimer.Elapsed.TotalSeconds; tests = $results
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
}
Write-Output "Passed $($results.Count) Milestone $Milestone checks ($Configuration, original O0/O2): $report"
