[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$one = "Iteration finished.`nItem destroyed.`nIteration finished.`nIteration finished.`nFlow checked.`nItem destroyed.`nItem destroyed.`n"
$expected = $one + $one + "Ownership joins finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 15 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
