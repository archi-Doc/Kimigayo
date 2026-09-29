[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Borrowed sum is 55.`nView destroyed; counter is still 55.`nFinal value is 56.`nCounter destroyed.`nDone.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 5 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
