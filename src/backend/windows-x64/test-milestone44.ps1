[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Device 1 draws 1 W.`nLamp 2 draws 40 W.`nLamp 3 draws 45 W.`nFan 4 draws 2 W.`nStatic slots, dynamic implementations.`nDevice 4 released.`nDevice 3 released.`nDevice 2 released.`nDevice 1 released.`nShared handles dispatch on their payload.`nDevice 6 released.`nDevice 5 released.`nVirtual dispatch finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 44 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
