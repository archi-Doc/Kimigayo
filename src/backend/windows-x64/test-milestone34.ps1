[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Handle reads payload 1.`nCloned handle shares payload 1.`nReleased one strong handle.`nPayload 1 destroyed.`nArc views read payload 2.`nPayload 2 destroyed.`nBorrowed counter is 5.`nShared ownership finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 34 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
