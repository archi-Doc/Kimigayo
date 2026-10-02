[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Owned iterator acquired.`nContinue after item 1.`nItem 1 destroyed.`nExit after item 2.`nItem 2 destroyed.`nItem 4 destroyed.`nItem 3 destroyed.`nOwned iteration finished.`nBorrowed iterator acquired.`nExternal first is 10; remaining total is 50.`nBorrowed iterator acquired.`nGeneral iteration finished.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 28 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
