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
$source = Join-Path $repo 'milestones/Milestone5.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone5/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Borrowed sum is 55.`nView destroyed; counter is still 55.`nFinal value is 56.`nCounter destroyed.`nDone.`n"
Build-And-Run $source (Split-Path $source) 'Milestone5' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
if (-not $original.Contains('while number <= 10') -or -not $original.Contains('add(counter@uniq, 1)')) {
    throw 'Review the variants against the current source'
}
foreach ($level in @('O0', 'O2')) {
    foreach ($name in @('Renamed', 'AbortSum', 'AbortFinal', 'AlternateCounts', 'ImmediateTemporary')) {
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        switch ($name) {
            'Renamed' {
                Copy-Item -LiteralPath $source -Destination $copy
                if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
            }
            'AbortSum' { [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'while number <= 10' 'while number <= 9'), $utf8) }
            'AbortFinal' { [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'add(counter@uniq, 1)' 'add(counter@uniq, 2)'), $utf8) }
            'AlternateCounts' { [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'while number <= 10' 'while number <= 4' '55' '10' '56' '11'), $utf8) }
            'ImmediateTemporary' {
                $program = @'
struct Item
    public var value: i32 = 3
    deinit => Console.writeLine("drop")
func borrow(item: ref/Item) -> ref/Item during item => item
let value = borrow(Item.init()).value
if value != 3 => $abort("Unexpected value")
Console.writeLine("ok")
'@
                [IO.File]::WriteAllText($copy, $program, $utf8)
            }
        }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        switch ($name) {
            'AbortSum' { Build-And-Run $project $directory $name $level '' "AbortSum.kimi:42:13: abort KIMI_E_ABORT: Unexpected sum`n" 1 }
            'AbortFinal' { Build-And-Run $project $directory $name $level "Borrowed sum is 55.`nView destroyed; counter is still 55.`n" "AbortFinal.kimi:29:9: abort KIMI_E_ABORT: Unexpected final value`n" 1 }
            'AlternateCounts' { Build-And-Run $project $directory $name $level (Edit-KimiSource $expected '55' '10' '56' '11') }
            'ImmediateTemporary' { Build-And-Run $project $directory $name $level "drop`nok`n" }
            default { Build-And-Run $project $directory $name $level $expected }
        }
    }
}

$anchor = '// Mutating or moving counter here'
$invalid = [ordered]@{
    LiveLoanMutation = @{ source = (Edit-KimiSource $original $anchor "add(counter@uniq, 1)`n        $anchor"); diagnostic = 'CallActivationConflict_Kd' }
    LiveLoanMove = @{ source = (Edit-KimiSource $original $anchor "finish(counter@move)`n        $anchor"); diagnostic = 'MovedPlace_Kd' }
    BareExclusive = @{ source = (Edit-KimiSource $original 'add(counter@uniq, 1)' 'add(counter, 1)'); diagnostic = 'ExclusiveBorrowRequired_Kd' }
    LiveLoanReplacement = @{ source = (Edit-KimiSource $original $anchor "counter = Counter.init()`n        $anchor"); diagnostic = 'ComparisonLoanConflict_Kd' }
    LiveLoanFieldWrite = @{ source = (Edit-KimiSource $original $anchor "counter.value = 99`n        $anchor"); diagnostic = 'ComparisonLoanConflict_Kd' }
    EscapedLocal = @{ source = (Edit-KimiSource $original 'return counter' "let local = Counter.init()`n    return local@ref"); diagnostic = 'TypeMismatch_Kd' }
    SharedWrite = @{ source = (Edit-KimiSource $original 'counter: uniq/Counter' 'counter: ref/Counter'); diagnostic = 'SharedPathAccess_Kd' }
    ImmutableOwner = @{ source = (Edit-KimiSource $original 'var counter = Counter.init()' 'let counter = Counter.init()'); diagnostic = 'InvalidAssignment_Kd' }
    MissingStoredOrigin = @{ source = (Edit-KimiSource $original 'let counter: ref/Counter during source' 'let counter: ref/Counter'); diagnostic = 'MissingOriginBinding_Kd' }
    WrongArgument = @{ source = (Edit-KimiSource $original 'Counter.init()' 'Counter.init(true)'); diagnostic = 'NoApplicableOverload_Kd' }
    WrongReferent = @{ source = (Edit-KimiSource $original 'borrowCounter(counter@ref)' 'borrowCounter(1)'); diagnostic = 'NoApplicableOverload_Kd' }
    MovedRead = @{ source = (Edit-KimiSource $original 'Console.writeLine("Done.")' "let invalid = counter.value`n    Console.writeLine(`"Done.`")"); diagnostic = 'MovedPlace_Kd' }
    TemporaryEscape = @{ source = (Edit-KimiSource $original 'CounterView.init(borrowCounter(counter@ref))' 'CounterView.init(borrowCounter(Counter.init()))'); diagnostic = 'ComparisonLoanConflict_Kd' }
    DoubleExclusive = @{ source = "struct S`n    public var value: i32 = 0`nfunc both(a: uniq/S, b: uniq/S) => ()`nvar s = S.init()`nboth(s@uniq, s@uniq)"; diagnostic = 'CallActivationConflict_Kd' }
    ParentDuringReborrow = @{ source = "struct S`n    public var value: i32 = 0`nfunc bad(s: uniq/S)`n    let r = s@ref`n    s.value = 9`n    let n = r.value`nvar s = S.init()`nbad(s@uniq)"; diagnostic = 'ComparisonLoanConflict_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 5 checks ($Configuration): $report"
