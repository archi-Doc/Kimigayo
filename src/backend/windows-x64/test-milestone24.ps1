[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Setter entered.`nResource 1 destroyed.`nBorrowed resource is 2.`nBorrowed resource is 2.`nHolder scope finished.`nResource 2 destroyed.`nOwned getter received.`nResource 3 destroyed.`nOwnership properties finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 24 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
