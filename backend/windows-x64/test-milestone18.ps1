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
$source = Join-Path $repo 'milestones/Milestone18.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone18/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Resource 4 destroyed.`nResource 3 destroyed.`nGeneric result secured.`nResources received.`nResource 2 destroyed.`nResource 1 destroyed.`nGeneric result secured.`nTuple received.`nGeneric result secured.`nGeneric values finished.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone18' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$second = $original.Replace("        true)", "        false)").Replace('items[0].id == 1 and items[1].id == 2', 'items[0].id == 3 and items[1].id == 4')
$secondOutput = "Resource 2 destroyed.`nResource 1 destroyed.`nGeneric result secured.`nResources received.`nResource 4 destroyed.`nResource 3 destroyed.`nGeneric result secured.`nTuple received.`nGeneric result secured.`nGeneric values finished.`n"
$values = $original.Replace('Resource.init(1)', 'Resource.init(11)').Replace('Resource.init(2)', 'Resource.init(12)').Replace('Resource.init(3)', 'Resource.init(13)').Replace('Resource.init(4)', 'Resource.init(14)')
foreach ($id in 1..4) { $values = $values.Replace("            $id =>", "            $($id + 10) =>").Replace("Resource $id destroyed.", "Resource $($id + 10) destroyed.") }
$values = $values.Replace('items[0].id == 1 and items[1].id == 2', 'items[0].id == 11 and items[1].id == 12')
$valueOutput = $expected
foreach ($id in 1..4) { $valueOutput = $valueOutput.Replace("Resource $id destroyed.", "Resource $($id + 10) destroyed.") }
$unused = [regex]::Replace($original, '(?ms)^        match delivered\n.*?(?=^    do\n)', "        Console.writeLine(`"Resources received.`")`n")
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = $original.Replace('Resource', 'Ticket').Replace('Box', 'Crate').Replace('Delivery', 'Parcel').Replace('relay', 'send').Replace('pending', 'secured').Replace('choose', 'select').Replace('package', 'wrap'); stdout = $expected.Replace('Resource', 'Ticket') }
    Values = @{ source = $values; stdout = $valueOutput }
    Second = @{ source = $second; stdout = $secondOutput }
    Early = @{ source = $original.Replace("    return pending@move", "    if true => return pending@move`n    return pending@move"); stdout = $expected }
    Unused = @{ source = $unused; stdout = $expected }
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
    SelectedMoved = @{ source = $original.Replace('        match delivered@move', "        let invalid = selected@move`n        match delivered@move"); diagnostic = 'MovedPlace_Kd' }
    DeliveredMoved = @{ source = $original.Replace('            .Missing => $abort("Missing resources")', "            .Missing => `$abort(`"Missing resources`")`n        let invalid = delivered@move"); diagnostic = 'MovedPlace_Kd' }
    BoxDestructor = @{ source = $original.Replace('    // No user deinit:', "    deinit => ()`n    // No user deinit:"); diagnostic = 'UnsupportedOwnership_Kd' }
    Duplicate = @{ source = "func duplicate<T>(value: T) -> (T, T) => (value@move, value@move)`n" + $original; diagnostic = 'MovedPlace_Kd' }
    DeferredMove = @{ source = $original.Replace('    return pending@move', "    defer => discard(pending@move)`n    return pending@move") + "`nfunc discard<T>(value: T) => ()`n"; diagnostic = 'MovedPlace_Kd' }
    DeferredRead = @{ source = $original.Replace('    return pending@move', "    defer => observe<T>(pending@ref/T)`n    return pending@move") + "`nfunc observe<T>(value: ref/T) => ()`n"; diagnostic = 'MovedPlace_Kd' }
    TypeMismatch = @{ source = $original.Replace('package(relay(selected@move.take()))', 'package<[2 of i32]>(relay(selected@move.take()))'); diagnostic = 'NoApplicableOverload_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 18 checks ($Configuration): $report"
