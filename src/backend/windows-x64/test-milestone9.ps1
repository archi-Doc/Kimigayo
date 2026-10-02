[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Found 6 at index 3.`nMissing value handled.`nSearch finished.`nBatch destroyed.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 9 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
