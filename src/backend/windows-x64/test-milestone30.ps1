[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "User comparisons keep their operands.`nTuple comparisons compose witnesses.`nFloating Contract equality is NaN-reflexive.`nComparison contracts finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 30 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
