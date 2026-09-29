[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Standard access finished.`nInput evaluated.`nReceiver evaluated.`nCustom set.`nCustom get.`nComputed set.`nComputed get.`nCopy properties finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 23 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
