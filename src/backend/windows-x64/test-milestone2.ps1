[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Sum is 55.`nDone.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 2 -Configuration $Configuration -Expected $expected
