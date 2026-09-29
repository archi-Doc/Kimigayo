[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Accepted total is 12.`nPattern run finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 10 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
