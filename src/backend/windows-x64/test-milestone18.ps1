[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Resource 4 destroyed.`nResource 3 destroyed.`nGeneric result secured.`nResources received.`nResource 2 destroyed.`nResource 1 destroyed.`nGeneric result secured.`nTuple received.`nGeneric result secured.`nGeneric values finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 18 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
