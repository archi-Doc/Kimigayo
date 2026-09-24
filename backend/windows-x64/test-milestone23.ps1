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
    Compound = @{ source = (Edit-KimiSource $original '.level = input()' '.level += input()' 'level == 8' 'level == 11'); stdout = "Standard access finished.`nReceiver evaluated.`nCustom get.`nInput evaluated.`nCustom set.`nCustom get.`nComputed set.`nComputed get.`nCopy properties finished.`n" }
    RestrictedRead = @{ source = (Edit-KimiSource $original 'public var raw: i32' "public var raw: i32`n        private set" "    meter.raw = 4`n" '' 'meter.raw == 4' 'meter.raw == 2'); stdout = $expected }
    DifferentSetterInput = @{ source = (Edit-KimiSource $original "set(value: i32) -> ()`n            Console.writeLine(`"Computed set.`")`n            self.raw = value / 2" "set(value: bool) -> ()`n            Console.writeLine(`"Computed set.`")`n            if value => self.raw = 6" 'meter.doubled = 12' 'meter.doubled = true'); stdout = $expected }
}
Invoke-MilestoneVariants $variants $expected
$projection = "struct Point`n    Self is Copy`n    public var x: i32 = 1`nstruct S`n    public var point: Point = Point.init()`n        get() -> Point => storage`npublic func main()`n    var s = S.init()`n"
$prefix = $original.Substring(0, $original.IndexOf('public func main()', [StringComparison]::Ordinal))
$invalid = [ordered]@{
    PrivateSetter = @{ source = (Edit-KimiSource $original 'public var raw: i32' "public var raw: i32`n        private set"); diagnostic = 'InaccessibleBinding_Kd' }
    ConstructionCallsSetter = @{ source = (Edit-KimiSource $original 'self.level = 3' "self.level = 3`n        self.level = 4"); diagnostic = 'ReassignedLet_Kd' }
    ConstructionCallsGetter = @{ source = (Edit-KimiSource $original 'self.level = 3' "self.level = 3`n        _ = self.level"); diagnostic = 'UnsupportedBinding_Kd' }
    GetterStorageWrite = @{ source = (Edit-KimiSource $original 'return storage' "storage = 1`n            return storage"); diagnostic = 'InvalidAssignment_Kd' }
    WrongSetterInput = @{ source = (Edit-KimiSource $original 'meter.doubled = 12' 'meter.doubled = true'); diagnostic = 'TypeMismatch_Kd' }
    PrivateGetter = @{ source = (Edit-KimiSource $original 'get() -> i32' 'private get() -> i32'); diagnostic = 'InaccessibleBinding_Kd' }
    ImmutableReceiver = @{ source = (Edit-KimiSource $original 'var meter = Meter.init()' 'let meter = Meter.init()'); diagnostic = 'InvalidAssignment_Kd' }
    RestrictedExclusiveBorrow = @{ source = (Edit-KimiSource $prefix 'public var raw: i32' "public var raw: i32`n        private set") + "public func main()`n    var meter = Meter.init()`n    let edit = meter.raw@uniq`n"; diagnostic = 'InvalidAssignment_Kd' }
    GetterExclusiveBorrow = @{ source = $prefix + "public func main()`n    var meter = Meter.init()`n    let edit = meter.level@uniq`n"; diagnostic = 'InvalidAssignment_Kd' }
    RestrictedMove = @{ source = (Edit-KimiSource $prefix 'public var raw: i32' "public var raw: i32`n        private set") + "public func main()`n    let meter = Meter.init()`n    let taken = meter.raw@move`n"; diagnostic = 'InaccessibleBinding_Kd' }
    CustomSetterMove = @{ source = (Edit-KimiSource $prefix 'public var raw: i32' "public var raw: i32`n        set(value: i32) -> () => storage = value") + "public func main()`n    let meter = Meter.init()`n    let taken = meter.raw@move`n"; diagnostic = 'InvalidAssignment_Kd' }
    ExpiredGetterResult = @{ source = $prefix + "public func main()`n    let meter = Meter.init()`n    let view = meter.level@ref`n    _ = view + 1`n"; diagnostic = 'ComparisonLoanConflict_Kd' }
    GetterChildWrite = @{ source = $projection + "    s.point.x = 9`n"; diagnostic = 'InvalidAssignment_Kd' }
    GetterChildUpdate = @{ source = $projection + "    s.point.x += 9`n"; diagnostic = 'InvalidAssignment_Kd' }
}
Complete-Milestone $invalid
