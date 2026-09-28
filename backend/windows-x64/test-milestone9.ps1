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
$source = Join-Path $repo 'milestones/Milestone9.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone9/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Found 6 at index 3.`nMissing value handled.`nSearch finished.`nBatch destroyed.`n"
Build-And-Run $source (Split-Path $source) 'Milestone9' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    First = @{ source = (Edit-KimiSource $original 'let target: i32 = 6' 'let target: i32 = 2' 'index == 3 and value == 6' 'index == 0 and value == 2' 'Found 6 at index 3.' 'Found 2 at index 0.'); stdout = (Edit-KimiSource $expected 'Found 6 at index 3.' 'Found 2 at index 0.') }
    Last = @{ source = (Edit-KimiSource $original 'let target: i32 = 6' 'let target: i32 = 8' 'index == 3 and value == 6' 'index == 4 and value == 8' 'Found 6 at index 3.' 'Found 8 at index 4.'); stdout = (Edit-KimiSource $expected 'Found 6 at index 3.' 'Found 8 at index 4.') }
    Absent = @{ source = (Edit-KimiSource $original 'let target: i32 = 6' 'let target: i32 = 99' '.Missing => $abort("Expected a match")' '.Missing => Console.writeLine("No match.")'); stdout = (Edit-KimiSource $expected 'Found 6 at index 3.' 'No match.') }
    Singleton = @{ source = (Edit-KimiSource $original '[5 of i32]' '[1 of i32]' '[2, 4, 5, 6, 8]' '[6]' 'find<5, i32>' 'find<1, i32>' 'index == 3' 'index == 0' 'Found 6 at index 3.' 'Found 6 at index 0.'); stdout = (Edit-KimiSource $expected 'Found 6 at index 3.' 'Found 6 at index 0.') }
    Wide = @{ source = (Edit-KimiSource $original 'i32' 'i64'); stdout = $expected }
    NonemptyMissing = @{ source = (Edit-KimiSource $original 'let empty: [0 of i32] = []' 'let empty: [2 of i32] = [0, 1]' 'find<0, i32>' 'find<2, i32>'); stdout = $expected }
    Abort = @{ source = (Edit-KimiSource $original 'value == target)' '$abort("predicate"))'); stdout = ''; abort = '$abort("predicate")' }
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
            $expectedError = "$name.kimi:${line}:${column}: abort KIMI_E_ABORT: predicate`n"
            $expectedExit = 1
        }
        Build-And-Run $project $directory $name $level $variant.stdout $expectedError $expectedExit
    }
}
$invalid = [ordered]@{
    WrongPayload = @{ source = (Edit-KimiSource $original '.Found(index, value)' '.Found(value, index)'); diagnostic = 'TypeMismatch_Kd' }
    WrongCallback = @{ source = (Edit-KimiSource $original 'func [target] (value: i32)' 'func [target] (value: bool)'); diagnostic = 'NoApplicableOverload_Kd' }
    WrongArity = @{ source = (Edit-KimiSource $original 'if accepts(value)' 'if accepts()'); diagnostic = 'NoApplicableOverload_Kd' }
    UnprovedCopy = @{ source = (Edit-KimiSource $original 'T is Copy' ''); diagnostic = 'TransferRequired_Kd' }
    WrongLength = @{ source = (Edit-KimiSource $original 'find<5, i32>' 'find<4, i32>'); diagnostic = 'NoApplicableOverload_Kd' }
    MissingCase = @{ source = (Edit-KimiSource $original '.Missing => $abort("Expected a match")' ''); diagnostic = 'NonExhaustiveMatch_Kd' }
    CallbackMoved = @{ source = (Edit-KimiSource $original 'if accepts(value)' "let moved = accepts`n                if accepts(value)"); diagnostic = 'TransferRequired_Kd' }
    ReturnedBorrow = @{ source = (Edit-KimiSource $original 'return self.value@ref/T' "let local: T = self.value`n                return local@ref/T"); diagnostic = 'TypeMismatch_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 9 checks ($Configuration): $report"
