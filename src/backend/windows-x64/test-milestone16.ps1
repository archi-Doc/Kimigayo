[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$one = "External borrow survived.`nBoth sources updated.`n"
$expected = $one + $one + "Origin forwarding finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 16 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
