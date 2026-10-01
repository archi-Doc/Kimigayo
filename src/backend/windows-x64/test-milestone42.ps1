[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$expected = "Wrapped 4, -128, 0, -128, -128.`nHalf is 1069547520 in bits; 300 wraps to 44; 4 leaves the wrapping Type.`nLiterals 3, 9007199254740993, 100000000000000000000000000000000000000.`nHashes 1093368299 and 2595871832.`nDraws 0 98 46 35; checksums 10 and 15781573103004042447.`nCounter wrapped to 4, a key seen 3 times.`n"
& (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 42 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Expected $expected
