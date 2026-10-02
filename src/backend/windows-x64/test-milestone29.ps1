[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "No tasks.`nMany tasks.`nRemoved the last task.`nTask 1 destroyed.`nPopped a task.`nTask 3 destroyed.`nTwo tasks.`nTask 6 destroyed.`nCleared the spare tasks.`nIterating task 5.`nTask 5 destroyed.`nTask 2 destroyed.`nArray run finished.`nTask 4 destroyed.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 29 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
