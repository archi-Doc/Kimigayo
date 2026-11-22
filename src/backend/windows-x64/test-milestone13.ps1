[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Even sample total is 6; first is still 1.`nIterator remains exhausted.`nMiddle slice total is 5.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 13 -Configuration $Configuration -Expected $expected
