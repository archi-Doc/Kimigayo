# Times the hash and generator workloads of src/Benchmark/Kimi on wrapping integer Types (HashWrapping.kimi) and on
# checked, widened-and-masked Types (HashChecked.kimi) at O0 and O2 through the milestone harness. Timing only, outside
# normal Verify (docs/dev/VERIFICATION.md); the harness still checks each first run's output.
#   ./src/backend/windows-x64/benchmark-p42.ps1 [-Runs 5] [-Configuration Release] [-Name <label>]
[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateRange(1, 100)] [int] $Runs = 5,
    [string] $Name = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$root = Join-Path $repo "artifacts/benchmarks/p42-hash-prng/$stamp$(if ($Name) { "-$Name" })"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$expected = "hash 2828836293 draws 16360015469738469032`n"
$harness = Join-Path $PSScriptRoot 'milestone-harness.ps1'

function Get-Median([double[]] $values) {
    $sorted = $values | Sort-Object
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$programs = [ordered]@{}
foreach ($program in @('HashWrapping', 'HashChecked')) {
    $workRoot = Join-Path $root "work/$program"
    & $harness -Milestone 0 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected -Source (Join-Path $repo "src/Benchmark/Kimi/$program.kimi") -WorkRoot $workRoot -Runs $Runs | Out-Null
    $reportPath = (Get-ChildItem -LiteralPath $workRoot -Recurse -Filter verification.json | Select-Object -First 1).FullName
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.status -cne 'passed') { throw "Harness did not pass: $reportPath" }
    $levels = [ordered]@{}
    foreach ($test in $report.tests) {
        $times = [double[]] (@($test.runSeconds) + @($test.repeatedRunSeconds))
        $levels[$test.optimization] = [ordered]@{ buildSeconds = $test.buildSeconds; runSeconds = $times; medianSeconds = Get-Median $times; minSeconds = ($times | Measure-Object -Minimum).Minimum }
    }
    $programs[$program] = [ordered]@{ source = $report.source; sourceSha256 = $report.sourceSha256; report = [IO.Path]::GetRelativePath($repo, $reportPath).Replace('\', '/'); levels = $levels }
}

$ratios = [ordered]@{}
foreach ($level in @('O0', 'O2')) {
    $ratios[$level] = [Math]::Round($programs['HashChecked'].levels[$level].medianSeconds / $programs['HashWrapping'].levels[$level].medianSeconds, 3)
}
$summary = [ordered]@{
    workload = 'FNV-1a over 63 bytes x 1,048,576 rounds and 33,554,432 xorshift64* draws'; runsPerLevel = $Runs; compilerConfiguration = $Configuration
    compilerSha256 = $report.compilerSha256; machine = [Environment]::MachineName; started = $stamp; programs = $programs; checkedOverWrappingMedianRatio = $ratios
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("# P42 hash and generator timing ($stamp, $Configuration, $Runs runs per level)")
$lines.Add('')
$lines.Add('| Program | Level | Build s | Median run s | Min run s | Runs s |')
$lines.Add('| --- | --- | --- | --- | --- | --- |')
foreach ($program in $programs.Keys) {
    foreach ($level in @('O0', 'O2')) {
        $entry = $programs[$program].levels[$level]
        $lines.Add("| $program | $level | $([Math]::Round($entry.buildSeconds, 2)) | $([Math]::Round($entry.medianSeconds, 4)) | $([Math]::Round($entry.minSeconds, 4)) | $(($entry.runSeconds | ForEach-Object { [Math]::Round($_, 4) }) -join ' ') |")
    }
}
$lines.Add('')
$lines.Add("Checked / wrapping median run time: O0 $($ratios['O0']), O2 $($ratios['O2']).")
[IO.File]::WriteAllLines((Join-Path $root 'summary.md'), $lines)
$lines | Write-Output
Write-Output "Summary: $root"
