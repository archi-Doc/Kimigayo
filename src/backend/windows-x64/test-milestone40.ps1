[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Balances 20, 25, 25.`nClosed Chi.`nClosed Aki.`nEve holds 20.`nBen holds 25.`nDan holds 25.`nFay holds 5.`nSettled 10 30 20 40 after 3 exchanges.`nPositions ^1 and 3 name one element.`nLimits 0 and 19.`nDisjoint element access finished.`nClosed Fay.`nClosed Dan.`nClosed Ben.`nClosed Eve.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 40 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
