# Compares the native fixture evidence of two Verify runs (VERIFICATION, behavior-preserving commits).
# Inputs: two successful evidence directories written by verify.ps1, each with summary.json, fixtures-<Configuration>
# and the native steps' native*-fixtures/results.json. Output: fixture-comparison.json and fixture-comparison.txt in
# -Output (default: the candidate run). It compares the set and bytes of every file in fixtures-<Configuration> (the
# .ll files, their expectation sidecars and any other fixture output) and the per-fixture native outcomes (result and
# completed levels; a passing level matched its sidecars). Fixture IR records no checkout path, so runs from any
# worktree compare byte for byte; nothing is normalized. Exit code 1 when anything differs; a failed run or missing
# evidence throws.
#   ./scripts/compare-fixtures.ps1 -Baseline artifacts/verify/<parent run> -Candidate artifacts/verify/<run>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [string] $Output = ''
)
$ErrorActionPreference = 'Stop'

function Read-Run([string] $path) {
    $directory = (Resolve-Path -LiteralPath $path).ProviderPath
    $summary = Get-Content -LiteralPath (Join-Path $directory 'summary.json') -Raw | ConvertFrom-Json
    $steps = @($summary.steps)
    if ($steps.Count -eq 0 -or @($steps | Where-Object result -cne 'PASS').Count -gt 0) { throw "$directory is not a successful Verify run." }
    $fixtures = Join-Path $directory "fixtures-$($summary.configuration)"
    if (-not (Test-Path -LiteralPath $fixtures -PathType Container)) { throw "$directory has no fixtures-$($summary.configuration) evidence." }
    $outcomes = @{}
    $logs = @{}
    foreach ($step in @(Get-ChildItem -LiteralPath $directory -Directory -Filter 'native*-fixtures' | Sort-Object Name)) {
        $results = Join-Path $step.FullName 'results.json'
        if (-not (Test-Path -LiteralPath $results -PathType Leaf)) { throw "$($step.FullName) has no results.json." }
        foreach ($record in @(Get-Content -LiteralPath $results -Raw | ConvertFrom-Json)) {
            if (-not $outcomes.ContainsKey($record.fixture)) { $outcomes[$record.fixture] = @(); $logs[$record.fixture] = @() }
            $outcomes[$record.fixture] += (@(if ($record.ok) { 'PASS' } else { 'FAIL' }) + $record.levels) -join ' '
            $logs[$record.fixture] += "$($step.Name)/$([IO.Path]::GetFileName($record.log))"
        }
    }
    # A fixture selected by several native steps has the set of its outcomes.
    foreach ($fixture in @($outcomes.Keys)) { $outcomes[$fixture] = @($outcomes[$fixture] | Sort-Object -Unique -CaseSensitive) -join '; ' }
    $files = @(Get-ChildItem -LiteralPath $fixtures -File -Name | Sort-Object)
    [pscustomobject]@{
        directory = $directory; head = $summary.head; configuration = $summary.configuration; pipeline = $summary.pipeline
        fixtures = $fixtures; names = $files; outcomes = $outcomes; logs = $logs
        files = [Collections.Generic.HashSet[string]]::new([string[]]$files, [StringComparer]::Ordinal)
    }
}

$baseRun = Read-Run $Baseline
$candidateRun = Read-Run $Candidate
$common = @($baseRun.names | Where-Object { $candidateRun.files.Contains($_) })
$missing = @($baseRun.names | Where-Object { -not $candidateRun.files.Contains($_) })
$added = @($candidateRun.names | Where-Object { -not $baseRun.files.Contains($_) })
# Opening about 30,000 small files dominates the time, so chunks of the common files compare in parallel.
$parallel = [Math]::Min(8, [Environment]::ProcessorCount)
$chunks = @(for ($i = 0; $i -lt $parallel; $i++) { , @(for ($j = $i; $j -lt $common.Count; $j += $parallel) { $common[$j] }) })
$differing = @($chunks | ForEach-Object -ThrottleLimit $parallel -Parallel {
    $base = $using:baseRun
    $candidate = $using:candidateRun
    foreach ($file in $_) {
        $before = [IO.File]::ReadAllBytes([IO.Path]::Combine($base.fixtures, $file))
        if (-not [Linq.Enumerable]::SequenceEqual($before, [IO.File]::ReadAllBytes([IO.Path]::Combine($candidate.fixtures, $file)))) { $file }
    }
} | Sort-Object)
$irCount = @($common -clike '*.ll').Count
$irIdentical = $irCount - @($differing -clike '*.ll').Count
$equalNative = 0
$differingNative = [Collections.Generic.List[object]]::new()
foreach ($fixture in @(@($baseRun.outcomes.Keys) + @($candidateRun.outcomes.Keys) | Sort-Object -Unique -CaseSensitive)) {
    $before = $baseRun.outcomes[$fixture]
    $after = $candidateRun.outcomes[$fixture]
    if ([string]::Equals($before, $after)) { $equalNative++; continue }
    $differingNative.Add([ordered]@{
        fixture = $fixture; baseline = $before; candidate = $after
        baselineLogs = @($baseRun.logs[$fixture] | Where-Object { $_ }); candidateLogs = @($candidateRun.logs[$fixture] | Where-Object { $_ })
    })
}
$identical = $missing.Count + $added.Count + $differing.Count + $differingNative.Count -eq 0
$runs = foreach ($run in @($baseRun, $candidateRun)) {
    [ordered]@{ directory = $run.directory; head = $run.head; configuration = $run.configuration; pipeline = $run.pipeline }
}
$report = [ordered]@{
    identical = $identical; baseline = $runs[0]; candidate = $runs[1]
    files = [ordered]@{ identical = $common.Count - $differing.Count; ir = $irCount; irIdentical = $irIdentical; differing = $differing; missing = $missing; added = $added }
    native = [ordered]@{ equal = $equalNative; differing = @($differingNative) }
}
$Output = if ($Output) { [IO.Path]::GetFullPath($Output) } else { $candidateRun.directory }
New-Item -ItemType Directory -Force $Output | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'fixture-comparison.json')
$text = [Collections.Generic.List[string]]::new()
foreach ($run in @(@('Baseline: ', $baseRun), @('Candidate:', $candidateRun))) {
    $text.Add("$($run[0]) $($run[1].directory) ($($run[1].head), $($run[1].configuration), $($run[1].pipeline))")
}
$text.Add("Files: $($report.files.identical) identical, $($differing.Count) differing, $($missing.Count) missing, $($added.Count) added")
$text.Add("IR: $irIdentical of $irCount common .ll identical")
$text.Add("Native: $equalNative equal, $($differingNative.Count) differing")
$text.Add("Result: $(if ($identical) { 'IDENTICAL' } else { 'DIFFERENT' })")
foreach ($section in @(@('Differing', $differing), @('Missing', $missing), @('Added', $added))) {
    if ($section[1].Count -gt 0) { $text.Add("$($section[0]):"); foreach ($name in $section[1]) { $text.Add("  $name") } }
}
if ($differingNative.Count -gt 0) {
    $text.Add('Differing native outcomes (baseline -> candidate):')
    foreach ($item in $differingNative) { $text.Add("  $($item.fixture): $(if ($item.baseline) { $item.baseline } else { 'not run' }) -> $(if ($item.candidate) { $item.candidate } else { 'not run' })") }
}
$text | Set-Content -LiteralPath (Join-Path $Output 'fixture-comparison.txt')
$text | Select-Object -First 6 | Write-Output
Write-Output "Report: $(Join-Path $Output 'fixture-comparison.txt')"
exit [int](-not $identical)
