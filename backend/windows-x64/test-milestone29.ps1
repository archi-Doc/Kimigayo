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
$source = Join-Path $repo 'milestones/Milestone29.kimi'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$compilerHash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
$work = Join-Path $repo "bin/milestone29/$Configuration/$([guid]::NewGuid().ToString('N'))"
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

$expected = "No tasks.`nMany tasks.`nRemoved the last task.`nTask 1 destroyed.`nPopped a task.`nTask 3 destroyed.`nTwo tasks.`nTask 6 destroyed.`nCleared the spare tasks.`nIterating task 5.`nTask 5 destroyed.`nTask 2 destroyed.`nArray run finished.`nTask 4 destroyed.`n"
if ($Cases -eq 'All') { Build-And-Run $source (Split-Path $source) 'Milestone29' 'O2' $expected }
$original = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
# The source is immutable; all behavioral variants live in private harness directories.
$complete = Edit-KimiSource $original 'require task.id == 5 else' 'require task.id == 5 or task.id == 2 else' 'Console.writeLine("Iterating task 5.")' 'Console.writeLine("Iterating.")' '        exit // task is destroyed first; the iterator then destroys the unyielded Task 2.' ''
$completeOutput = Edit-KimiSource $expected "Iterating task 5.`nTask 5 destroyed.`nTask 2 destroyed." "Iterating.`nTask 5 destroyed.`nIterating.`nTask 2 destroyed."
$capacityChecks = '    let spareCapacity = spare.capacity' + "`n" + '    spare@uniq.reserve(0)' + "`n" + '    spare@uniq.clear()' + "`n" + '    match spare@uniq.pop()' + "`n" + '        .None => ()' + "`n" + '        .Some(_) => $abort("Empty pop")' + "`n" + '    require spare.length == 0 and spare.capacity == spareCapacity else => $abort("No-op capacity")' + "`n" + '    spare@uniq.shrinkToFit()' + "`n" + '    require spare.length == 0 and spare.capacity <= spareCapacity else => $abort("Shrink")' + "`n" + '    Console.writeLine("Cleared the spare tasks.")'
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = (Edit-KimiSource $original 'Task' 'Job' 'tasks' 'jobs' 'describe' 'inspect'); stdout = (Edit-KimiSource $expected 'Task' 'Job' 'tasks' 'jobs') }
    Wide = @{ source = (Edit-KimiSource $original 'id: i32' 'id: i64'); stdout = $expected }
    Growth = @{ source = (Edit-KimiSource $original 'reserve(additional: 4)' 'reserve(additional: 0)' 'tasks.length == 0 and tasks.capacity >= 4' 'tasks.length == 0 and tasks.capacity == 0'); stdout = $expected }
    IndexValues = @{ source = (Edit-KimiSource $original 'insert(^0,' 'insert(Index.init(0, fromEnd: true),' 'remove(^1)' 'remove(Index.init(1, fromEnd: true))'); stdout = $expected }
    CompleteIteration = @{ source = $complete; stdout = $completeOutput }
    Capacity = @{ source = (Edit-KimiSource $original '    Console.writeLine("Cleared the spare tasks.")' $capacityChecks); stdout = $expected }
    SharedReads = @{ source = (Edit-KimiSource $original '    match tasks@uniq.pop()' ('    let item = tasks[0]' + "`n" + '    let explicit = tasks[0]@ref' + "`n" + '    require item.id == 5 and explicit.id == 5 else => $abort("Shared read")' + "`n" + '    match tasks@uniq.pop()')); stdout = $expected }
    SharedSliceIteration = @{ source = (Edit-KimiSource $original '    var spare: Array<Task>' ('    let view = tasks[..]' + "`n" + '    require view.length == 2 and view[0].id == 5 else => $abort("Slice")' + "`n" + '    var total = 0' + "`n" + '    for item in tasks => total += item.id' + "`n" + '    require total == 7 else => $abort("Shared iteration")' + "`n" + '    var spare: Array<Task>')); stdout = $expected }
    ResultIteration = @{ source = (Edit-KimiSource $original 'for task in tasks@move' 'for task in (if true => tasks@move else => tasks@move)'); stdout = $expected }
    ClearAbort = @{ source = "struct Task`n    public let id: i32`n    public init(id: i32) => self.id = id`n    deinit`n        if self.id == 2 => `$abort(`"stop`")`n        Console.writeLine(`"drop`")`nvar values: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]`nvalues@uniq.clear()`nConsole.writeLine(`"after`")`n"; stdout = "drop`n"; exit = 1; stderr = '{name}.kimi:5:28: abort KIMI_E_ABORT: stop' + "`n" }
    AbandonedArgument = @{ source = "struct Task`n    public let id: i32`n    public init(id: i32) => self.id = id`n    deinit => Console.writeLine(`"drop`")`nvar values: Array<Task> = []`nlet completed = work: do`n    values@uniq.insert(value: Task.init(1), index: (index: do`n        exit to work: false`n        exit to index: 0@isize`n    ))`n    exit to work: true`nrequire not completed and values.length == 0 else => `$abort(`"abandon`")`nConsole.writeLine(`"done`")`n"; stdout = "drop`ndone`n" }
    EmptyRemove = @{ source = "var values: Array<i32> = []`nlet n = values@uniq.remove(^1)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    EndRemove = @{ source = "var values: Array<i32> = [1]`nlet n = values@uniq.remove(^0)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    InvalidInsert = @{ source = "var values: Array<i32> = []`nvalues@uniq.insert(^1, 7)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    NegativeReserve = @{ source = "var values: Array<i32> = []`nvalues@uniq.reserve(-1)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:1: abort KIMI_E_ARGUMENT: Invalid argument value' + "`n" }
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
        $stderr = if ($variant.stderr) { $variant.stderr.Replace('{name}', $name) } else { '' }
        Build-And-Run $project $directory $name $level $variant.stdout $stderr ([int]$variant.exit)
    }
}
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    MovedArray = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let invalid = tasks.length' + "`n" + '    Console.writeLine("Array run finished.")')); diagnostic = 'MovedPlace_Kd' }
    ImplicitTransfer = @{ source = (Edit-KimiSource $original '    let last = tasks@uniq.remove(^1)' ('    let invalid = tasks' + "`n" + '    let last = tasks@uniq.remove(^1)')); diagnostic = 'TransferRequired_Kd' }
    MissingExclusive = @{ source = (Edit-KimiSource $original 'tasks@uniq.append(Task.init(1))' 'tasks.append(Task.init(1))'); diagnostic = 'ExclusiveBorrowRequired_Kd' }
    SharedMutation = @{ source = (Edit-KimiSource $original 'tasks@uniq.append(Task.init(1))' 'tasks@ref.append(Task.init(1))'); diagnostic = 'NoApplicableOverload_Kd' }
    ElementMove = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let invalid = tasks[0]@move' + "`n" + '    tasks[0] = Task.init(5)')); diagnostic = 'UnsupportedOwnership_Kd' }
    LiveBorrow = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks@ref' + "`n" + '    tasks[0] = Task.init(5)' + "`n" + '    let invalid = view.length')); diagnostic = 'ComparisonLoanConflict_Kd' }
    LiveElement = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks[0]' + "`n" + '    tasks[0] = Task.init(5)' + "`n" + '    let invalid = view.id')); diagnostic = 'ComparisonLoanConflict_Kd' }
    # Valid forms outside the verified generation boundary: rejected by ownership analysis, never at generation.
    ZeroSizedElement = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let units: Array<()> = [()]' + "`n" + '    Console.writeLine("Array run finished.")')); diagnostic = 'UnsupportedOwnership_Kd' }
    SharedStringIteration = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let names: Array<string> = ["name"]' + "`n" + '    for name in names => ()' + "`n" + '    Console.writeLine("Array run finished.")')); diagnostic = 'UnsupportedOwnership_Kd' }
    LiveEmptySlice = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks[0..0]' + "`n" + '    tasks@uniq.reserve(0)' + "`n" + '    let invalid = view.length' + "`n" + '    tasks[0] = Task.init(5)')); diagnostic = 'CallActivationConflict_Kd' }
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
        if ($plainDiagnostic -notmatch ('\b(?:' + $required + ')\b') -or $plainDiagnostic -match '\bGenerationFailed_Kd\b' -or $record.status -cne 'incomplete' -or
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
Write-Output "Passed $($results.Count) Milestone 29 checks ($Configuration): $report"
