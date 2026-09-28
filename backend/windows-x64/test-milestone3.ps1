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
$source = Join-Path $repo 'milestones/Milestone3.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone3/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Leaving sumTo.`nSum is 55.`nLeaving main.`n"
Build-And-Run $source (Split-Path $source) 'Milestone3' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
if (-not $original.Contains('sumTo(10)') -or -not $original.Contains('var total: i32 = 0')) {
    throw 'Review the variants against the current source'
}
foreach ($level in @('O0', 'O2')) {
    foreach ($name in @('Renamed', 'NegativeLimit', 'UnexpectedSum', 'ReturnSnapshot')) {
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        switch ($name) {
            'Renamed' {
                Copy-Item -LiteralPath $source -Destination $copy
                if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
            }
            'NegativeLimit' { [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'sumTo(10)' 'sumTo(-1)'), $utf8) }
            'UnexpectedSum' { [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'sumTo(10)' 'sumTo(9)'), $utf8) }
            'ReturnSnapshot' {
                # The returned i32 must be secured before deferred mutation of its source.
                [IO.File]::WriteAllText($copy, (Edit-KimiSource $original 'var total: i32 = 0' "var total: i32 = 0`n    defer => total = -1"), $utf8)
            }
        }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        switch ($name) {
            'NegativeLimit' { Build-And-Run $project $directory $name $level '' "NegativeLimit.kimi:5:9: abort KIMI_E_ABORT: Limit must be nonnegative`n" 1 }
            'UnexpectedSum' { Build-And-Run $project $directory $name $level "Leaving sumTo.`n" "UnexpectedSum.kimi:18:9: abort KIMI_E_ABORT: Unexpected sum`n" 1 }
            default { Build-And-Run $project $directory $name $level $expected }
        }
    }
}

$invalid = [ordered]@{
    WrongArgument = @{ source = "func f(x: i32) => ()`npublic func main() => f(true)"; diagnostic = 'NoApplicableOverload_Kd' }
    MissingArgument = @{ source = "func f(x: i32) => ()`npublic func main() => f()"; diagnostic = 'NoApplicableOverload_Kd' }
    ExtraArgument = @{ source = "func f(x: i32) => ()`npublic func main() => f(1, 2)"; diagnostic = 'NoApplicableOverload_Kd' }
    WrongReturn = @{ source = "func f() -> i32 => return true`npublic func main() => f()"; diagnostic = 'TypeMismatch_Kd' }
    MissingReturn = @{ source = "func f() -> i32`n    let x: i32 = 1`npublic func main() => f()"; diagnostic = 'ControlFlow_Kd' }
    InvalidMain = @{ source = 'public func main() -> i32 => 0'; diagnostic = 'InvalidStartupMain_Kd' }
    MixedStartup = @{ source = "public func main() => ()`nConsole.writeLine(`"mixed`")"; diagnostic = 'MixedStartupBodies_Kd' }
    ReturnFromDefer = @{ source = "public func main()`n    defer => return"; diagnostic = 'ControlFlow_Kd' }
    DeferredMovedUse = @{ source = "public func main()`n    let text = `"x`"`n    defer => Console.writeLine(text)`n    let taken = text@move"; diagnostic = 'MovedPlace_Kd' }
    DeferredUninitializedUse = @{ source = "public func main()`n    let text: string`n    defer => Console.writeLine(text)"; diagnostic = 'UninitializedPlace_Kd' }
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
Write-Output "Passed $($results.Count) Milestone 3 checks ($Configuration): $report"
