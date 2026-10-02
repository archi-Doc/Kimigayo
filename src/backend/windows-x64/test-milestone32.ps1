[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "source retained`nreading destroyed`nvalue=Reading(42)`n君: value=Reading(42)`ndone=true`nMy number is 42`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 32 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
