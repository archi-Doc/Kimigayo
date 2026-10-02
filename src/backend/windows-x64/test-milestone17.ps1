[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Exchange scope finished.`nResource 2 destroyed.`nResource 3 destroyed.`nPrepared result.`nResource 1 destroyed.`nResult received.`nResource 5 destroyed.`nResource 4 destroyed.`nCleanup finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 17 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
