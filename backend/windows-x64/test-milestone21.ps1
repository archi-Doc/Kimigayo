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
$source = Join-Path $repo 'milestones/Milestone21.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone21/$Configuration/$([guid]::NewGuid().ToString('N'))"
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
        return @{ stdout = $bytes; stderr = $stderr; exitCode = $process.exitCode }
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

$default = "Default index evaluated.`n"
$expected = "${default}Specialized selection is 30.`nExplicit selection is 10.`n${default}Forwarded selection is 30.`n${default}Ordinary selection is 4.`n${default}Local selection is 9.`nSpecialization finished.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone21' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$header = "specialize func pick<3, i32>(`n    values: ref/[3 of i32] during source, index: isize) -> ref/i32 during source`n"
$specialization = "// All generic slots are fixed. The Origin binder and default are inherited.`n" + $header +
    "    require index >= 0 and index < 3 else => `$abort(`"Invalid specialized index`")`n    return values[2 - index]@ref/i32`n`n"
if (-not $original.Contains($specialization)) { throw 'The specialization block anchor does not match the program.' }
# README separate checks: the specialization removed (10/30/10/4/7), every index supplied (no default
# messages), other values, other names and another source lifetime; selection never changes.
$noSpecialization = (Edit-KimiSource $original $specialization '' 'selected == 30' 'selected == 10' 'Specialized selection is 30.' 'Specialized selection is 10.' 'explicit == 10' 'explicit == 30' 'Explicit selection is 10.' 'Explicit selection is 30.' 'forwarded == 30' 'forwarded == 10' 'Forwarded selection is 30.' 'Forwarded selection is 10.' 'shortLived == 9' 'shortLived == 7' 'Local selection is 9.' 'Local selection is 7.')
$explicitIndex = (Edit-KimiSource $original 'pick<3, i32>(numbers@ref)' 'pick<3, i32>(numbers@ref, index: 0)' 'pick<N, T>(values)' 'pick<N, T>(values, index: 0)' 'pick<2, i64>(wide@ref)' 'pick<2, i64>(wide@ref, index: 0)')
$values = (Edit-KimiSource $original '[10, 20, 30]' '[12, 22, 32]' 'selected == 30' 'selected == 32' 'Specialized selection is 30.' 'Specialized selection is 32.' 'explicit == 10' 'explicit == 12' 'Explicit selection is 10.' 'Explicit selection is 12.' 'forwarded == 30' 'forwarded == 32' 'Forwarded selection is 30.' 'Forwarded selection is 32.')
$lifetime = (Edit-KimiSource $original "    let numbers: [3 of i32] = [10, 20, 30]`n    do`n        let selected" "    do`n        let numbers: [3 of i32] = [10, 20, 30]`n        let selected" "    do`n        let explicit" "    let numbers: [3 of i32] = [10, 20, 30]`n    do`n        let explicit")
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    NoSpecialization = @{ source = $noSpecialization; stdout = "${default}Specialized selection is 10.`nExplicit selection is 30.`n${default}Forwarded selection is 10.`n${default}Ordinary selection is 4.`n${default}Local selection is 7.`nSpecialization finished.`n" }
    ExplicitIndex = @{ source = $explicitIndex; stdout = (Edit-KimiSource $expected $default '') }
    Values = @{ source = $values; stdout = (Edit-KimiSource $expected 'Specialized selection is 30.' 'Specialized selection is 32.' 'Explicit selection is 10.' 'Explicit selection is 12.' 'Forwarded selection is 30.' 'Forwarded selection is 32.') }
    Names = @{ source = (Edit-KimiSource $original 'pick' 'choose' 'forward<' 'relay<'); stdout = $expected }
    Lifetime = @{ source = $lifetime; stdout = $expected }
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
# README rejections: redeclared default or boundary, added/renamed binder, static (narrowed) result,
# mismatched result Type, label or parameter structure, partial specialization, duplicate key, ambiguous
# original and an invalid ordinary generic body.
$signature = '    values: ref/[3 of i32] during source, index: isize) -> ref/i32 during source'
$ambiguous = "func pick<length N, T>(`n    values: ref/[N of i32] during source, index: isize = 0) -> ref/i32 during source => values[index]@ref/i32`n`n"
$invalid = [ordered]@{
    RedeclaredDefault = @{ source = (Edit-KimiSource $original $signature '    values: ref/[3 of i32] during source, index: isize = defaultIndex()) -> ref/i32 during source'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    RedeclaredBoundary = @{ source = (Edit-KimiSource $original $signature '    ! values: ref/[3 of i32] during source, index: isize) -> ref/i32 during source'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    AddedBinder = @{ source = (Edit-KimiSource $original $signature '    values: ref/[3 of i32] during extra, index: isize) -> ref/i32 during extra'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    StaticResult = @{ source = (Edit-KimiSource $original $signature '    values: ref/[3 of i32] during source, index: isize) -> ref/i32 during static'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    WrongResult = @{ source = (Edit-KimiSource $original $signature '    values: ref/[3 of i32] during source, index: isize) -> i32'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    WrongLabel = @{ source = (Edit-KimiSource $original $signature '    items: ref/[3 of i32] during source, index: isize) -> ref/i32 during source' 'return values[2 - index]@ref/i32' 'return items[2 - index]@ref/i32'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    WrongStructure = @{ source = (Edit-KimiSource $original $signature '    values: ref/[3 of i32] during source) -> ref/i32 during source' 'require index >= 0 and index < 3' 'let index: isize = 0' 'return values[2 - index]@ref/i32' 'return values[2]@ref/i32'); diagnostic = 'SpecializationInputMismatch_Kd' }
    Partial = @{ source = (Edit-KimiSource $original 'specialize func pick<3, i32>(' 'specialize func pick<3, T>('); diagnostic = 'InvalidTypeFormation_Kd' }
    Duplicate = @{ source = (Edit-KimiSource $original $specialization ($specialization + $specialization)); diagnostic = 'DuplicateBinding_Kd' }
    Ambiguous = @{ source = (Edit-KimiSource $original $specialization ($ambiguous + $specialization)); diagnostic = 'AmbiguousBinding_Kd' }
    InvalidOrdinaryBody = @{ source = (Edit-KimiSource $original '    return values[index]@ref/T' '    return index'); diagnostic = 'TypeMismatch_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 21 checks ($Configuration): $report"
