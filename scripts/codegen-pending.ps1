# The R5 pending ledger report (R5 plan E10, PD10; DECISIONS R5-M66; removed with the selector at G4), dot-sourced by verify.ps1
# after verification-selection.ps1, whose Get-KimiExecutedCases decides which cases ran. The ledger is the union of
# tests/xUnitTest/TestData/codegen-pending/<group>.tsv, one file per review group, with the rows
#   test<TAB><type>.<method><TAB><full display name>    fixture<TAB><stem>.ll<TAB>1
# where a test row names one case: under sel, verify.ps1 has xUnit print theory arguments in full, so each name is distinct.
# The report compares the failing set of a MIR code generation run with it and writes codegen-pending.json into the evidence.

# The ledger's groups by row id (kind, key and name, tab-separated). A malformed row (a test row's name is its type.method,
# optionally with an argument list), a file other than .tsv or an id listed twice throws; no directory is an empty ledger.
function Read-KimiCodegenLedger([string] $Directory) {
    $rows = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $groups = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $Directory -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Directory | Sort-Object Name)) {
            if ($file.PSIsContainer -or $file.Extension -cne '.tsv') { throw "The pending ledger holds only <group>.tsv files: $($file.FullName)" }
            $groups.Add($file.BaseName)
            $number = 0
            foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
                $number++
                if ($line -cnotmatch '^(test\t(\S+\.[^\s.]+)\t\2(\([^\t]*\))?|fixture\t\S+\.ll\t1)$') {
                    throw "$($file.Name):$number is not a pending row (test<TAB>Type.Method<TAB>its case name or fixture<TAB>name.ll<TAB>1)."
                }
                $id = if ($line.StartsWith("fixture`t")) { $line.Substring(0, $line.Length - 2) } else { $line }
                if ($rows.ContainsKey($id)) { throw "$($file.Name):$number lists $($id.Replace("`t", ' ')) again." }
                $rows[$id] = $file.BaseName
            }
        }
    }
    [pscustomobject]@{ rows = $rows; groups = $groups }
}

# The run's executed and failing ids: a case failed when it ran and did not pass, and a name executed twice throws (a row names
# one case); a fixture failed when a native run of it did not pass at both O0 and O2 (test-scalars.ps1's rule).
function Get-KimiCodegenRun([string] $Evidence) {
    $executed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $failing = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $results = @(Get-ChildItem -LiteralPath $Evidence -Filter 'tests-*.xml' -File)
    if ($results.Count -eq 0) { throw "$Evidence records no test results." }
    foreach ($case in @($results | ForEach-Object { Get-KimiExecutedCases $_.FullName })) {
        $id = "test`t$($case.type).$($case.method)`t$($case.name)"
        if (-not $executed.Add($id)) { throw "The run executed $($case.name) twice; a pending row names one case." }
        if ($case.result -cne 'Pass') { $null = $failing.Add($id) }
    }
    $native = @(Get-ChildItem -LiteralPath $Evidence -Directory -Filter 'native*-fixtures' | ForEach-Object { Join-Path $_.FullName 'results.json' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    foreach ($record in @($native | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })) {
        # A fixture several native steps ran failed when one of its runs failed.
        $id = "fixture`t$($record.fixture)"
        $null = $executed.Add($id)
        if (-not $record.ok -or ($record.levels -join ',') -cne 'O0,O2') { $null = $failing.Add($id) }
    }
    [pscustomobject]@{ executed = $executed; failing = $failing; native = $native.Count -gt 0 }
}

# The entries of the ledger, the failing set and each state other than pending, as test cases and as fixtures.
function Get-KimiCodegenTotals($Entries) {
    $totals = [ordered]@{}
    foreach ($name in @('ledger', 'failing', 'passesNow', 'noLongerExecuted', 'outOfScope', 'added')) { $totals[$name] = [ordered]@{ tests = 0; fixtures = 0 } }
    foreach ($entry in $Entries) {
        $kind = if ($entry.kind -ceq 'test') { 'tests' } else { 'fixtures' }
        if ($entry.state -cne 'added') { $totals.ledger[$kind] += 1 }
        if ($entry.state -ceq 'pending' -or $entry.state -ceq 'added') { $totals.failing[$kind] += 1 }
        if ($entry.state -cne 'pending') { $totals[$entry.state][$kind] += 1 }
    }
    $totals
}

# Writes <Evidence>/codegen-pending.json and returns the summary.json field. A failing id is pending with a row, else added; a
# row that ran and passed passes now; a row that did not run is no longer executed when the run covers it (a Session every
# test, a Unit run its selected methods and any method its fresh assembly lacks, a run whose native steps wrote results every
# fixture matching a -Fixtures pattern), else out of scope.
function Write-KimiCodegenPending([string] $Evidence, [string] $Mode, [string] $Configuration, [string[]] $Fixtures, [string] $Ledger) {
    $ledgerRows = Read-KimiCodegenLedger $Ledger
    $run = Get-KimiCodegenRun $Evidence
    $selected = $discovered = $null
    if ($Mode -cne 'Session') {
        $selected, $discovered = foreach ($name in @('selected-methods.json', 'discovered-methods.json')) {
            $path = Join-Path $Evidence $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$Evidence has no $name." }
            , [Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json), [StringComparer]::Ordinal)
        }
    }
    $ids = [Collections.Generic.SortedSet[string]]::new([string[]]@($ledgerRows.rows.Keys), [StringComparer]::Ordinal)
    $ids.UnionWith($run.failing)
    $entries = @(foreach ($id in $ids) {
        $kind, $key, $name = $id -split "`t", 3
        $group = $ledgerRows.rows[$id]
        $state = if ($run.failing.Contains($id)) { if ($null -ne $group) { 'pending' } else { 'added' } }
        elseif ($run.executed.Contains($id)) { 'passesNow' }
        elseif ($kind -ceq 'fixture') {
            if ($run.native -and @($Fixtures | Where-Object { $key -like $_ }).Count -gt 0) { 'noLongerExecuted' } else { 'outOfScope' }
        }
        elseif ($Mode -ceq 'Session' -or $selected.Contains($key) -or -not $discovered.Contains($key)) { 'noLongerExecuted' }
        else { 'outOfScope' }
        [ordered]@{ kind = $kind; key = $key; name = $name; group = $group; state = $state }
    })
    $executed = [ordered]@{ tests = 0; fixtures = 0 }
    foreach ($id in $run.executed) { if ($id.StartsWith("fixture`t")) { $executed.fixtures++ } else { $executed.tests++ } }
    $groups = [ordered]@{}
    foreach ($group in $ledgerRows.groups) { $groups[$group] = Get-KimiCodegenTotals @($entries | Where-Object { $_.group -ceq $group }) }
    $totals = Get-KimiCodegenTotals $entries
    $report = Join-Path $Evidence 'codegen-pending.json'
    [ordered]@{
        run = [ordered]@{ directory = $Evidence; mode = $Mode; configuration = $Configuration; fixtures = @($Fixtures) }
        ledger = [ordered]@{ directory = $Ledger; groups = @($ledgerRows.groups) }
        executed = $executed; totals = $totals; groups = $groups; entries = $entries
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report
    [ordered]@{ report = $report; executed = $executed; totals = $totals }
}
