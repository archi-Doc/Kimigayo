[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Shared operators and function values: 4, 6.`nOne located element replaced: 4, 6.`nExplicit Result propagation: 42, error 7.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 37 -Configuration $Configuration -Expected $expected
