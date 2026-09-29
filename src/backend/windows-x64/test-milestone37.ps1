[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "No orders to count.`nLarge orders: 3 overall, 2 in the tail.`nFirst order over 40 is 3.`nRunning total is 155.`nCategory 10 totals 75.`nCategory 20 totals 20.`nCategory 30 totals 60.`nStopped at order 4.`nRemoved order 1.`nOrder 1 destroyed.`nProcessing finished.`nOrder 5 destroyed.`nOrder 4 destroyed.`nOrder 3 destroyed.`nOrder 2 destroyed.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 37 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
