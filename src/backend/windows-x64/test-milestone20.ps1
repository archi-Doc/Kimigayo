[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Associated numbers are 21, 21.`nAssociated flags are true, true.`nContract forwarding finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 20 -Configuration $Configuration -Expected $expected
