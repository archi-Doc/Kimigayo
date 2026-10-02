[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Generic weights are 6, 3, 2.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 11 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
