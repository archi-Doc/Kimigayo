# Compares the native fixture evidence of two Verify runs (VERIFICATION, behavior-preserving commits).
# Inputs: two successful evidence directories written by verify.ps1, each with summary.json, fixtures-<Configuration>
# and the native steps' native*-fixtures/results.json. Output: fixture-comparison.json and fixture-comparison.txt in
# -Output (default: the candidate run). It compares the set and bytes of every file in fixtures-<Configuration> (the
# .ll files, their expectation sidecars and any other fixture output) and the per-fixture native outcomes (result and
# completed levels; a passing level matched its sidecars). Fixture IR records no checkout path, so runs from any worktree
# compare byte for byte; nothing is normalized. Exit code 1 when anything differs; a failed run or missing evidence throws.
# -O2Metrics also writes o2-metrics.json (information only: the exit code stays the comparison's): instructions and
# blocks per define of each natively run fixture's optimized IR, the first native step's <work>/native*/<stem>.O2.ll
# (summary.json workDirectory), totalled over the runtime helpers (this checkout's runtime template) and over the
# generated bodies; fixtures with one in both runs ranked by candidate/baseline body instruction growth, then by the
# difference, then by name; the others listed as unpaired. Generated bodies are not paired across runs: each run names
# them __kimi_f<n> by its own numbering.
#   ./scripts/compare-fixtures.ps1 -Baseline artifacts/verify/<parent run> -Candidate artifacts/verify/<run> [-O2Metrics]
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [string] $Output = '',
    [switch] $O2Metrics
)
$ErrorActionPreference = 'Stop'

