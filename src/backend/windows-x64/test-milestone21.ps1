[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$default = "Default index evaluated.`n"
$expected = "${default}Specialized selection is 30.`nExplicit selection is 10.`n${default}Forwarded selection is 30.`n${default}Ordinary selection is 4.`n${default}Local selection is 9.`nSpecialization finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 21 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
