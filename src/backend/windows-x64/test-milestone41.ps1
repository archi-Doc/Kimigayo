[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "First 12, last 50.`nInner 1..^1 has 4; tail 44 and 50; head 3.`nSpan 2..^1 resolves to 2..5, sum 100.`n1..=4 covers 4`n^8.. does not resolve`nThe end boundary is not an element.`nA closed end past the length is refused.`nQueue 4 2 3 1 after removing 9.`nClosed ranges reach 255 without overflow.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 41 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
