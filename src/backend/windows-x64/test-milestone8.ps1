[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Chosen item.`nBoxed array total is 12.`nGeneric scope finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 8 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
