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
$source = Join-Path $repo 'milestones/Milestone1.kimi'
$work = Join-Path $repo "bin/milestone1/$Configuration/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null
$report = Join-Path $work 'verification.json'
@{ status = 'incomplete' } | ConvertTo-Json | Set-Content -LiteralPath $report
$results = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
$dotnet = (Get-Command dotnet).Source
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

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
        $errorText = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "Timed out: $File" }
        $null = $copy.GetAwaiter().GetResult()
        $stderr = $errorText.GetAwaiter().GetResult()
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

function Assert-Output($Actual, [string] $Expected, [string] $Name) {
    $hex = [Convert]::ToHexString($Actual.stdout)
    if ($Actual.stderr -or $hex -cne [Convert]::ToHexString($utf8.GetBytes($Expected))) {
        throw "Output mismatch: $Name; stdout=$hex; stderr=$($Actual.stderr)"
    }
    $results.Add(@{ name = $Name; stdoutHex = $hex; stderr = $Actual.stderr; exitCode = $Actual.exitCode })
}

function Build-And-Run([string] $InputPath, [string] $Directory, [string] $Name, [string] $Level, [string] $Expected, [string] $Case = $Name) {
    $null = Invoke-Kimi @('build', $InputPath, '--ToolchainRoot', $ToolchainRoot)
    $stem = Join-Path $Directory "bin/x86_64-pc-windows-msvc/$Name"
    $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
    if ($record.status -cne 'linked' -or $record.optimization -cne $Level -or
        -not $record.reportedVersionsMatched -or $record.unverifiedToolchain) { throw "Unverified build: $InputPath" }
    # NativeToolchain.Build verifies input IR and, for O2, optimized IR before linking.
    Assert-Output (Invoke-Captured "$stem.$Level.exe" @()) $Expected "$Case.$Level.native"
    Assert-Output (Invoke-Kimi @('run', "$stem.$Level.exe")) $Expected "$Case.$Level.run-exe"
    $run = Invoke-Kimi @('run', $InputPath)
    if ($run.stderr -or -not $utf8.GetString($run.stdout).EndsWith($Expected, [StringComparison]::Ordinal)) {
        throw "Source/project run did not forward output: $InputPath"
    }
    $results.Add(@{ name = "$Case.$Level.run-input"; exitCode = $run.exitCode })
    Copy-Item -LiteralPath "$stem.link.build.json" -Destination (Join-Path $work "$Case.$Level.build.json")
}

# Exercise the actual single-source input, without including Milestone2..5.
Build-And-Run $source (Split-Path $source) 'Milestone1' 'O2' "Hello, world!`n"

# O0 is a project setting. Copy the exact source bytes under another name and
# verify their identity; no milestone source or expected output is simplified.
foreach ($level in @('O0', 'O2')) {
    $directory = Join-Path $work $level
    New-Item -ItemType Directory -Path $directory | Out-Null
    $copy = Join-Path $directory 'Renamed.kimi'
    Copy-Item -LiteralPath $source -Destination $copy
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $sourceHash) { throw 'Source copy differs' }
    $project = Join-Path $directory 'Renamed.kimiproj'
    [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"`n", $utf8)
    Build-And-Run $project $directory 'Renamed' $level "Hello, world!`n"

    # General literal contents, embedded NUL, empty output, and a borrowed local printed twice (SPEC 22.4).
    [IO.File]::WriteAllText($copy, "let text = `"日本語\0x`"`n::Kimi.Console.writeLine(text)`n::Kimi.Console.writeLine(`"`")`n::Kimi.Console.writeLine(text@ref)`n", $utf8)
    Build-And-Run $project $directory 'Renamed' $level "日本語`0x`n`n日本語`0x`n" 'BorrowedUnicode'
}

$invalid = [ordered]@{
    WrongType = '::Kimi.Console.writeLine(1)'
    MissingArgument = '::Kimi.Console.writeLine()'
    ExtraArgument = '::Kimi.Console.writeLine("a", "b")'
    UnknownLabel = '::Kimi.Console.writeLine(other: "a")'
    ExclusiveArgument = "let text = `"a`"`n::Kimi.Console.writeLine(text@uniq)"
    UseAfterMove = "let text = `"a`"`nlet taken = text@move`n::Kimi.Console.writeLine(text)"
    Uninitialized = "let text: string`n::Kimi.Console.writeLine(text)"
    MixedStartup = "public func main() => ()`n::Kimi.Console.writeLine(`"a`")"
    MissingStartup = 'func helper() => ()'
    InvalidMain = 'public func main() -> i32 => 0'
}
foreach ($entry in $invalid.GetEnumerator()) {
    $path = Join-Path $work "$($entry.Key).kimi"
    [IO.File]::WriteAllText($path, $entry.Value, $utf8)
    $failure = Invoke-Kimi @('build', $path, '--ToolchainRoot', $ToolchainRoot) 1
    $text = $utf8.GetString($failure.stdout) + $failure.stderr
    [IO.File]::WriteAllText((Join-Path $work "$($entry.Key).diagnostics.txt"), $text, $utf8)
    $stem = Join-Path $work "bin/x86_64-pc-windows-msvc/$($entry.Key)"
    $record = Get-Content -LiteralPath "$stem.link.build.json" -Raw | ConvertFrom-Json
    if ($record.status -cne 'incomplete' -or (Test-Path "$stem.ll") -or (Test-Path "$stem.O2.exe")) {
        throw "Invalid input reached emission: $($entry.Key)"
    }
    $results.Add(@{ name = $entry.Key; rejected = $true; exitCode = $failure.exitCode })
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $sourceHash -or
    (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -cne $compilerHash) { throw 'Source/compiler changed during verification' }
@{
    status = 'passed'; compilerConfiguration = $Configuration; compilerSha256 = $compilerHash
    sourceSha256 = $sourceHash; tests = $results
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Passed $($results.Count) Milestone 1 checks ($Configuration): $report"
