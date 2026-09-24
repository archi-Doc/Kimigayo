[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 23 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "Standard access finished.`nInput evaluated.`nReceiver evaluated.`nCustom set.`nCustom get.`nComputed set.`nComputed get.`nCopy properties finished.`n"
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = (Edit-KimiSource $original 'Meter' 'Gauge' 'level' 'reading' 'doubled' 'scaled'); stdout = $expected }
    Values = @{ source = (Edit-KimiSource $original 'return 8' 'return 14' 'level == 8' 'level == 14' 'doubled = 12' 'doubled = 20' 'doubled == 12' 'doubled == 20' 'raw == 6' 'raw == 10'); stdout = $expected }
}
Invoke-MilestoneVariants $variants $expected
$invalid = [ordered]@{
    PrivateSetter = @{ source = (Edit-KimiSource $original 'public var raw: i32' "public var raw: i32`n        private set"); diagnostic = 'InaccessibleBinding_Kd' }
    ConstructionCallsSetter = @{ source = (Edit-KimiSource $original 'self.level = 3' "self.level = 3`n        self.level = 4"); diagnostic = 'ReassignedLet_Kd' }
}
Complete-Milestone $invalid
