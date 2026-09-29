[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Hello, world!`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 1 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
