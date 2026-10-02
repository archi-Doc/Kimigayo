[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = @'
Duplicate returned both inputs.
Rejected item destroyed.
Rejected key destroyed.
Replacement key destroyed.
Replacement returned item 10.
Item 10 destroyed.
Item 11 destroyed.
Stored keys and insertion order preserved.
Removed the original key and item.
Item 12 destroyed.
Key 1 destroyed.
Owning iteration starts at key 2.
Item 20 destroyed.
Key 2 destroyed.
Item 30 destroyed.
Key 3 destroyed.
Item 40 destroyed.
Key 4 destroyed.
Dictionary run finished.
'@ + [char]10
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 31 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
