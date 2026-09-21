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
$source = Join-Path $repo 'milestones/Milestone17.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone17/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Exchange scope finished.`nResource 2 destroyed.`nResource 3 destroyed.`nPrepared result.`nResource 1 destroyed.`nResult received.`nResource 5 destroyed.`nResource 4 destroyed.`nCleanup finished.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone17' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$values = $original
$valueOutput = $expected
foreach ($n in 1..5) {
    $id = $n * 11
    $values = $values.Replace("Resource.init($n)", "Resource.init($id)").Replace("$n => Console", "$id => Console").Replace("== $n ", "== $id ").Replace("Resource $n destroyed.", "Resource $id destroyed.")
    $valueOutput = $valueOutput.Replace("Resource $n destroyed.", "Resource $id destroyed.")
}
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = $original.Replace('Resource', 'Handle').Replace('items', 'storage').Replace('prepare', 'assemble'); stdout = $expected.Replace('Resource', 'Handle') }
    Values = @{ source = $values; stdout = $valueOutput }
    Implicit = @{ source = $original.Replace('@uniq', ''); stdout = $expected }
    Typed = @{ source = $original.Replace('@uniq', '@uniq/Resource'); stdout = $expected }
    Literal = @{ source = $original.Replace('items[0]', 'items[((0x0))]').Replace('items[1]', 'items[(0b1)]'); stdout = $expected }
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
        } else { [IO.File]::WriteAllText($copy, $variant.source, $utf8) }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        Build-And-Run $project $directory $name $level $variant.stdout
    }
}
$invalid = [ordered]@{
    MissingReturn = @{ source = $original.Replace('return items //', 'items //'); diagnostic = 'ControlFlow_Kd' }
    MissingRepair = @{ source = $original.Replace('    items[0] = Resource.init(3)', '    // items[0] = Resource.init(3)'); diagnostic = 'MovedPlace_Kd' }
    MovedRead = @{ source = $original.Replace('    items[0] = Resource.init(3)', "    let invalid = items[0].id`n    items[0] = Resource.init(3)"); diagnostic = 'MovedPlace_Kd' }
    Overlap = @{ source = $original.Replace('swap(items[0]@uniq, items[1]@uniq)', 'swap(items[0]@uniq, items[((0x0))]@uniq)'); diagnostic = 'CallReservationConflict_Kd|CallActivationConflict_Kd|ComparisonLoanConflict_Kd' }
    ReservedMove = @{ source = $original.Replace('with: Resource.init(4)', 'with: items[1]'); diagnostic = 'CallReservationConflict_Kd|ComparisonLoanConflict_Kd' }
    DeferredMovedRead = @{ source = $original.Replace('    return items', "    defer => Console.writeLine(if items[0].id == 4 => `"bad`" else => `"bad`")`n    return items"); diagnostic = 'MovedPlace_Kd' }
    Immutable = @{ source = $original.Replace('    var items:', '    let items:'); diagnostic = 'InvalidAssignment_Kd|InvalidConversion_Kd|ImmutablePlace_Kd|UnsupportedBinding_Kd' }
    RetainedBorrow = @{ source = $original.Replace('    Kimi.Intrinsics.replace', "    let held = items[1]@ref`n    Kimi.Intrinsics.replace").Replace('    require items[1].id == 5', '    require held.id == 5'); diagnostic = 'ComparisonLoanConflict_Kd|CallActivationConflict_Kd' }
    UnknownOverlap = @{ source = $original.Replace('    Kimi.Intrinsics.swap', "    let index: isize = 1`n    Kimi.Intrinsics.swap").Replace('swap(items[0]@uniq, items[1]@uniq)', 'swap(items[0]@uniq, items[index]@uniq)'); diagnostic = 'UnsupportedOwnership_Kd|CallReservationConflict_Kd|CallActivationConflict_Kd|ComparisonLoanConflict_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 17 checks ($Configuration): $report"
