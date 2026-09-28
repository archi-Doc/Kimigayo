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
$source = Join-Path $repo 'milestones/Milestone6.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone6/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Selected 7.`nControl flow passed.`n"
Build-And-Run $source (Split-Path $source) 'Milestone6' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
foreach ($anchor in @('value >= 7', 'found == 7', 'yield found * 10', 'score == 70', 'exit to validate: false')) {
    if (-not $original.Contains($anchor)) { throw "Review the variants against the current source: $anchor" }
}
$cleanup = (Edit-KimiSource $original 'public func main()' "public func main()`n    defer => Console.writeLine(`"main cleanup`")" 'let found: i32 = search: loop' "let found: i32 = search: loop`n        defer => Console.writeLine(`"iteration cleanup`")" 'let score: i32 = if found == 7' "let score: i32 = if found == 7`n        defer => Console.writeLine(`"selection cleanup`")" 'let accepted: bool = validate: do' "let accepted: bool = validate: do`n        defer => Console.writeLine(`"validation cleanup`")")
$guard = @'
(probe: do
                Console.writeLine("guard")
                exit to probe: value >= 7
            )
'@
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    SkipThree = @{ source = (Edit-KimiSource $original 'value >= 7' 'value >= 3' 'found == 7' 'found == 5' 'Selected 7.' 'Selected 5.' 'score == 70' 'score == 50'); stdout = "Selected 5.`nControl flow passed.`n" }
    AbortValidation = @{ source = (Edit-KimiSource $original 'yield found * 10' 'yield 0'); stdout = "Selected 7.`n"; abort = $true }
    ElseBranch = @{ source = (Edit-KimiSource $original 'found == 7' 'found == 99'); stdout = ''; abort = $true }
    FalseComparison = @{ source = (Edit-KimiSource $original 'score == 70' 'score == 71'); stdout = "Selected 7.`n"; abort = $true }
    GuardEffects = @{ source = (Edit-KimiSource $original 'value >= 7' $guard); stdout = "guard`nguard`nguard`n$expected" }
    Cleanup = @{ source = $cleanup; stdout = ("iteration cleanup`n" * 7) + "Selected 7.`nselection cleanup`nvalidation cleanup`nControl flow passed.`nmain cleanup`n" }
    AbortCleanup = @{ source = (Edit-KimiSource $cleanup 'yield found * 10' 'yield 0'); stdout = ("iteration cleanup`n" * 7) + "Selected 7.`nselection cleanup`nvalidation cleanup`n"; abort = $true }
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
            $abortLine = 0
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i].Contains('$abort(')) { $abortLine = $i + 1; $abortColumn = $lines[$i].IndexOf('$abort(') + 1; break }
            }
            if ($abortLine -eq 0) { throw 'Missing Abort source location' }
            $expectedError = "$name.kimi:${abortLine}:${abortColumn}: abort KIMI_E_ABORT: Control-flow check failed`n"
            $expectedExit = 1
        }
        Build-And-Run $project $directory $name $level $variant.stdout $expectedError $expectedExit
    }
}
$invalid = [ordered]@{
    MissingLoopTarget = @{ source = (Edit-KimiSource $original 'exit to search: value' 'exit to missing: value'); diagnostic = 'ControlFlow_Kd' }
    WrongContinueTarget = @{ source = (Edit-KimiSource $original 'exit to validate: false' 'continue to validate'); diagnostic = 'ControlFlow_Kd' }
    UnlabeledDoExit = @{ source = (Edit-KimiSource $original 'exit to validate: false' 'exit false'); diagnostic = 'ControlFlow_Kd' }
    WrongLoopResult = @{ source = (Edit-KimiSource $original 'exit to search: value' 'exit to search: true'); diagnostic = 'TypeMismatch_Kd' }
    WrongYieldResult = @{ source = (Edit-KimiSource $original 'yield found * 10' 'yield true'); diagnostic = 'TypeMismatch_Kd' }
    ImplicitUnitResult = @{ source = (Edit-KimiSource $original 'yield found * 10' 'found * 10'); diagnostic = 'TypeMismatch_Kd' }
    ContinuingRequire = @{ source = (Edit-KimiSource $original 'exit to validate: false' '()'); diagnostic = 'ControlFlow_Kd' }
    NonBooleanGuard = @{ source = (Edit-KimiSource $original 'value >= 7' 'value'); diagnostic = 'TypeMismatch_Kd' }
    NonExhaustiveMatch = @{ source = (Edit-KimiSource $original '_ => ()' '// Missing catch-all'); diagnostic = 'NonExhaustiveMatch_Kd' }
    EscapedPatternBinding = @{ source = (Edit-KimiSource $original 'yield found * 10' 'yield value * 10'); diagnostic = 'UnresolvedBinding_Kd' }
    WrongYieldTarget = @{ source = (Edit-KimiSource $original 'yield found * 10' 'yield to validate: found * 10'); diagnostic = 'ControlFlow_Kd' }
    UnreachableWrongResult = @{ source = (Edit-KimiSource $original 'yield found * 10' "yield found * 10`n        yield true"); diagnostic = 'TypeMismatch_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 6 checks ($Configuration): $report"
