[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Stateful result is 13.`nNested capture result is 18.`nCaptured message.`nCaptured message.`nClosure run finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 12 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
