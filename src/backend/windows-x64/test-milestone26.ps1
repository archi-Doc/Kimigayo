[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Packet destroyed.`nGeneric capture total is 20.`nCompound capture advanced to 8.`nConsumed packet.`nPacket destroyed.`nErased calls finished.`nPacket destroyed.`nGeneral closures finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 26 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
