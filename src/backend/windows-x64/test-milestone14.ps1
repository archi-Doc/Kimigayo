[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Accepted three jobs.`nAccumulator destroyed.`nObject total is 12.`nAccumulator destroyed.`nPipeline finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 14 -Configuration $Configuration -Expected $expected
