[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Selected 7.`nControl flow passed.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 6 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
