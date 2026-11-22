# The native fixture check: each selected fixture built at O0 and O2 by the configuration's compiler, then run against its sidecars.
# Builds: a fixture's IR becomes two IR/manifest pairs, Name.ll beside Name.link.json as emit writes them (the O2 pair in the output
# directory, the O0 pair in its O0 folder). Their manifest is the one kimi emit writes for a one-line program (one emit per run: this
# compiler's fixed fields and supplies' entries) with the pair's IR, hash and optimization, and every kernel32 import in its dllimport
# form as the external declarations. Every pair is written, and an earlier run's record removed, before the first round. Up to
# -Parallel lanes, each fixture's lines in one lane, send their build --Manifest lines, one at a time, to a kimi tatara session of
# their own: one process doing the lines' LLVM and LLD work through Tatara, as kimi build does (NativeBuild: optimize, emit, inspect,
# link). A line whose pair has no record after its session was not attempted (Tatara's refusal ends a session after its line) and goes
# to the next round of lanes. Runs: test-scalar-fixture.ps1, -Parallel at a time, reads each pair's record and runs its executable.
# Writes results.json, native-results.json and each session's log to -LogDirectory.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string] $FixturePattern = '*.ll',
    [string] $FixtureDirectory = '',
    [string] $OutputDirectory = '',
    [string] $LogDirectory = '',
    [ValidateRange(1, 2147483647)] [int] $ExecutionDeadlineMilliseconds = 30000,
    [ValidateRange(1, 2147483647)] [int] $Parallel = [Math]::Min(4, [Environment]::ProcessorCount),
    # A previous run's native-results.json: its passed entries with this run's keys, verified within -ReuseHours, are not re-executed.
    [string] $ReuseResults = '',
    [ValidateRange(1, 2147483647)] [int] $ReuseHours = 24
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$fixtures = if ($FixtureDirectory) { (Resolve-Path -LiteralPath $FixtureDirectory).Path } else { Join-Path $repo 'temp/scalar-fixtures' }
$selectedFixtures = @(Get-ChildItem -LiteralPath $fixtures -Filter $FixturePattern -File | Where-Object Extension -EQ '.ll' | Sort-Object Name)
if ($selectedFixtures.Count -eq 0) { throw "No scalar fixtures match '$FixturePattern' in '$fixtures'." }
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).ProviderPath) } else { Join-Path $repo 'temp/scalar-native' }
New-Item -ItemType Directory -Force $out, (Join-Path $out 'O0') | Out-Null
Write-Output 'Toolchain verification: not-performed'
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $out 'logs' }
New-Item -ItemType Directory -Force $logs | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
# A native outcome depends only on the fixture's files (fixture key) and on the compiler's Tatara, the supplies, the native build's code
# (src/Kimi/Build, the tatara and build commands and the target profile), the runner code and the deadline (environment key); missing
# files hash as empty.
function Get-NativeKey([string[]] $Parts) {
    [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Parts -join "`n")))
}
function Get-NativeFileHash([string] $Path) {
    if (-not [IO.File]::Exists($Path)) { return '' }
    $stream = [IO.File]::OpenRead($Path)
    try { [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
}
$environment = @((Join-Path (Split-Path -Parent $compiler) 'tatara.dll'), (Get-KimiBackendArchive), (Get-KimiKernel32)) +
    @(Get-ChildItem -LiteralPath (Join-Path $repo 'src/Kimi/Build') -Filter '*.cs' -File -Recurse | ForEach-Object FullName | Sort-Object) +
    @(foreach ($name in @('TataraCommand.cs', 'BuildCommand.cs')) { Join-Path $repo "src/Kimi/Unit/Command/$name" }) +
    @(Get-ChildItem -LiteralPath (Join-Path $repo 'src/Kimi/Compiler/Core') -Filter '*Profile.cs' -File | Sort-Object Name | ForEach-Object FullName) +
    @(foreach ($name in @('test-scalars.ps1', 'test-scalar-fixture.ps1', 'test-scalar-process.ps1', 'expected-output.ps1', 'toolchain.ps1', 'kernel32.ps1', 'kernel32.def', 'profile.json')) {
        Join-Path $PSScriptRoot $name
    })
$environmentKey = Get-NativeKey (@($environment | ForEach-Object { Get-NativeFileHash $_ }) + "$ExecutionDeadlineMilliseconds")
$now = [DateTimeOffset]::UtcNow
$stored = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
if ($ReuseResults) {
    $previous = Get-Content -LiteralPath $ReuseResults -Raw | ConvertFrom-Json -DateKind String
    if ($previous.environmentKey -ceq $environmentKey) {
        foreach ($entry in $previous.fixtures) {
            $age = $now - [DateTimeOffset]::Parse($entry.verifiedAt, [Globalization.CultureInfo]::InvariantCulture)
            if ($entry.status -ceq 'passed' -and $age -ge [TimeSpan]::Zero -and $age -le [TimeSpan]::FromHours($ReuseHours)) { $stored[$entry.fixture] = $entry }
        }
    }
}
$keys = @{}
$irHashes = @{}
$reused = [Collections.Generic.List[object]]::new()
$pending = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($fixture in $selectedFixtures) {
    $stem = Join-Path $fixtures $fixture.BaseName
    $irHashes[$fixture.Name] = Get-NativeFileHash $fixture.FullName
    $keys[$fixture.Name] = Get-NativeKey (@($irHashes[$fixture.Name]) + @(foreach ($extension in @('stdout', 'stderr', 'exit', 'observation')) { Get-NativeFileHash "$stem.$extension" }))
    $entry = $null
    if ($stored.TryGetValue($fixture.Name, [ref] $entry) -and $entry.key -ceq $keys[$fixture.Name]) {
        $reused.Add([pscustomobject][ordered]@{
            fixture = $fixture.Name; status = 'passed'; seconds = 0; log = $entry.log; levels = @('O0', 'O2'); executions = @()
            builds = @(); problems = @(); reused = $true; verifiedAt = $entry.verifiedAt
        })
    }
    else { $pending.Add($fixture) }
}
$builds = @{}
if ($pending.Count -gt 0) {
    # The manifest: what kimi emit writes for a one-line program, with the pair's fields left for each fixture.
    $template = Join-Path $out 'manifest-template'
    if (Test-Path -LiteralPath $template) { Remove-Item -LiteralPath $template -Recurse -Force }
    New-Item -ItemType Directory $template | Out-Null
    [IO.File]::WriteAllText((Join-Path $template 'Template.kimi'), "Console.writeLine(`"template`")`n", $utf8)
    $emitted = & dotnet $compiler emit (Join-Path $template 'Template.kimi') 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "The compiler did not emit the manifest template ($LASTEXITCODE): $emitted" }
    $manifest = Get-ChildItem -LiteralPath $template -Filter '*.link.json' -File -Recurse | Select-Object -First 1
    $head = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json -DateKind String
    $head.irFile = '@IR@'; $head.irSha256 = '@SHA256@'; $head.optimization = '@LEVEL@'
    $head.PSObject.Properties.Remove('input') # The template's source, no pair's input.
    $head.externalDeclarations = @((Get-KimiKernel32Definition).Split("`n") | Select-Object -Skip 2 | ForEach-Object { $_.Trim() } | Where-Object { $_ } |
        ForEach-Object { [ordered]@{ symbol = $_; dllimport = $true } })
    $head = $head | ConvertTo-Json -Depth 10
    # Each pending fixture's two pairs, written before the first round (a record is this run's build): its O2 pair in the output
    # directory, its O0 pair in O0/, the IR copied, the manifest written and an earlier run's record removed. A write failure fails the step.
    $queue = [Collections.Generic.List[object]]::new() # Per fixture, its builds with no record.
    foreach ($fixture in $pending) {
        $name = [Text.Json.JsonEncodedText]::Encode("$($fixture.BaseName).ll").ToString()
        $builds[$fixture.Name] = @(foreach ($level in @('O0', 'O2')) {
            $pair = if ($level -ceq 'O2') { Join-Path $out $fixture.BaseName } else { Join-Path $out "O0/$($fixture.BaseName)" }
            [IO.File]::Copy($fixture.FullName, "$pair.ll", $true)
            [IO.File]::Delete("$pair.link.build.json")
            [IO.File]::WriteAllText("$pair.link.json", $head.Replace('@IR@', $name).Replace('@SHA256@', $irHashes[$fixture.Name]).Replace('@LEVEL@', $level), $utf8)
            [pscustomobject]@{ level = $level; manifest = "$pair.link.json"; record = "$pair.link.build.json"; log = $null }
        })
        $queue.Add($builds[$fixture.Name])
    }
    # Rounds of up to -Parallel lanes, the fixtures dealt round robin (a fixture's lines to one lane): a lane sends its lines to its
    # session. A round that attempts no line ends the builds.
    for ($session = 0; $queue.Count -gt 0; $session += $lanes.Count) {
        $count = [Math]::Min($Parallel, $queue.Count)
        $lanes = @(for ($k = 0; $k -lt $count; $k++) {
            $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
            foreach ($argument in @($compiler, 'tatara')) { $start.ArgumentList.Add($argument) }
            $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            $start.StandardInputEncoding = $utf8
            $process = [Diagnostics.Process]::Start($start)
            $log = [IO.File]::Create((Join-Path $logs "session-$($session + $k).log"))
            [pscustomobject]@{
                process = $process; log = $log; output = $process.StandardOutput.BaseStream.CopyToAsync($log)
                errors = $process.StandardError.ReadToEndAsync()
                builds = @(for ($i = $k; $i -lt $queue.Count; $i += $count) { foreach ($build in $queue[$i]) { $build.log = $log.Name; $build } })
            }
        })
        $lanes | ForEach-Object -Parallel {
            $ErrorActionPreference = 'Stop'
            $lane = $_
            $utf8 = [Text.UTF8Encoding]::new($false)
            $stdin = $lane.process.StandardInput
            try {
                # A command line's path, with no backslash to escape.
                try { foreach ($build in $lane.builds) { $stdin.WriteLine("build --Manifest `"$($build.manifest.Replace('\', '/'))`""); $stdin.Flush() } }
                finally { $stdin.Close() }
            }
            catch [IO.IOException] { } # The session ended early (Tatara's refusal): its remaining lines have no record, for the next round.
            $lane.process.WaitForExit()
            $lane.output.Wait()
            $end = $utf8.GetBytes("$($lane.errors.Result)(test-scalars.ps1) kimi tatara exited with $($lane.process.ExitCode)`n")
            $lane.log.Write($end, 0, $end.Length)
            $lane.log.Dispose()
        } -ThrottleLimit $Parallel
        $left = [Collections.Generic.List[object]]::new()
        $attempted = $false
        foreach ($lines in $queue) {
            $rest = @(foreach ($build in $lines) { if (-not [IO.File]::Exists($build.record)) { $build } })
            $attempted = $attempted -or $rest.Count -lt $lines.Count
            if ($rest.Count -gt 0) { $left.Add($rest) }
        }
        if (-not $attempted) { break }
        $queue = $left
    }
}
$context = @{ fixtures = $fixtures; logs = $logs; builds = $builds; executionDeadlineMilliseconds = $ExecutionDeadlineMilliseconds }
$worker = Join-Path $PSScriptRoot 'test-scalar-fixture.ps1'
if ($Parallel -eq 1) {
    $results = @($pending | ForEach-Object { & $worker -Fixture $_ -Context $context })
}
else {
    $results = @($pending | ForEach-Object -Parallel {
        & $using:worker -Fixture $_ -Context $using:context
    } -ThrottleLimit $Parallel)
}
$executed = $results.Count
$results = @(@($results) + @($reused) | Sort-Object fixture)
$results | ConvertTo-Json -Depth 6 -AsArray | Set-Content -LiteralPath (Join-Path $logs 'results.json')
[ordered]@{
    environmentKey = $environmentKey; written = [DateTimeOffset]::UtcNow.ToString('o')
    fixtures = @(foreach ($result in $results) {
        [ordered]@{
            fixture = $result.fixture; key = $keys[$result.fixture]; status = $result.status; log = $result.log
            verifiedAt = if ($result.reused) { $result.verifiedAt } else { $now.ToString('o') }
        }
    })
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logs 'native-results.json')
$runs = 0
$failures = @($results | Where-Object status -CNE 'passed')
$incomplete = @($results | Where-Object status -CEQ 'incomplete').Count
foreach ($result in $results) {
    $runs += $result.levels.Count
    Write-Output "$($result.fixture): $($result.status.ToUpperInvariant()); $($result.log)"
}
if ($results.Count -ne $selectedFixtures.Count -or $failures.Count -gt 0) { throw "Native fixture verification failed ($incomplete incomplete); see $logs" }
Write-Output "Passed $runs native scalar executions (O0/O2); $executed fixtures run, $($reused.Count) reused (verified within $ReuseHours h)"
