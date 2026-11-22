[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Setter entered.`nResource 1 destroyed.`nBorrowed resource is 2.`nBorrowed resource is 2.`nHolder scope finished.`nResource 2 destroyed.`nConsuming function received.`nResource 3 destroyed.`nOwnership properties finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 36 -Configuration $Configuration -Expected $expected
