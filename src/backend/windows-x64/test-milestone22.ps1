[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Compound layouts preserved.`nSpecialization preserved.`nRed received.`nRed destroyed.`nBlue received.`nBlue destroyed.`nGeneric generation finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 22 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
