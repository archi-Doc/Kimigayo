[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $repo "Kimi/bin/$Configuration/net10.0/Kimi.dll"
$source = Join-Path $repo 'milestones/Milestone20.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone20/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Default index evaluated.`nInferred selection is 10.`nExplicit selection is 30.`nDefault index evaluated.`nForwarded selection is 10.`nDefault index evaluated.`nOrdinary selection is 4.`nDefault index evaluated.`nLocal selection is 7.`nInference and defaults finished.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone20' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
# The default index 1 selects the second element everywhere a default is omitted.
$defaultOne = $original.Replace('return 0', 'return 1').Replace('inferred == 10', 'inferred == 20').Replace('forwarded == 10', 'forwarded == 20').Replace('ordinary == 4', 'ordinary == 5').Replace('shortLived == 7', 'shortLived == 8').Replace('Inferred selection is 10.', 'Inferred selection is 20.').Replace('Forwarded selection is 10.', 'Forwarded selection is 20.').Replace('Ordinary selection is 4.', 'Ordinary selection is 5.').Replace('Local selection is 7.', 'Local selection is 8.')
$defaultOneOutput = $expected.Replace('Inferred selection is 10.', 'Inferred selection is 20.').Replace('Forwarded selection is 10.', 'Forwarded selection is 20.').Replace('Ordinary selection is 4.', 'Ordinary selection is 5.').Replace('Local selection is 7.', 'Local selection is 8.')
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = $original.Replace('pick', 'choose').Replace('forward', 'relay').Replace('numbers', 'items').Replace('defaultIndex', 'firstIndex'); stdout = $expected }
    Values = @{ source = $original.Replace('10', '11').Replace('20', '21').Replace('30', '31'); stdout = $expected.Replace('10', '11').Replace('30', '31') }
    Explicit = @{ source = $original.Replace('let inferred = pick(numbers@ref)', 'let inferred = pick<3, i32>(numbers@ref)'); stdout = $expected }
    Index = @{ source = $original.Replace('index: 2', 'index: 1').Replace('explicit == 30', 'explicit == 20').Replace('Explicit selection is 30.', 'Explicit selection is 20.'); stdout = $expected.Replace('Explicit selection is 30.', 'Explicit selection is 20.') }
    DefaultOne = @{ source = $defaultOne; stdout = $defaultOneOutput }
    Supplied = @{ source = $original.Replace('let inferred = pick(numbers@ref)', 'let inferred = pick(numbers@ref, index: 0)'); stdout = $expected.Substring("Default index evaluated.`n".Length) }
}
foreach ($level in @('O0', 'O2')) {
    foreach ($entry in $variants.GetEnumerator()) {
        if ($Cases -ne 'All') { continue }
        $name = $entry.Key
        $variant = $entry.Value
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        if ($name -eq 'Renamed') {
            Copy-Item -LiteralPath $source -Destination $copy
            if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
        } else {
            if ($variant.source -ceq $original) { throw "Variant mutation did not change the input: $name" }
            [IO.File]::WriteAllText($copy, $variant.source, $utf8)
        }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        Build-And-Run $project $directory $name $level $variant.stdout
    }
}
$invalid = [ordered]@{
    WrongLength = @{ source = $original.Replace('pick<3, i32>(numbers@ref, index: 2)', 'pick<2, i32>(numbers@ref, index: 2)'); diagnostic = 'NoApplicableOverload_Kd' }
    PartialArguments = @{ source = $original.Replace('pick<3, i32>(numbers@ref, index: 2)', 'pick<3>(numbers@ref, index: 2)'); diagnostic = 'NoApplicableOverload_Kd' }
    EscapingLocal = @{ source = $original.Replace('    Console.writeLine("Inference and defaults finished.")', "    var escaped = pick(numbers@ref)`n    do`n        let local: [3 of i32] = [7, 8, 9]`n        escaped = forward(local@ref)`n    require escaped == 7 else => `$abort(`"escape`")`n    Console.writeLine(`"Inference and defaults finished.`")"); diagnostic = 'NoApplicableOverload_Kd' }
    StaticOrigin = @{ source = $original.Replace('index: isize = defaultIndex()) -> ref{source}/T', 'index: isize = defaultIndex()) -> ref{static}/T'); diagnostic = 'TypeMismatch_Kd' }
    UnboundDefault = @{ source = $original.Replace("public func main()`n", "func make<T>(value: T = `$abort(`"No value`")) -> T => value`n`npublic func main()`n    let m = make()`n"); diagnostic = 'NoApplicableOverload_Kd' }
}
foreach ($level in @('O0', 'O2')) {
    foreach ($entry in $invalid.GetEnumerator()) {
        if ($entry.Value.source -ceq $original) { throw "Rejection mutation did not change the input: $($entry.Key)" }
        $name = $entry.Key
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $path = Join-Path $directory "$name.kimi"
        [IO.File]::WriteAllText($path, $entry.Value.source, $utf8)
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        $failure = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot) 1
        $diagnostic = $utf8.GetString($failure.stdout) + $failure.stderr
        [IO.File]::WriteAllText((Join-Path $directory 'diagnostics.txt'), $diagnostic, $utf8)
        $stem = Join-Path $directory "bin/x86_64-pc-windows-msvc/$name"
        $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
        $required = $entry.Value.diagnostic
        $plainDiagnostic = [regex]::Replace($diagnostic, '\x1b\[[0-9;]*m', '')
        if ($plainDiagnostic -notmatch ('\b(?:' + $required + ')\b') -or $record.status -cne 'incomplete' -or
            (Test-Path "$stem.ll") -or (Test-Path "$stem.$level.exe")) { throw "Invalid input was not diagnosed before emission: $name.$level" }
        $results.Add(@{ name = "$name.$level"; rejected = $true; exitCode = $failure.exitCode; diagnostic = $required })
    }
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $sourceHash -or
    (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -cne $compilerHash) { throw 'Source/compiler changed during verification' }
@{
    status = 'passed'; compilerConfiguration = $Configuration; compilerSha256 = $compilerHash; cases = $Cases
    sourceSha256 = $sourceHash; tests = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Passed $($results.Count) Milestone 20 checks ($Configuration): $report"
