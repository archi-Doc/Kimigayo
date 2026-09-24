[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 30 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "User comparisons keep their operands.`nTuple comparisons compose witnesses.`nFloating Contract equality is NaN-reflexive.`nComparison contracts finished.`n"
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = (Edit-KimiSource $original 'Key' 'Token' 'equivalent' 'sameValue'); stdout = $expected }
    Values = @{ source = (Edit-KimiSource $original 'Key.init(2)' 'Key.init(3)' 'first.value == 2' 'first.value == 3'); stdout = $expected }
    Float32 = @{ source = (Edit-KimiSource $original 'f64' 'f32'); stdout = $expected }
}
Invoke-MilestoneVariants $variants $expected
$main = $original.IndexOf('public func main()', [StringComparison]::Ordinal)
if ($main -lt 0) { throw 'Missing main anchor' }
$declarations = $original.Substring(0, $main)
$simple = $declarations + "public func main()`n    let first = Key.init(2)`n    let last = Key.init(5)`n    let result = first == last`n"
$equals = 'public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value'
# Each rejection has a small independent main so pending composition cannot mask the required error.
$invalid = [ordered]@{
    MissingConformance = @{ source = (Edit-KimiSource $simple "    Self is Comparable`n" ''); diagnostic = 'UnsatisfiedConstraint_Kd' }
    WrongEquality = @{ source = (Edit-KimiSource $simple $equals 'public func equals(self: ref/Self, other: ref/Self) -> i32 => 0'); diagnostic = 'IncompatibleContractImplementation_Kd' }
    ExclusiveReceiver = @{ source = (Edit-KimiSource $simple 'equals(self: ref/Self' 'equals(self: uniq/Self'); diagnostic = 'MissingContractImplementation_Kd' }
    MissingEquality = @{ source = (Edit-KimiSource $simple "    $equals`n" ''); diagnostic = 'MissingContractImplementation_Kd' }
    MissingPremise = @{ source = (Edit-KimiSource $simple "    T is Equatable`n" ''); diagnostic = 'UnprovenConstraint_Kd' }
    MixedTypes = @{ source = (Edit-KimiSource $simple 'first == last' 'first == 1'); diagnostic = 'TypeMismatch_Kd' }
    FloatingOrder = @{ source = $declarations + "public func main()`n    let nan: f64 = 0.0 / 0.0`n    let invalid = order(nan@ref, nan@ref)`n"; diagnostic = 'NoApplicableOverload_Kd' }
    ConflictingMove = @{ source = $declarations + "func take(value: Key) -> Key => value@move`npublic func main()`n    let first = Key.init(2)`n    let invalid = first == take(first@move)`n"; diagnostic = 'ComparisonLoanConflict_Kd' }
}
Complete-Milestone $invalid
