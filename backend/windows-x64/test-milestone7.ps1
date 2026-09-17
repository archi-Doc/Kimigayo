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
$source = Join-Path $repo 'milestones/Milestone7.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone7/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Row finished.`nRow finished.`nRow finished.`nMatrix total is 42.`nBorrowed row total is 20.`n"
Build-And-Run $source (Split-Path $source) 'Milestone7' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Triple = @{ source = $original.Replace('value * 2', 'value * 3').Replace('total == 42', 'total == 63').Replace('rowTotal == 20', 'rowTotal == 30'); stdout = $expected }
    StopAtZero = @{ source = $original.Replace('continue to rows', 'exit to rows').Replace('total == 42', 'total == 20'); stdout = $expected.Replace("Row finished.`nRow finished.`nRow finished.`n", "Row finished.`nRow finished.`n") }
    Exhaust = @{ source = $original.Replace('value == 7', 'value == 99').Replace('total == 42', 'total == 72').Replace('matrix[2][2] == 7', 'matrix[2][2] == 14'); stdout = $expected }
    MatrixBounds = @{ source = $original.Replace('matrix[2][2] == 7', 'matrix[3][2] == 7'); stdout = "Row finished.`n" * 3; bounds = 'matrix[3][2]' }
    SliceBounds = @{ source = $original.Replace('rowView[index]', 'rowView[index + 1]'); stdout = ("Row finished.`n" * 3) + "Matrix total is 42.`n"; bounds = 'rowView[index + 1]' }
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
        if ($variant.bounds) {
            $lines = $variant.source -split '\r?\n'
            $line = 0
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i].Contains($variant.bounds)) { $line = $i + 1; $column = $lines[$i].IndexOf($variant.bounds) + 1; break }
            }
            if ($line -eq 0) { throw 'Missing bounds source location' }
            $expectedError = "$name.kimi:${line}:${column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds`n"
            $expectedExit = 1
        }
        Build-And-Run $project $directory $name $level $variant.stdout $expectedError $expectedExit
    }
}
$invalid = [ordered]@{
    OuterLength = $original.Replace('[3 of [4 of i32]]', '[2 of [4 of i32]]')
    InnerLength = $original.Replace('[3 of [4 of i32]]', '[3 of [3 of i32]]')
    SliceWrite = $original.Replace('var rowTotal: i32 = 0', 'rowView[0] = 99')
    BorrowedWrite = $original.Replace('var rowTotal: i32 = 0', "var rowTotal: i32 = 0`n    matrix[0][0] = 99")
    BorrowedReplacement = $original.Replace('var rowTotal: i32 = 0', "var rowTotal: i32 = 0`n    matrix[0] = [9, 9, 9, 9]")
    ImmutableIndex = $original.Replace('let value = matrix[row][column]', 'row = 0')
    EscapedIndex = $original.Replace('require total == 42', 'require row == 0')
    WrongExitResult = $original.Replace('exit to rows //', 'exit to rows: 1 //')
    MissingTarget = $original.Replace('continue to rows', 'continue to missing')
    RawRange = $original.Replace('for row in matrix.indices', 'for row in 0..3')
    WrongIndexType = $original.Replace('rowView[index]', 'rowView[index@i32]')
    TupleBinding = $original.Replace('for row in matrix.indices', 'for (row, other) in matrix.indices')
    Uninitialized = $original.Replace('let rowView = matrix[0][..]', "let other: [4 of i32]`n    let rowView = other[..]")
}
foreach ($entry in $invalid.GetEnumerator()) {
    $path = Join-Path $work "$($entry.Key).kimi"
    [IO.File]::WriteAllText($path, $entry.Value, $utf8)
    $failure = Invoke-Kimi @('build', $path, '--ToolchainRoot', $ToolchainRoot) 1
    $diagnostic = $utf8.GetString($failure.stdout) + $failure.stderr
    [IO.File]::WriteAllText((Join-Path $work "$($entry.Key).diagnostics.txt"), $diagnostic, $utf8)
    $stem = Join-Path $work "bin/x86_64-pc-windows-msvc/$($entry.Key)"
    $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
    if ($diagnostic -notmatch '\b\w+_Kd\b' -or $record.status -cne 'incomplete' -or
        (Test-Path "$stem.ll") -or (Test-Path "$stem.O2.exe")) { throw "Invalid input was not diagnosed before emission: $($entry.Key)" }
    $results.Add(@{ name = $entry.Key; rejected = $true; exitCode = $failure.exitCode })
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $sourceHash -or
    (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -cne $compilerHash) { throw 'Source/compiler changed during verification' }
@{
    status = 'passed'; compilerConfiguration = $Configuration; compilerSha256 = $compilerHash
    sourceSha256 = $sourceHash; tests = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Passed $($results.Count) Milestone 7 checks ($Configuration): $report"