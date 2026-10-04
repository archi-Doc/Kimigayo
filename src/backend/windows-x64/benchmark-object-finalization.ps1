# Opt-in uninstrumented native timings. Each source is built once at O0/O2, with a checked first run and fixed repeats.
[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateRange(1, 100)] [int] $Runs = 7,
    [ValidateSet('Rc', 'Arc')] [string[]] $Mode = @('Rc', 'Arc'),
    [string] $Name = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$root = Join-Path $repo "artifacts/benchmarks/object-finalization/$stamp$(if ($Name) { "-$Name" })"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$programs = [ordered]@{}
foreach ($modeName in $Mode) {
    foreach ($workload in @('Finalization', 'DictionaryFinalization')) {
        $program = $modeName + $workload
        $checksum = if ($workload -eq 'Finalization') { 14680064 } else { 3670016 }
        $workRoot = Join-Path $root $program
        $source = Join-Path $repo "src/Benchmark/Kimi/Rc$workload.kimi"
        if ($modeName -eq 'Arc') {
            $text = [IO.File]::ReadAllText($source).Replace('makeRc(', 'makeArc(').Replace('rc/', 'arc/')
            $source = Join-Path $root "$program.kimi"
            [IO.File]::WriteAllText($source, $text, [Text.UTF8Encoding]::new($false))
        }
        & (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 0 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected "checksum $checksum`n" -Source $source -WorkRoot $workRoot -Runs $Runs | Out-Null
        $reportPath = (Get-ChildItem -LiteralPath $workRoot -Recurse -Filter verification.json | Select-Object -First 1).FullName
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.status -cne 'passed') { throw "Harness did not pass: $reportPath" }
        $levels = [ordered]@{}
        foreach ($test in $report.tests) {
            $times = [double[]] (@($test.runSeconds) + @($test.repeatedRunSeconds))
            $sorted = @($times | Sort-Object)
            $middle = [int][Math]::Floor($sorted.Count / 2)
            $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
            $levels[$test.optimization] = [ordered]@{ runSeconds = $times; medianSeconds = $median }
        }
        $programs[$program] = [ordered]@{ sourceSha256 = $report.sourceSha256; compilerSha256 = $report.compilerSha256; report = [IO.Path]::GetRelativePath($repo, $reportPath).Replace('\', '/'); levels = $levels }
    }
}
$summary = [ordered]@{
    compilerConfiguration = $Configuration; machine = [Environment]::MachineName; started = $stamp
    runsPerLevel = $Runs; instrumentation = 'none'; includesProcessStartup = $true
    workloads = '1048576 factory/clone/final-release rounds; 262144 two-entry Dictionary owning-iteration rounds'
    programs = $programs
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
Write-Output "Summary: $root"
