[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Row finished.`nRow finished.`nRow finished.`nMatrix total is 42.`nBorrowed row total is 20.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 7 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
