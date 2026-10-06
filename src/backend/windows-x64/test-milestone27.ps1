[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Start evaluated.`nEnd evaluated.`nNested views retain 20 and 30.`nEnd boundary is not an element.`nShort target rejected.`nLast text.`nLast text.`nSlice Origins preserved.`nPair second.`nPair replaced.`nExclusive views and independent copies verified.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 27 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
