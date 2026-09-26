[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $repo "Kimi/bin/$Configuration/net10.0/Kimi.dll"
$source = Join-Path $repo 'milestones/Milestone10.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone10/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

function Build-And-Run([string] $InputPath, [string] $Directory, [string] $Name, [string] $Level, [string] $Stdout, [string] $Stderr = '', [int] $ExitCode = 0) {
    $null = Invoke-Kimi @('build', $InputPath, '--ToolchainRoot', $ToolchainRoot)
    $stem = Join-Path $Directory "bin/x86_64-pc-windows-msvc/$Name"
    $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
    if ($record.status -cne 'linked' -or $record.optimization -cne $Level -or
        -not $record.reportedVersionsMatched -or $record.unverifiedToolchain) { throw "Unverified build: $InputPath" }
    # The normal build verifies input IR and optimized O2 IR before object generation.
    foreach ($mode in @('native', 'run-exe', 'run-input')) {
        $actual = switch ($mode) {
            'native' { Invoke-Captured "$stem.$Level.exe" @() $ExitCode }
            'run-exe' { Invoke-Kimi @('run', "$stem.$Level.exe") $ExitCode }
            'run-input' { Invoke-Kimi @('run', $InputPath) $ExitCode }
        }
        $hex = [Convert]::ToHexString($actual.stdout)
        # Source/project run prepends a project summary; direct execution must be exact.
        $outputMatches = if ($mode -eq 'run-input') {
            $text = $utf8.GetString($actual.stdout)
            $summaryEnd = $text.IndexOf("`n")
            $summaryEnd -ge 0 -and $text.Substring($summaryEnd + 1) -ceq $Stdout
        } else { $hex -ceq [Convert]::ToHexString($utf8.GetBytes($Stdout)) }
        if (-not $outputMatches -or $actual.stderr -cne $Stderr) { throw "Output mismatch: $Name.$Level.$mode; stdout=$hex; stderr=$($actual.stderr)" }
        $results.Add(@{ name = "$Name.$Level.$mode"; stdoutHex = $hex; stderr = $actual.stderr; exitCode = $actual.exitCode })
    }
    Copy-Item -LiteralPath "$stem.link.build.json" -Destination (Join-Path $work "$Name.$Level.build.json")
}

$expected = "Accepted total is 12.`nPattern run finished.`n"
Build-And-Run $source (Split-Path $source) 'Milestone10' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
$empty = [regex]::Replace($original, '(?s)\[7 of Command<\(i32, bool\)>\] = \[.*?\r?\n    \]', '[0 of Command<(i32, bool)>] = []')
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Exhaustion = @{ source = (Edit-KimiSource $original '.Stop, .Data((100, true))' '.Skip, .Data((100, false))'); stdout = $expected }
    Alternate = @{ source = (Edit-KimiSource $original '.Data((5, true))' '.Data((3, true))' 'answer == 12' 'answer == 10' 'total is 12.' 'total is 10.'); stdout = (Edit-KimiSource $expected 'total is 12.' 'total is 10.') }
    Empty = @{ source = (Edit-KimiSource $empty 'answer == 12' 'answer == 0' 'total is 12.' 'total is 0.'); stdout = (Edit-KimiSource $expected 'total is 12.' 'total is 0.') }
    ImmediateStop = @{ source = (Edit-KimiSource $original '.Data((5, true))' '.Stop' 'answer == 12' 'answer == 0' 'total is 12.' 'total is 0.'); stdout = (Edit-KimiSource $expected 'total is 12.' 'total is 0.') }
    GuardCleanup = @{ source = "func accepts(value: i32) -> bool`n    defer => Console.writeLine(`"Guard cleaned.`")`n    return value > 0`n`n" + (Edit-KimiSource $original 'if value > 0' 'if accepts(value)'); stdout = "Guard cleaned.`nGuard cleaned.`nGuard cleaned.`n" + $expected }
    Abort = @{ source = (Edit-KimiSource $original 'answer == 12' 'answer == 11'); stdout = ''; abort = '$abort("Unexpected command total")' }
}
foreach ($level in @('O0', 'O2')) {
    foreach ($entry in $variants.GetEnumerator()) {
        $name = $entry.Key
        $variant = $entry.Value
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        if ($name -eq 'Renamed') {
            Copy-Item -LiteralPath $source -Destination $copy
            if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
        } else { [IO.File]::WriteAllText($copy, $variant.source, $utf8) }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        $expectedError = ''
        $expectedExit = 0
        if ($variant.abort) {
            $lines = $variant.source -split '\r?\n'
            $line = 0
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i].Contains($variant.abort)) { $line = $i + 1; $column = $lines[$i].IndexOf($variant.abort) + 1; break }
            }
            if ($line -eq 0) { throw 'Missing Abort source location' }
            $expectedError = "$name.kimi:${line}:${column}: abort KIMI_E_ABORT: Unexpected command total`n"
            $expectedExit = 1
        }
        Build-And-Run $project $directory $name $level $variant.stdout $expectedError $expectedExit
    }
}
$invalid = [ordered]@{
    WrongPayload = @{ source = (Edit-KimiSource $original '.Data((5, true))' '.Data(5)'); diagnostic = 'TypeMismatch_Kd' }
    MissingCase = @{ source = [regex]::Replace($original, '(?m)^\s*\.Stop => exit to scan: total\r?\n', ''); diagnostic = 'NonExhaustiveMatch_Kd' }
    WrongPatternArity = @{ source = (Edit-KimiSource $original '.Data((let value, true))' '.Data((let value, true, _))'); diagnostic = 'InvalidPattern_Kd' }
    DuplicatePatternName = @{ source = (Edit-KimiSource $original '.Data((let value, true))' '.Data((let value, let value))'); diagnostic = 'DuplicateBinding_Kd' }
    CandidateAssignment = @{ source = (Edit-KimiSource $original 'if value > 0' 'if (value = 1)'); diagnostic = 'SharedPathAccess_Kd' }
    WrongGuardType = @{ source = (Edit-KimiSource $original 'if value > 0' 'if value'); diagnostic = 'TypeMismatch_Kd' }
    MissingTarget = @{ source = (Edit-KimiSource $original 'exit to scan:' 'exit to missing:'); diagnostic = 'ControlFlow_Kd' }
    UninitializedTotal = @{ source = (Edit-KimiSource $original 'var total: i32 = 0' 'var total: i32'); diagnostic = 'UninitializedPlace_Kd' }
    EscapedPatternName = @{ source = (Edit-KimiSource $original 'total = total + amount' 'total = total + value'); diagnostic = 'UnresolvedBinding_Kd' }
}
foreach ($entry in $invalid.GetEnumerator()) {
    $path = Join-Path $work "$($entry.Key).kimi"
    if ($entry.Value.source -ceq $original) { throw "Rejection mutation did not change the input: $($entry.Key)" }
    [IO.File]::WriteAllText($path, $entry.Value.source, $utf8)
    $failure = Invoke-Kimi @('build', $path, '--ToolchainRoot', $ToolchainRoot) 1
    $diagnostic = $utf8.GetString($failure.stdout) + $failure.stderr
    [IO.File]::WriteAllText((Join-Path $work "$($entry.Key).diagnostics.txt"), $diagnostic, $utf8)
    $stem = Join-Path $work "bin/x86_64-pc-windows-msvc/$($entry.Key)"
    $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
    $required = $entry.Value.diagnostic
    $plainDiagnostic = [regex]::Replace($diagnostic, '\x1b\[[0-9;]*m', '')
    if ($plainDiagnostic -notmatch ('\b(?:' + $required + ')\b') -or $record.status -cne 'incomplete' -or
        (Test-Path "$stem.ll") -or (Test-Path "$stem.O2.exe")) { throw "Invalid input was not diagnosed with $required before emission: $($entry.Key)" }
    $results.Add(@{ name = $entry.Key; rejected = $true; exitCode = $failure.exitCode; diagnostic = $required })
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $sourceHash -or
    (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -cne $compilerHash) { throw 'Source/compiler changed during verification' }
@{
    status = 'passed'; compilerConfiguration = $Configuration; compilerSha256 = $compilerHash
    sourceSha256 = $sourceHash; tests = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Passed $($results.Count) Milestone 10 checks ($Configuration): $report"
