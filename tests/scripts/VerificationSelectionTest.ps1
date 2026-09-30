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
Write-Output "Passed $cases verification selector checks."