function Read-Run([string] $path) {
    $directory = (Resolve-Path -LiteralPath $path).ProviderPath
    $summary = Get-Content -LiteralPath (Join-Path $directory 'summary.json') -Raw | ConvertFrom-Json
    $steps = @($summary.steps)
    if ($steps.Count -eq 0 -or @($steps | Where-Object result -cne 'PASS').Count -gt 0) { throw "$directory is not a successful Verify run." }
    $fixtures = Join-Path $directory "fixtures-$($summary.configuration)"
    if (-not (Test-Path -LiteralPath $fixtures -PathType Container)) { throw "$directory has no fixtures-$($summary.configuration) evidence." }
    $work = $summary.workDirectory
    if ($O2Metrics -and -not ($work -and (Test-Path -LiteralPath $work -PathType Container))) { throw "$directory has no native work directory '$work'." }
    $outcomes = @{}
    $logs = @{}
    $o2 = @{}
    foreach ($step in @(Get-ChildItem -LiteralPath $directory -Directory -Filter 'native*-fixtures' | Sort-Object Name)) {
        $results = Join-Path $step.FullName 'results.json'
        if (-not (Test-Path -LiteralPath $results -PathType Leaf)) { throw "$($step.FullName) has no results.json." }
        foreach ($record in @(Get-Content -LiteralPath $results -Raw | ConvertFrom-Json)) {
            if (-not $outcomes.ContainsKey($record.fixture)) { $outcomes[$record.fixture] = @(); $logs[$record.fixture] = @() }
            # Archived evidence used ok; current runs publish status. Compatibility is confined to this historical reader.
            $status = if ($record.PSObject.Properties['status']) { $record.status }
            elseif ($record.PSObject.Properties['ok'] -and $record.ok -is [bool]) { if ($record.ok) { 'passed' } else { 'failed' } }
            else { throw "$results has no native status for $($record.fixture)." }
            $label = switch -CaseSensitive ($status) {
                'passed' { 'PASS' }
                'failed' { 'FAIL' }
                'incomplete' { throw "$results has incomplete native observation for $($record.fixture)." }
                default { throw "$results has unknown native status '$status'." }
            }
            $outcomes[$record.fixture] += (@($label) + $record.levels) -join ' '
            $logs[$record.fixture] += "$($step.Name)/$([IO.Path]::GetFileName($record.log))"
            # verify.ps1 writes native<suffix>-fixtures into the evidence and that step's outputs into <work>/native<suffix>.
            $file = if ($O2Metrics) { [IO.Path]::Combine($work, $step.Name -creplace '-fixtures$', [IO.Path]::ChangeExtension($record.fixture, '.O2.ll')) }
            if ($file -and -not $o2.ContainsKey($record.fixture) -and [IO.File]::Exists($file)) { $o2[$record.fixture] = $file }
        }
    }
    # A fixture selected by several native steps has the set of its outcomes.
    foreach ($fixture in @($outcomes.Keys)) { $outcomes[$fixture] = @($outcomes[$fixture] | Sort-Object -Unique -CaseSensitive) -join '; ' }
    $files = @(Get-ChildItem -LiteralPath $fixtures -File -Name | Sort-Object)
    [pscustomobject]@{
        directory = $directory; head = $summary.head; configuration = $summary.configuration; pipeline = $summary.pipeline
        fixtures = $fixtures; names = $files; outcomes = $outcomes; logs = $logs; o2 = $o2
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
$nativeFixtures = @(@($baseRun.outcomes.Keys) + @($candidateRun.outcomes.Keys) | Sort-Object -Unique -CaseSensitive)
foreach ($fixture in $nativeFixtures) {
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
    files = [ordered]@{
        identical = $common.Count - $differing.Count; ir = $irCount; irIdentical = $irIdentical; differing = $differing; missing = $missing; added = $added
    }
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
if ($O2Metrics) {
    # The runtime helpers are the functions LlvmModuleText.WriteRuntime writes in both generators: the runtime template's, each
    # named on the line before its body's entry: label, by a define line or by the {{name}} marker ExpandRuntime replaces with
    # that definition. Every other define is a generated body.
    $template = [IO.File]::ReadAllLines((Join-Path $PSScriptRoot '../src/Kimi/Compiler/Emission/WindowsRuntime.ll.in'))
    $runtime = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i = 1; $i -lt $template.Count; $i++) {
        if ($template[$i] -ceq 'entry:') { $null = $runtime.Add(($template[$i - 1] -creplace '^\{\{(.+)\}\}$|^[^@]*@([^(]+)\(.*$', '$1$2')) }
    }
    $paths = @(@($baseRun.o2.Values) + @($candidateRun.o2.Values))
    $chunks = @(for ($i = 0; $i -lt $parallel; $i++) { , @(for ($j = $i; $j -lt $paths.Count; $j += $parallel) { $paths[$j] }) })
    $metrics = @{}
    $chunks | ForEach-Object -ThrottleLimit $parallel -Parallel {
        $runtime = $using:runtime
        foreach ($path in $_) {
            $defines = [Collections.Generic.List[object]]::new()
            $total = [ordered]@{
                instructions = 0; blocks = 0; runtime = [ordered]@{ instructions = 0; blocks = 0 }; bodies = [ordered]@{ instructions = 0; blocks = 0 }
                defines = $defines
            }
            $define = $null
            # Between a define and its closing brace, LLVM's printer indents an instruction by exactly two spaces (a switch's
            # cases further; its closing ']' is no instruction) and writes a block label at column 0; an unlabeled entry
            # block is the one before any label.
            foreach ($line in [IO.File]::ReadLines($path)) {
                if ($null -eq $define) {
                    if ($line.StartsWith('define ', 'Ordinal')) { $define = [ordered]@{ name = $line.Substring($line.IndexOf('@') + 1).Split('(')[0]; instructions = 0; blocks = 0 } }
                }
                elseif ($line -ceq '}') {
                    $part = if ($runtime.Contains($define.name)) { $total.runtime } else { $total.bodies }
                    foreach ($count in @('instructions', 'blocks')) { $total[$count] += $define[$count]; $part[$count] += $define[$count] }
                    $defines.Add($define)
                    $define = $null
                }
                elseif ($line.StartsWith('  ', 'Ordinal')) { if ($line.Length -gt 2 -and ' ]'.IndexOf($line[2]) -lt 0) { $define.instructions++; $define.blocks = [Math]::Max(1, $define.blocks) } }
                elseif ($line.Length -gt 0 -and ' ;'.IndexOf($line[0]) -lt 0) { $define.blocks++ }
            }
            if ($total.bodies.instructions -eq 0) { throw "$path has no generated function." }
            [pscustomobject]@{ path = $path; metrics = $total }
        }
    } | ForEach-Object { $metrics[$_.path] = $_.metrics }
    foreach ($run in @($baseRun, $candidateRun)) { foreach ($fixture in @($run.o2.Keys)) { $run.o2[$fixture] = $metrics[$run.o2[$fixture]] } }
    $ranked = [Collections.Generic.List[object]]::new()
    $unpaired = [Collections.Generic.List[object]]::new()
    $sides = foreach ($side in 0, 1) {
        [ordered]@{ instructions = 0; blocks = 0; runtime = [ordered]@{ instructions = 0; blocks = 0 }; bodies = [ordered]@{ instructions = 0; blocks = 0 } }
    }
    function Add-Counts($sum, $counts) {
        foreach ($count in @('instructions', 'blocks')) { $sum[$count] += $counts[$count]; $sum.runtime[$count] += $counts.runtime[$count]; $sum.bodies[$count] += $counts.bodies[$count] }
    }
    # Fixtures rank by the growth of their generated bodies: the runtime helpers are the same text in both generators.
    foreach ($fixture in $nativeFixtures) {
        $before = $baseRun.o2[$fixture]
        $after = $candidateRun.o2[$fixture]
        if ($null -eq $before -or $null -eq $after) { $unpaired.Add([ordered]@{ fixture = $fixture; baseline = $before; candidate = $after }); continue }
        $growth = [Math]::Round($after.bodies.instructions / [double]$before.bodies.instructions, 4)
        $ranked.Add([ordered]@{ fixture = $fixture; growth = $growth; difference = $after.bodies.instructions - $before.bodies.instructions; baseline = $before; candidate = $after })
        Add-Counts $sides[0] $before
        Add-Counts $sides[1] $after
    }
    $ranked = @($ranked | Sort-Object @{ Expression = { $_.growth }; Descending = $true }, @{ Expression = { $_.difference }; Descending = $true }, @{ Expression = { $_.fixture } } -CaseSensitive)
    $growth = if ($ranked.Count -gt 0) { [Math]::Round($sides[1].bodies.instructions / [double]$sides[0].bodies.instructions, 4) } else { $null }
    $o2Report = [ordered]@{
        baseline = $runs[0]; candidate = $runs[1]; runtime = @($runtime | Sort-Object -CaseSensitive)
        totals = [ordered]@{ fixtures = $ranked.Count; baseline = $sides[0]; candidate = $sides[1]; growth = $growth }; ranked = $ranked; unpaired = @($unpaired)
    }
    $o2Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'o2-metrics.json')
    $o2Line = "O2: $($ranked.Count) paired, body instructions $($sides[0].bodies.instructions) -> $($sides[1].bodies.instructions) (growth $growth), " +
        "runtime $($sides[0].runtime.instructions) -> $($sides[1].runtime.instructions), all $($sides[0].instructions) -> $($sides[1].instructions), $($unpaired.Count) unpaired"
    $text.Add($o2Line)
    if ($ranked.Count -gt 0) { $text.Add('Largest O2 body instruction growth (baseline -> candidate):') }
    foreach ($item in @($ranked | Select-Object -First 10)) { $text.Add("  $($item.fixture): $($item.baseline.bodies.instructions) -> $($item.candidate.bodies.instructions) ($($item.growth))") }
    if ($unpaired.Count -gt 0) { $text.Add('Unpaired O2 fixtures:') }
    foreach ($item in $unpaired) { $text.Add("  $($item.fixture): none in $(@(foreach ($side in @('baseline', 'candidate')) { if ($null -eq $item[$side]) { $side } }) -join ' and ')") }
}
$text | Set-Content -LiteralPath (Join-Path $Output 'fixture-comparison.txt')
$text | Select-Object -First 6 | Write-Output
if ($O2Metrics) { Write-Output $o2Line }
Write-Output "Report: $(Join-Path $Output 'fixture-comparison.txt')"
exit [int](-not $identical)
