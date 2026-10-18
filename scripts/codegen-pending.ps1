# The R5 pending ledger report (R5 plan E10, PD10; DECISIONS R5-M66; removed with the selector at G4), dot-sourced by verify.ps1
# after verification-selection.ps1, whose Get-KimiExecutedTests decides which cases ran. The ledger is the union of
# tests/xUnitTest/TestData/codegen-pending/<group>.tsv, one file per review group, with the rows
#   test<TAB><type>.<method><TAB><failing cases>    fixture<TAB><stem>.ll<TAB>1
# The report compares the failing set of a MIR code generation run with it and writes codegen-pending.json into the evidence.

# The ledger rows by "<kind> <key>". A malformed row, a file other than .tsv or a key listed twice throws; no directory is an empty ledger.
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
                if ($line -cnotmatch '^(test\t\S+\.[^\s.]+\t[1-9]\d*|fixture\t\S+\.ll\t1)$') {
                    throw "$($file.Name):$number is not a pending row (test<TAB>Type.Method<TAB>cases or fixture<TAB>name.ll<TAB>1)."
                }
                $kind, $key, $count = $line.Split("`t")
                if ($rows.ContainsKey("$kind $key")) { throw "$($file.Name):$number lists $kind $key again." }
                $rows["$kind $key"] = [pscustomobject]@{ group = $file.BaseName; count = [int]$count }
            }
        }
    }
    [pscustomobject]@{ rows = $rows; groups = $groups }
}

# The run's executed and failing counts by "<kind> <key>": a case failed when it ran and did not pass, counted per type.method; a
# fixture failed when its native run did not pass at both O0 and O2 (test-scalars.ps1's rule).
function Get-KimiCodegenRun([string] $Evidence) {
    $executed = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $passed = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $results = @(Get-ChildItem -LiteralPath $Evidence -Filter 'tests-*.xml' -File)
    if ($results.Count -eq 0) { throw "$Evidence records no test results." }
    foreach ($xml in $results) {
        foreach ($test in @(Get-KimiExecutedTests $xml.FullName)) { $executed["test $test"] += 1 }
        foreach ($node in @(Select-Xml -LiteralPath $xml.FullName -XPath '//test[@result="Pass"]')) {
            $passed["test $($node.Node.type).$($node.Node.method)"] += 1
        }
    }
    $failing = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($id in $executed.Keys) { $failing[$id] = $executed[$id] - $passed[$id] }
    $native = @(Get-ChildItem -LiteralPath $Evidence -Directory -Filter 'native*-fixtures' | ForEach-Object { Join-Path $_.FullName 'results.json' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    foreach ($record in @($native | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json })) {
        # A fixture several native steps ran failed when one of its runs failed.
        $id = "fixture $($record.fixture)"
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

# Writes <Evidence>/codegen-pending.json and returns the summary.json field. A row not executed is no longer executed when the run
# covers it (a Session every test, a Unit run its selected methods and any method its fresh assembly lacks, the native steps the
# fixtures of their patterns), else out of scope.
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
        $kind, $key = $id.Split(' ', 2)
        $row = $ledgerRows.rows[$id]
        $count = if ($row) { $row.count } else { 0 }
        $failing = [int]$run.failing[$id]
        $state = if ($run.executed.ContainsKey($id)) {
            if ($failing -lt $count) { 'passesNow' } elseif ($failing -gt $count) { 'added' } else { 'pending' }
        }
        elseif ($kind -ceq 'fixture') {
            if ($run.native -and @($Fixtures | Where-Object { $key -like $_ }).Count -gt 0) { 'noLongerExecuted' } else { 'outOfScope' }
        }
        elseif ($Mode -ceq 'Session' -or $selected.Contains($key) -or -not $discovered.Contains($key)) { 'noLongerExecuted' }
        else { 'outOfScope' }
        [ordered]@{ kind = $kind; key = $key; group = $row.group; ledger = $count; executed = [int]$run.executed[$id]; failing = $failing; state = $state }
    })
    $executed = [ordered]@{ tests = 0; fixtures = 0 }
    foreach ($id in $run.executed.Keys) { if ($id.StartsWith('fixture ')) { $executed.fixtures++ } else { $executed.tests += $run.executed[$id] } }
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
