# Selector regression checks; no builds or native programs are needed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/verification-selection.ps1')
$cases = 0
function Equal($actual, $expected) {
    if (($actual -join '|') -cne ($expected -join '|')) { throw "Expected '$expected', got '$actual'." }
    $script:cases++
}
function Reject([scriptblock] $action, [string] $message) {
    try { & $action }
    catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        $script:cases++
        return
    }
    throw "Expected rejection containing '$message'."
}
Equal (ConvertTo-KimiTestPattern 'OneTest') 'XunitTest.OneTest'
Equal (ConvertTo-KimiTestPattern 'OneTest.Run' -Method) 'XunitTest.OneTest.Run'
Equal (ConvertTo-KimiTestPattern 'XunitTest.OneTest') 'XunitTest.OneTest'
Equal (ConvertTo-KimiTestPattern 'XunitTest.OneTest.Run' -Method) 'XunitTest.OneTest.Run'
Equal (ConvertTo-KimiTestPattern '*OneTest') '*OneTest'
Equal (ConvertTo-KimiTestPattern '*Run' -Method) '*Run'
Equal (Get-KimiTestAlternatives 'XunitTest.OneTest') @('XunitTest.OneTest', 'XunitTest.OneTest+AllocationTests')
Equal (Get-KimiTestAlternatives 'XunitTest.OneTest*') @('XunitTest.OneTest*')
Equal (Get-KimiTestAlternatives 'XunitTest.OneTest+AllocationTests') @('XunitTest.OneTest+AllocationTests')
Equal (Get-KimiTestAlternatives 'XunitTest.OneTest.Run' -Method) @('XunitTest.OneTest.Run', 'XunitTest.OneTest+AllocationTests.Run')
Equal (Get-KimiTestAlternatives 'XunitTest.OneTest+AllocationTests.Run' -Method) @('XunitTest.OneTest+AllocationTests.Run')
foreach ($pattern in @('', ' ', 'One Test', 'One*Test', '**', '-class', 'OneTest?')) {
    Reject { ConvertTo-KimiTestPattern $pattern } 'Invalid test selector'
}
Reject { ConvertTo-KimiTestPattern 'Run' -Method } 'needs its declaring class'
$methods = @('XunitTest.OneTest.Run', 'XunitTest.OneTest+AllocationTests.Storage', 'XunitTest.TwoTest.Run')
Assert-KimiTestSelections @('XunitTest.OneTest') @() $methods
Assert-KimiTestSelections @('XunitTest.OneTest*') @('XunitTest.OneTest.Storage') $methods
Assert-KimiTestSelections @('XunitTest.OnlyAllocation') @() @('XunitTest.OnlyAllocation+AllocationTests.Storage')
$cases += 3
# A good selector must not conceal an independent misspelling in the same invocation.
Reject { Assert-KimiTestSelections @('XunitTest.OneTest', 'XunitTest.TypoTest') @() $methods } 'TypoTest'
Reject { Assert-KimiTestSelections @() @('XunitTest.OneTest.Run', 'XunitTest.OneTest.Typo') $methods } 'OneTest.Typo'
# The common compiler checks join each nonempty group once, also when the caller already selected them.
$common = @('XunitTest.ArchitectureRulesTest')
Equal (Join-KimiCommonTestSelections @('XunitTest.OneTest')) (@('XunitTest.OneTest') + $common)
Equal (Join-KimiCommonTestSelections @('XunitTest.OneTest.Run') -Method) @('XunitTest.OneTest.Run', 'XunitTest.ArchitectureRulesTest.*')
Equal (Join-KimiCommonTestSelections @((ConvertTo-KimiTestPattern 'ArchitectureRulesTest'), 'XunitTest.OneTest')) @('XunitTest.ArchitectureRulesTest', 'XunitTest.OneTest')
Equal (Join-KimiCommonTestSelections @('XunitTest.ArchitectureRulesTest.*') -Method) @('XunitTest.ArchitectureRulesTest.*')
Equal (Join-KimiCommonTestSelections @()) @()
Equal (Join-KimiCommonTestSelections @() -Method) @()
$methods += @('XunitTest.ArchitectureRulesTest.Rules')
Assert-KimiTestSelections @() (Join-KimiCommonTestSelections @('XunitTest.TwoTest.Run') -Method) $methods
$cases++
Reject { Assert-KimiTestSelections (Join-KimiCommonTestSelections @('XunitTest.TypoTest')) @() $methods } 'TypoTest'
Reject { Assert-KimiTestSelections @() (Join-KimiCommonTestSelections @('XunitTest.OneTest.Typo') -Method) $methods } 'OneTest.Typo'
# Tests reported as Not Run (Explicit tests not requested) were recorded but not executed; a run without a result file
# recorded none.
$result = [IO.Path]::GetTempFileName()
try {
    Set-Content -LiteralPath $result -Value ('<assemblies><assembly><collection>' +
        '<test type="XunitTest.OneTest" method="Run" result="Pass" />' +
        '<test type="XunitTest.OneTest+AllocationTests" method="Storage" result="Fail" />' +
        '<test type="XunitTest.TwoTest" method="Explicit" result="NotRun" /></collection></assembly></assemblies>')
    $executed = @(Get-KimiExecutedTests $result)
    $recorded = @(Get-KimiRecordedCases $result | ForEach-Object { "$($_.type).$($_.method)" })
}
finally { Remove-Item -LiteralPath $result }
Equal $executed @('XunitTest.OneTest.Run', 'XunitTest.OneTest+AllocationTests.Storage')
Equal $recorded @('XunitTest.OneTest.Run', 'XunitTest.OneTest+AllocationTests.Storage', 'XunitTest.TwoTest.Explicit')
Equal @(Get-KimiExecutedTests $result) @()
Equal @(Get-KimiRecordedCases $result) @()
# Each requested selector executes a test of its own: one that matches only a test left Not Run fails, also beside a
# selector that executes.
Assert-KimiTestSelections @('XunitTest.OneTest') @('XunitTest.OneTest.Storage') $executed 'among the executed tests'
$cases++
Reject { Assert-KimiTestSelections @() @('XunitTest.OneTest.Run', 'XunitTest.TwoTest.Explicit') $executed 'among the executed tests' } "'XunitTest.TwoTest.Explicit' among the executed tests"
Reject { Assert-KimiTestSelections @('XunitTest.TwoTest') @() $executed 'among the executed tests' } "'XunitTest.TwoTest' among the executed tests"
Write-Output "Passed $cases verification selector checks."
