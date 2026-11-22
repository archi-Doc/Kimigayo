[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Hello, world!`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 1 -Configuration $Configuration -Expected $expected
