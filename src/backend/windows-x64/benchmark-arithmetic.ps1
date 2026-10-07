# Matched native arithmetic measurements; timing is opt-in and never a normal Verify assertion.
[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateRange(1, 99)] [int] $Samples = 7,
    [string] $Name = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$root = Join-Path $repo "artifacts/benchmarks/arithmetic-native/$stamp$(if ($Name) { "-$Name" })"
$sources = Join-Path $root 'sources'
New-Item -ItemType Directory -Path $sources -Force | Out-Null
& dotnet (Join-Path $repo "src/Benchmark/bin/$Configuration/net10.0/Benchmark.dll") --arithmetic-native-sources $sources
if ($LASTEXITCODE -ne 0) { throw 'Arithmetic source generation failed.' }
$results = [Collections.Generic.List[object]]::new()
foreach ($source in Get-ChildItem -LiteralPath $sources -Filter '*.kimi' -File | Sort-Object Name) {
    $work = Join-Path $root "work/$($source.BaseName)"
    & (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 0 -Source $source.FullName -WorkRoot $work -Runs ($Samples + 1) -Expected "ok`n" -Configuration $Configuration -ToolchainRoot $ToolchainRoot | Out-Null
    $record = Get-ChildItem -LiteralPath $work -Recurse -Filter verification.json -File | Select-Object -First 1
    $report = Get-Content -LiteralPath $record.FullName -Raw | ConvertFrom-Json
    if ($report.status -cne 'passed') { throw "Native workload failed: $($source.Name)" }
    foreach ($test in $report.tests) {
        $level = $test.optimization
        $directory = Join-Path $record.DirectoryName $level
        $binary = Get-ChildItem -LiteralPath $directory -Recurse -Filter "$($source.BaseName).$level.exe" -File | Select-Object -First 1
        $ir = Join-Path $binary.DirectoryName $(if ($level -ceq 'O0') { "$($source.BaseName).ll" } else { "$($source.BaseName).$level.ll" })
        $stream = [IO.File]::OpenRead($binary.FullName)
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try { $text = $pe.PEHeaders.SectionHeaders | Where-Object Name -CEQ '.text' }
        finally { $pe.Dispose(); $stream.Dispose() }
        $times = [double[]]$test.repeatedRunSeconds
        if ($times.Count -ne $Samples -or $null -eq $text) { throw 'Incomplete timing or native code-size evidence.' }
        $sorted = $times | Sort-Object
        $middle = [int][Math]::Floor($Samples / 2)
        $median = if ($Samples % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
        $results.Add([ordered]@{
            name = $source.BaseName; optimization = $level; sourceSha256 = $report.sourceSha256; compilerSha256 = $report.compilerSha256
            checkedFirstRunSeconds = $test.runSeconds; measuredSeconds = $times; medianSeconds = $median
            irBytes = (Get-Item -LiteralPath $ir).Length; executableBytes = $binary.Length; textVirtualBytes = $text.VirtualSize; textFileBytes = $text.SizeOfRawData
            verification = [IO.Path]::GetRelativePath($repo, $record.FullName).Replace('\', '/')
        })
        Write-Output "$($source.BaseName) $level`: median $([Math]::Round($median, 6)) s, .text $($text.VirtualSize) bytes"
    }
}
[ordered]@{
    configuration = $Configuration; machine = [Environment]::MachineName; samplesPerLevel = $Samples
    conditions = '16,777,216 identical wrapping-add/shift/xor steps; first checked process excluded, then seven fresh processes by default; process startup included; no parallel workloads'
    results = $results
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
Write-Output "Summary: $root"
