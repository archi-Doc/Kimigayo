[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Owned element updated to 5.`nBorrowed elements total 30.`nExclusive referent updated to 33.`nTotals are 6, 30 and 73.`nSemantics-generic follow finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 39 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
