[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Counter created.`nSum is 55.`nLeaving finish.`nCounter destroyed.`nDone.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 4 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
