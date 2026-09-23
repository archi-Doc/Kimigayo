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
$source = Join-Path $repo 'milestones/Milestone16.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone16/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$one = "External borrow survived.`nBoth sources updated.`n"
$expected = $one + $one + "Origin forwarding finished.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone16' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = $original.Replace('Cell', 'Counter').Replace('Pair', 'Inputs').Replace('Selection', 'Choice').Replace('selected', 'chosen'); stdout = $expected }
    Values = @{ source = $original.Replace('Cell.init(10)', 'Cell.init(31)').Replace('Cell.init(20)', 'Cell.init(42)').Replace('=> 10 else => 20', '=> 31 else => 42').Replace('first.value == 13 and second.value == 24', 'first.value == 34 and second.value == 46'); stdout = $expected }
    Reverse = @{ source = $original.Replace('if useFirst', 'if not useFirst'); stdout = $expected }
    SameOrigin = @{ source = $original.Replace('Pair.init(first@ref, second@ref)', 'Pair.init(first@ref, first@ref)').Replace('=> 10 else => 20', '=> 10 else => 10'); stdout = $expected }
    Missing = @{ source = $original.Replace('return .Found(self.second)', 'return .Missing').Replace('.Missing => $abort("Expected a selected Cell")', '.Missing => first@ref').Replace('=> 10 else => 20', '=> 10 else => 10'); stdout = $expected }
    RetainedEnum = @{ source = $original.Replace('            let result = pair.choose(useFirst)', "            let retained = pair.choose(useFirst)`n            let result = retained@move"); stdout = $expected }
}
foreach ($level in @('O0', 'O2')) {
    foreach ($entry in $variants.GetEnumerator()) {
        if ($Cases -ne 'All') { continue }
        $name = $entry.Key
        $variant = $entry.Value
        if ($name -ne 'Renamed' -and $variant.source -ceq $original) { throw "Variant mutation did not change the input: $name" }
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        if ($name -eq 'Renamed') {
            Copy-Item -LiteralPath $source -Destination $copy
            if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
        } else { [IO.File]::WriteAllText($copy, $variant.source, $utf8) }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        Build-And-Run $project $directory $name $level $variant.stdout
    }
}
$invalid = [ordered]@{
    StaticResult = $original.Replace('origin selection.source == self.left and self.right', 'origin selection.source == static')
    IncompleteIntersection = $original.Replace('origin selection.source == self.left and self.right', 'origin selection.source == self.left')
    FirstWrite = $original.Replace('        require selected.value', "        first.value = 99`n        require selected.value")
    SecondWrite = $original.Replace('        require selected.value', "        second.value = 99`n        require selected.value")
    ParentWrite = $original.Replace('        child.value =', "        target.value = 99`n        child.value =")
    EscapingLocal = $original.Replace("    var first = Cell.init(10)`n", '').Replace('            let pair =', "            var first = Cell.init(10)`n            let pair =").Replace("    add(first@uniq, 2)`n", '').Replace('first.value == 13 and ', '')
}
foreach ($level in @('O0', 'O2')) {
    foreach ($entry in $invalid.GetEnumerator()) {
        if ($entry.Value -ceq $original) { throw "Rejection mutation did not change the input: $($entry.Key)" }
        $name = $entry.Key
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $path = Join-Path $directory "$name.kimi"
        [IO.File]::WriteAllText($path, $entry.Value, $utf8)
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        $failure = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot) 1
        $diagnostic = $utf8.GetString($failure.stdout) + $failure.stderr
        [IO.File]::WriteAllText((Join-Path $directory 'diagnostics.txt'), $diagnostic, $utf8)
        $stem = Join-Path $directory "bin/x86_64-pc-windows-msvc/$name"
        $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
        $required = if ($name -in @('StaticResult','IncompleteIntersection')) { 'TypeMismatch_Kd|OriginOutlives_Kd|InvalidOrigin_Kd' } else { 'ComparisonLoanConflict_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 16 checks ($Configuration): $report"
