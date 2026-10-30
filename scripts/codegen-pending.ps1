# The R5 pending ledger, its report and gate (R5 plan E10, E11, PD10; DECISIONS R5-M66, R5-M73; removed with the selector at
# G4), dot-sourced by verify.ps1 after verification-selection.ps1, whose Get-KimiExecutedCases decides which cases ran. The
# ledger is the union of tests/xUnitTest/TestData/codegen-pending/<group>.tsv, one file per review group, with the rows
#   test<TAB><type>.<method><TAB><full display name>    fixture<TAB><stem>.ll<TAB>1
# where a test row names one case: under an explicit -Codegen mir, verify.ps1 has xUnit print theory arguments in full, so each name is
# distinct.
# The report compares the failing set of a MIR code generation run with it and writes codegen-pending.json into the evidence;
# the run passes when no entry differs, and verify.ps1 -UpdatePending writes the ledger from the report.

# A ledger row: a test row's name is its type.method, optionally with an argument list.
$KimiCodegenRow = '^(test\t(\S+\.[^\s.]+)\t\2(\([^\t]*\))?|fixture\t\S+\.ll\t1)$'

# The ledger's groups by row id (kind, key and name, tab-separated). A malformed row, a file other than .tsv or an id listed
# twice throws; no directory is an empty ledger.
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
                if ($line -cnotmatch $KimiCodegenRow) {
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

# DECISIONS R5-M73's filing rule: a row goes to the review group of the first unit, in plan order, whose verification column
# (K13a-K13c: its Change column) names the class exactly, else of the first whose X* family prefixes its simple name, else
# C-frame. A nested class is filed by its outermost class and a fixture by its stem. Rows: unit, group, classes, families.
$KimiCodegenUnits = @(
    @('C0a', 'C-frame', 'PipelineSwitchTest', ''), @('C0b1', 'C-frame', 'MinimalEmissionTest', ''),
    @('C1a', 'C-scalar', '', 'Scalar Overflow Wrapping Bitwise Char'), @('C1b', 'C-scalar', '', 'Float'),
    @('C2a', 'C-aggregate', '', 'Element Borrowed Fixed'), @('C2b', 'C-aggregate', '', 'Aggregate Struct Composite'),
    @('C3a', 'call', '', 'Function Result Never Generic'),
    @('C3b', 'call', 'AbortEmissionTest LibraryAbortContextTest', 'Requirement Associated'),
    @('C3c', 'C-drop', 'PropertyWitnessEmissionTest', ''),
    @('C4a', 'C-drop', 'MirDropTest GenericDestructorEmissionTest', ''), @('C4c', 'C-drop', '', 'Owned Ownership'),
    @('C5a', 'closure', 'ContextualClosureTest', 'Closure Callable Concrete'), @('C5b', 'closure', '', 'Callback'),
    @('C6a', 'object', 'SharedObjectRuntimeTest SharedArcRuntimeTest GenericObjectFactoryTest PipelineObjectTest', 'Object Shared Dynamic'),
    @('C6b', 'object', 'SealedObjectFinalizationTest SharedObjectCleanupTest', ''),
    @('C6c', 'object', 'VirtualDispatchTest VirtualConditionalDispatchTest VirtualGenericDispatchTest VirtualEntryErasureTest', 'Virtual'),
    @('C7b', 'C7b', 'StaticInitializationTest', 'Static'), @('C8a', 'library-1', '', 'Raw Abort'),
    @('C8c', 'library-1', 'StringComparisonEmissionTest', 'String'), @('C8d', 'library-2', 'DynamicArrayCostTest', 'Array'),
    @('C8e', 'library-2', 'DictionaryLibraryTest DictionaryCostTest', 'Dictionary'),
    @('C8f', 'library-2', 'ComparisonContractTest ComparisonCompositionTest BuiltinComparisonTest ElementComparisonTest ComparisonOperatorTest', ''),
    @('C9a', 'format', '', 'Utf'), @('C9c', 'format', 'Utf8ConsoleTest', ''), @('C10a', 'test', 'TestProtocolTest', ''),
    @('K13a', 'K-edges', 'GenericGenerationDiagnosticTest', ''),
    @('K13b', 'K-edges', 'GenericStorageEmissionTest GenericDestructorEmissionTest InlineLayoutTest RecursiveDefaultTest GenericPointerEmissionTest', ''),
    @('K13c', 'object', 'VirtualGenericDispatchTest GenericObjectFactoryTest', '')
)

# The group of a row id or row by the filing rule.
function Get-KimiCodegenGroup([string] $Row) {
    $kind, $key, $null = $Row -split "`t", 3
    $name = if ($kind -ceq 'fixture') { $key } else { ($key.Substring(0, $key.LastIndexOf('.')) -split '\.')[-1].Split('+')[0] }
    foreach ($unit in $KimiCodegenUnits) { if ($unit[2] -and ($unit[2] -csplit ' ') -ccontains $name) { return $unit[1] } }
    foreach ($unit in $KimiCodegenUnits) {
        foreach ($family in @($unit[3] -csplit ' ' | Where-Object { $_ })) { if ($name.StartsWith($family, [StringComparison]::Ordinal)) { return $unit[1] } }
    }
    'C-frame'
}

# The run's executed and failing ids and the methods its results record: a case failed when it ran and did not pass, and a
# name executed twice throws (a row names one case); each fixture takes the native owner's status.
# Incomplete observations never enter the failing set.
function Get-KimiCodegenRun([string] $Evidence) {
    $executed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $failing = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $incomplete = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $results = @(Get-ChildItem -LiteralPath $Evidence -Filter 'tests-*.xml' -File)
    if ($results.Count -eq 0) { throw "$Evidence records no test results." }
    $recorded = [Collections.Generic.HashSet[string]]::new([string[]]@($results | ForEach-Object { Get-KimiRecordedCases $_.FullName } |
        ForEach-Object { "$($_.type).$($_.method)" }), [StringComparer]::Ordinal)
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
        switch -CaseSensitive ($record.status) {
            'passed' {}
            'failed' { $null = $failing.Add($id) }
            'incomplete' { $null = $incomplete.Add($id) }
            default { throw "Unknown native status '$($record.status)' for $($record.fixture)." }
        }
    }
    # Any incomplete execution prevents judging this fixture, even beside a completed run.
    $failing.ExceptWith($incomplete)
    [pscustomobject]@{ executed = $executed; failing = $failing; incomplete = $incomplete; recorded = $recorded; native = $native.Count -gt 0 }
}

