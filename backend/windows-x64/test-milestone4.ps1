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
$source = Join-Path $repo 'milestones/Milestone4.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone4/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "Counter created.`nSum is 55.`nLeaving finish.`nCounter destroyed.`nDone.`n"
Build-And-Run $source (Split-Path $source) 'Milestone4' 'O2' $expected
$original = [IO.File]::ReadAllText($source)
if (-not $original.Contains('while number <= 10') -or -not $original.Contains('deinit => Console.writeLine("Counter destroyed.")')) {
    throw 'Review the variants against the current source'
}
foreach ($level in @('O0', 'O2')) {
    foreach ($name in @('Renamed', 'AbortSum', 'DeinitObserves')) {
        $directory = Join-Path $work "$level/$name"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory "$name.kimi"
        switch ($name) {
            'Renamed' {
                Copy-Item -LiteralPath $source -Destination $copy
                if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
            }
            'AbortSum' { [IO.File]::WriteAllText($copy, $original.Replace('while number <= 10', 'while number <= 9'), $utf8) }
            'DeinitObserves' {
                $body = "deinit`n        if self.value != 55`n            `$abort(`"Destroyed value changed`")`n        Console.writeLine(`"Counter destroyed.`")"
                [IO.File]::WriteAllText($copy, $original.Replace('deinit => Console.writeLine("Counter destroyed.")', $body), $utf8)
            }
        }
        $project = Join-Path $directory "$name.kimiproj"
        [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
        if ($name -eq 'AbortSum') {
            Build-And-Run $project $directory $name $level "Counter created.`n" "AbortSum.kimi:14:9: abort KIMI_E_ABORT: Unexpected sum`n" 1
        } else { Build-And-Run $project $directory $name $level $expected }
    }
}

$invalid = [ordered]@{
    MovedRead = $original.Replace('finish(counter@move) //', "finish(counter@move)`n    let invalid = counter.value //")
    DoubleMove = $original.Replace('finish(counter@move) //', "finish(counter@move)`n    finish(counter@move) //")
    BareTransfer = $original.Replace('finish(counter@move) //', 'finish(counter) //')
    Incomplete = $original.Replace('self.value = 0', '()')
    ConditionalInit = $original.Replace('self.value = 0', 'if false => self.value = 0')
    LateInit = $original.Replace('self.value = 0', 'defer => self.value = 0')
    UninitializedRead = $original.Replace('self.value = 0', 'self.value = self.value + 1')
    ImmutableOwner = $original.Replace('var counter =', 'let counter =')
    PrivateConstructor = $original.Replace('public init()', 'init()')
    WrongArgument = $original.Replace('Counter.init()', 'Counter.init(true)')
    ExplicitDeinit = $original.Replace('finish(counter@move) //', 'counter.deinit() //')
    CopyWithDeinit = $original.Replace('struct Counter', "struct Counter`n    Self is Copy")
    PartialMove = "struct S`n    public var text: string`n    public init() => self.text = `"held`"`n    deinit => ()`nlet s = S.init()`nlet taken = s.text@move"
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
Write-Output "Passed $($results.Count) Milestone 4 checks ($Configuration): $report"
