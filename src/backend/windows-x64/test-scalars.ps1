[CmdletBinding()]
param(
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
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
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$fixtures = if ($FixtureDirectory) { (Resolve-Path -LiteralPath $FixtureDirectory).Path } else { Join-Path $repo 'temp/scalar-fixtures' }
$selectedFixtures = @(Get-ChildItem -LiteralPath $fixtures -Filter $FixturePattern -File | Where-Object Extension -EQ '.ll' | Sort-Object Name)
if ($selectedFixtures.Count -eq 0) { throw "No scalar fixtures match '$FixturePattern' in '$fixtures'." }
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).ProviderPath) } else { Join-Path $repo 'temp/scalar-native' }
New-Item -ItemType Directory -Force $out | Out-Null
$tools = @{}
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-dlltool', 'llvm-readobj', 'llvm-nm')) { $tools[$name] = Join-Path $LlvmBin "$name.exe" }
$profile = Read-KimiWindowsProfile
$kernel = Get-KimiInstalledKernel32 $ToolchainRoot
$archive = Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Installed backend is missing. Run setup.ps1.' }
Write-Output 'Toolchain verification: not-performed'
$allowedSymbols = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in $profile.providedSymbols) { $null = $allowedSymbols.Add($symbol) }
foreach ($line in ((Get-KimiKernel32Definition) -split "`n" | Select-Object -Skip 2)) {
    $symbol = $line.Trim()
    if ($symbol) { $null = $allowedSymbols.Add($symbol); $null = $allowedSymbols.Add("__imp_$symbol") }
}
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $out 'logs' }
New-Item -ItemType Directory -Force $logs | Out-Null
$context = @{
    tools = $tools; fixtures = $fixtures; output = $out; kernel = $kernel; archive = $archive
    allowedSymbols = [string[]]@($allowedSymbols); logs = $logs; executionDeadlineMilliseconds = $ExecutionDeadlineMilliseconds
}
# A native outcome depends only on the fixture's files (fixture key) and on the tools, archive, imports, allowed symbols,
# runner code and deadline (environment key); missing files hash as empty.
function Get-NativeKey([string[]] $Parts) {
    [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Parts -join "`n")))
}
function Get-NativeFileHash([string] $Path) {
    if (-not [IO.File]::Exists($Path)) { return '' }
    $stream = [IO.File]::OpenRead($Path)
    try { [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
}
$environment = @(foreach ($name in @('opt', 'llc', 'llvm-nm', 'lld-link')) { $tools[$name] }) + @($archive, $kernel.path) +
    @(foreach ($name in @('test-scalars.ps1', 'test-scalar-fixture.ps1', 'test-scalar-process.ps1', 'toolchain.ps1', 'kernel32.ps1', 'profile.json')) {
        Join-Path $PSScriptRoot $name
    })
$environmentKey = Get-NativeKey (@($environment | ForEach-Object { Get-NativeFileHash $_ }) +
    @(([string[]]@($allowedSymbols) | Sort-Object -CaseSensitive) -join ',') + "$ExecutionDeadlineMilliseconds")
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
$reused = [Collections.Generic.List[object]]::new()
$pending = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($fixture in $selectedFixtures) {
    $stem = Join-Path $fixtures $fixture.BaseName
    $keys[$fixture.Name] = Get-NativeKey @(foreach ($extension in @('ll', 'stdout', 'stderr', 'exit', 'observation')) { Get-NativeFileHash "$stem.$extension" })
    $entry = $null
    if ($stored.TryGetValue($fixture.Name, [ref] $entry) -and $entry.key -ceq $keys[$fixture.Name]) {
        $reused.Add([pscustomobject][ordered]@{
            fixture = $fixture.Name; status = 'passed'; seconds = 0; log = $entry.log; levels = @('O0', 'O2'); executions = @()
            tools = @(); violations = @(); problems = @(); reused = $true; verifiedAt = $entry.verifiedAt
        })
    }
    else { $pending.Add($fixture) }
}
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
