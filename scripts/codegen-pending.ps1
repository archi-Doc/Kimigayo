# The R5 pending ledger report (R5 plan E10, PD10; DECISIONS R5-M66; removed with the selector at G4), dot-sourced by verify.ps1
# after verification-selection.ps1, whose Get-KimiExecutedCases decides which cases ran. The ledger is the union of
# tests/xUnitTest/TestData/codegen-pending/<group>.tsv, one file per review group, with the rows
#   test<TAB><type>.<method><TAB><display name><TAB><failing cases>    fixture<TAB><stem>.ll<TAB>1
# where a count above 1 stands for cases whose display names collide (xUnit truncates long theory arguments with "···").
# The report compares the failing set of a MIR code generation run with it and writes codegen-pending.json into the evidence.

# The ledger rows by id (kind, key and name, tab-separated). A malformed row, a file other than .tsv or an id listed twice throws;
# no directory is an empty ledger.
function Read-KimiCodegenLedger([string] $Directory) {
    $rows = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $groups = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $Directory -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Directory | Sort-Object Name)) {
            if ($file.PSIsContainer -or $file.Extension -cne '.tsv') { throw "The pending ledger holds only <group>.tsv files: $($file.FullName)" }
            $groups.Add($file.BaseName)
            $number = 0
            foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
                $number++
                if ($line -cnotmatch '^(test\t\S+\.[^\s.]+\t[^\t]+\t[1-9]\d*|fixture\t\S+\.ll\t1)$') {
                    throw "$($file.Name):$number is not a pending row (test<TAB>Type.Method<TAB>display name<TAB>cases or fixture<TAB>name.ll<TAB>1)."
                }
                $fields = $line.Split("`t")
                $id = $fields[0..($fields.Count - 2)] -join "`t"
                if ($rows.ContainsKey($id)) { throw "$($file.Name):$number lists $($id.Replace("`t", ' ')) again." }
                $rows[$id] = [pscustomobject]@{ group = $file.BaseName; count = [int]$fields[-1] }
            }
        }
    }
    [pscustomobject]@{ rows = $rows; groups = $groups }
}

# The run's executed and failing counts by id: a case failed when it ran and did not pass, counted per type.method and display
# name; a fixture failed when a native run of it did not pass at both O0 and O2 (test-scalars.ps1's rule).
function Get-KimiCodegenRun([string] $Evidence) {
    $executed = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $failing = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $results = @(Get-ChildItem -LiteralPath $Evidence -Filter 'tests-*.xml' -File)
    if ($results.Count -eq 0) { throw "$Evidence records no test results." }
    foreach ($case in @($results | ForEach-Object { Get-KimiExecutedCases $_.FullName })) {
        $id = "test`t$($case.type).$($case.method)`t$($case.name)"
        $executed[$id] += 1
        if ($case.result -cne 'Pass') { $failing[$id] += 1 }
    }
    $native = @(Get-ChildItem -LiteralPath $Evidence -Directory -Filter 'native*-fixtures' | ForEach-Object { Join-Path $_.FullName 'results.json' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    foreach ($record in @($native | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })) {
        # A fixture several native steps ran failed when one of its runs failed.
        $id = "fixture`t$($record.fixture)"
        $executed[$id] = 1
        $failing[$id] = [int]($failing[$id] -gt 0 -or -not $record.ok -or ($record.levels -join ',') -cne 'O0,O2')
    }
    [pscustomobject]@{ executed = $executed; failing = $failing; native = $native.Count -gt 0 }
}

# Ledger, failing and the state deltas (|ledger - failing| of each entry not pending), as cases of tests and as fixtures.
function Get-KimiCodegenTotals($Entries) {
    $totals = [ordered]@{}
    foreach ($name in @('ledger', 'failing', 'passesNow', 'noLongerExecuted', 'outOfScope', 'added')) { $totals[$name] = [ordered]@{ tests = 0; fixtures = 0 } }
    foreach ($entry in $Entries) {
        $kind = if ($entry.kind -ceq 'test') { 'tests' } else { 'fixtures' }
        $totals.ledger[$kind] += $entry.ledger
        $totals.failing[$kind] += $entry.failing
        if ($entry.state -cne 'pending') { $totals[$entry.state][$kind] += [Math]::Abs($entry.ledger - $entry.failing) }
    }
    $totals
}

# Writes <Evidence>/codegen-pending.json and returns the summary.json field. A row whose cases fail fewer times passes now only as
# far as passing cases of its name account for it, else it is no longer executed, as is a row not executed that the run covers (a
# Session every test, a Unit run its selected methods and any method its fresh assembly lacks, the native steps the fixtures of
# their patterns); a row the run does not cover is out of scope.
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
    foreach ($id in $run.failing.Keys) { if ($run.failing[$id] -gt 0) { $null = $ids.Add($id) } }
    $entries = @(foreach ($id in $ids) {
        $kind, $key, $name = $id.Split("`t")
        $row = $ledgerRows.rows[$id]
        $count = if ($row) { $row.count } else { 0 }
        $failing = [int]$run.failing[$id]
        $state = if ($run.executed.ContainsKey($id)) {
            if ($failing -gt $count) { 'added' }
            elseif ($failing -eq $count) { 'pending' }
            elseif ($run.executed[$id] - $failing -ge $count - $failing) { 'passesNow' }
            else { 'noLongerExecuted' }
        }
        elseif ($kind -ceq 'fixture') {
            if ($run.native -and @($Fixtures | Where-Object { $key -like $_ }).Count -gt 0) { 'noLongerExecuted' } else { 'outOfScope' }
        }
        elseif ($Mode -ceq 'Session' -or $selected.Contains($key) -or -not $discovered.Contains($key)) { 'noLongerExecuted' }
        else { 'outOfScope' }
        [ordered]@{ kind = $kind; key = $key; name = $name; group = $row.group; ledger = $count; executed = [int]$run.executed[$id]; failing = $failing; state = $state }
    })
    $executed = [ordered]@{ tests = 0; fixtures = 0 }
    foreach ($id in $run.executed.Keys) { if ($id.StartsWith("fixture`t")) { $executed.fixtures++ } else { $executed.tests += $run.executed[$id] } }
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
