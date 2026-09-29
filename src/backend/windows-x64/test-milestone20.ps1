[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Default index evaluated.`nInferred selection is 10.`nExplicit selection is 30.`nDefault index evaluated.`nForwarded selection is 10.`nDefault index evaluated.`nOrdinary selection is 4.`nDefault index evaluated.`nLocal selection is 7.`nInference and defaults finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 20 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
