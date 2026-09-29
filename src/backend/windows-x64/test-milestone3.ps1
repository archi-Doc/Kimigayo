[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Leaving sumTo.`nSum is 55.`nLeaving main.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 3 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
