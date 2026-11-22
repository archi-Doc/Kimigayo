[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "source retained`nreading destroyed`nvalue=Reading(42)`n君: value=Reading(42)`ndone=true`nMy number is 42`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 24 -Configuration $Configuration -Expected $expected
