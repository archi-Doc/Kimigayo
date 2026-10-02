[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Sum is 55.`nDone.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 2 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
