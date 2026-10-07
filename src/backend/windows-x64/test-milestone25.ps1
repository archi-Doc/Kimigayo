[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Base field initialized.`nBase constructor finished.`nDerived field initialized.`nDerived constructor finished.`nInherited access finished.`nDerived value moved.`nDerived drop.`nDerived resource destroyed.`nBase drop.`nBase resource destroyed.`nInheritance finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 25 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