# The entries of the ledger, the failing set and each state other than pending, as test cases and as fixtures.
function Get-KimiCodegenTotals($Entries) {
    $totals = [ordered]@{}
    foreach ($name in @('ledger', 'failing', 'passesNow', 'noLongerExecuted', 'outOfScope', 'added', 'incomplete')) {
        $totals[$name] = [ordered]@{ tests = 0; fixtures = 0 }
    }
    foreach ($entry in $Entries) {
        $kind = if ($entry.kind -ceq 'test') { 'tests' } else { 'fixtures' }
        if ($null -ne $entry.group) { $totals.ledger[$kind] += 1 }
        if ($entry.state -ceq 'pending' -or $entry.state -ceq 'added') { $totals.failing[$kind] += 1 }
        if ($entry.state -cne 'pending') { $totals[$entry.state][$kind] += 1 }
    }
    $totals
}

# Writes <Evidence>/codegen-pending.json and returns the summary.json field. A failing id is pending with a row, else added; a
# row that ran and passed passes now; a row that did not run is no longer executed when the run covers it (its selected
# methods, which a Session discovers as all, and any method its fresh assembly lacks; once its native steps wrote results,
# every fixture matching a -Fixtures pattern), else out of scope. A selected method the results record no case of (a partial
# run) throws. Incomplete overrides every other state for its fixture; differences includes it and blocks the gate.
function Write-KimiCodegenPending([string] $Evidence, [string] $Mode, [string] $Configuration, [string[]] $Fixtures, [string] $Ledger) {
    $ledgerRows = Read-KimiCodegenLedger $Ledger
    $run = Get-KimiCodegenRun $Evidence
    $selected, $discovered = foreach ($name in @('selected-methods.json', 'discovered-methods.json')) {
        $path = Join-Path $Evidence $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$Evidence has no $name." }
        , [Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json), [StringComparer]::Ordinal)
    }
    $unrecorded = @($selected | Where-Object { -not $run.recorded.Contains($_) } | Sort-Object)
    if ($unrecorded.Count -gt 0) { throw "The results record no case of $($unrecorded.Count) selected methods (a partial run): $($unrecorded -join ', ')" }
    $ids = [Collections.Generic.SortedSet[string]]::new([string[]]@($ledgerRows.rows.Keys), [StringComparer]::Ordinal)
    $ids.UnionWith($run.failing)
    $ids.UnionWith($run.incomplete)
    $entries = @(foreach ($id in $ids) {
        $kind, $key, $name = $id -split "`t", 3
        $group = $ledgerRows.rows[$id]
        $state = if ($run.incomplete.Contains($id)) { 'incomplete' }
        elseif ($run.failing.Contains($id)) { if ($null -ne $group) { 'pending' } else { 'added' } }
        elseif ($run.executed.Contains($id)) { 'passesNow' }
        elseif ($kind -ceq 'fixture') {
            if ($run.native -and @($Fixtures | Where-Object { $key -like $_ }).Count -gt 0) { 'noLongerExecuted' } else { 'outOfScope' }
        }
        elseif ($selected.Contains($key) -or -not $discovered.Contains($key)) { 'noLongerExecuted' }
        else { 'outOfScope' }
        [ordered]@{ kind = $kind; key = $key; name = $name; group = $group; state = $state }
    })
    $executed = [ordered]@{ tests = 0; fixtures = 0 }
    foreach ($id in $run.executed) { if ($id.StartsWith("fixture`t")) { $executed.fixtures++ } else { $executed.tests++ } }
    $groups = [ordered]@{}
    foreach ($group in $ledgerRows.groups) { $groups[$group] = Get-KimiCodegenTotals @($entries | Where-Object { $_.group -ceq $group }) }
    $totals = Get-KimiCodegenTotals $entries
    $differences = 0
    foreach ($state in @('passesNow', 'noLongerExecuted', 'added', 'incomplete')) { $differences += $totals[$state].tests + $totals[$state].fixtures }
    $report = Join-Path $Evidence 'codegen-pending.json'
    [ordered]@{
        run = [ordered]@{ directory = $Evidence; mode = $Mode; configuration = $Configuration; fixtures = @($Fixtures) }
        ledger = [ordered]@{ directory = $Ledger; groups = @($ledgerRows.groups) }
        executed = $executed; totals = $totals; groups = $groups; entries = $entries
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report
    [ordered]@{ report = $report; executed = $executed; totals = $totals; differences = $differences }
}

# Writes the ledger a report implies and returns, for each file it changed, the rows added and removed: pending and
# out-of-scope rows stay in their files, rows that pass now or are no longer executed leave, and an added row goes to its
# group by the filing rule. Each file keeps the report's ordinal order with LF line ends; a file whose rows did not change is
# not written, and one left without rows is removed.
function Update-KimiCodegenLedger([string] $Ledger, [string] $Report) {
    $entries = @((Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json).entries)
    if (@($entries | Where-Object state -CEQ 'incomplete').Count -gt 0) { throw 'The ledger is not updated: incomplete native observation.' }
    $files = [Collections.Generic.SortedDictionary[string, Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
    $changes = @{}
    foreach ($group in (Read-KimiCodegenLedger $Ledger).groups) { $files[$group] = [Collections.Generic.List[string]]::new(); $changes[$group] = @(0, 0) }
    foreach ($entry in $entries) {
        if ($entry.state -cne 'pending' -and $entry.state -cne 'outOfScope' -and $entry.state -cne 'added') { $changes[$entry.group][1]++; continue }
        $row = if ($entry.kind -ceq 'test') { "test`t$($entry.key)`t$($entry.name)" } else { "fixture`t$($entry.key)`t1" }
        if ($row -cnotmatch $KimiCodegenRow) { throw "$($row.Replace("`t", ' ')) cannot be a pending row." }
        $group = if ($entry.state -ceq 'added') { Get-KimiCodegenGroup $row } else { $entry.group }
        if (-not $files.ContainsKey($group)) { $files[$group] = [Collections.Generic.List[string]]::new(); $changes[$group] = @(0, 0) }
        if ($entry.state -ceq 'added') { $changes[$group][0]++ }
        $files[$group].Add($row)
    }
    $utf8 = [Text.UTF8Encoding]::new($false)
    foreach ($group in $files.Keys) {
        $path = Join-Path $Ledger "$group.tsv"
        $text = -join @($files[$group] | ForEach-Object { "$_`n" })
        $counts = "$group.tsv +$($changes[$group][0]) -$($changes[$group][1])"
        if ($files[$group].Count -eq 0) { Remove-Item -LiteralPath $path; "$counts, removed" }
        elseif (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path, $utf8) -cne $text) {
            $null = [IO.Directory]::CreateDirectory($Ledger)
            [IO.File]::WriteAllText($path, $text, $utf8)
            "$counts, $($files[$group].Count) rows"
        }
    }
}
