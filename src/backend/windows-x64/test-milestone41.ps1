[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 41 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "First 12, last 50.`nInner 1..^1 has 4; tail 44 and 50; head 3.`nSpan 2..^1 resolves to 2..5, sum 100.`n1..=4 covers 4`n^8.. does not resolve`nThe end boundary is not an element.`nA closed end past the length is refused.`nQueue 4 2 3 1 after removing 9.`nClosed ranges reach 255 without overflow.`n"
$bounds = ': abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n"
# The source is immutable; all behavioral variants live in private harness directories.
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    # SPEC 4.6.2: an element position of any integer Type selects without a conversion.
    WidePositions = @{ source = (Edit-KimiSource $original 'let small: u8 = 1' 'let small: i128 = 1' 'let wide: u64 = 2' 'let wide: usize = 2'); stdout = $expected }
    # SPEC 4.6.4: a range written at the selection and the same range saved select the same view.
    DirectRange = @{ source = (Edit-KimiSource $original 'let middle = readings[inner]' 'let middle = readings[1..^1]'); stdout = $expected }
    # SPEC 4.6.3.1: literal-only boundaries take the expected range's boundary Types.
    ExpectedBoundaries = @{ source = (Edit-KimiSource $original 'let inner = 1..^1 // Range<i32, FromEnd<i32>>' 'let inner: Range<u8, FromEnd<u16>> = 1..^1'); stdout = $expected }
    # SPEC 4.6.3.4: a closed range ending below the maximum of its Type yields the same count.
    ClosedBelowMaximum = @{ source = (Edit-KimiSource $original 'for b in 250@u8..=255' 'for b in 249@u8..=254'); stdout = $expected }
    # SPEC 4.7.2: the Array entries take positions of other Types.
    ArrayPositions = @{ source = (Edit-KimiSource $original 'queue.insert(^0, 4)' 'queue.insert(3@u8, 4)' 'let removed = queue.remove(^2)' 'let removed = queue.remove(3@i64)'); stdout = $expected }
    # SPEC 4.6.9: ^0 is the end boundary, never an element; the one bounds check of the selection Aborts.
    ElementEnd = @{ source = "let values: [3 of i32] = [1, 2, 3]`nlet bad = values[^0]`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:11' + $bounds }
    # SPEC 4.6.2: a u64 offset beyond isize fails resolution; construction checked nothing.
    WideFromEnd = @{ source = "let values: [3 of i32] = [1, 2, 3]`nlet n: u64 = 18446744073709551615`nlet index = ^n`nlet bad = values[index]`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:4:11' + $bounds }
    # SPEC 4.6.3.4: a reversed closed range Aborts at its entry.
    ReversedClosed = @{ source = "let r = 3..=2`nfor i in r => ()`n"; stdout = ''; exit = 1; stderr = (Get-KimiLibraryLocation 'Core.kimi' '$abort("Reversed closed range")') + ': abort KIMI_E_ABORT: Reversed closed range' + "`n" }
    # SPEC 4.7.2: remove resolves an element position in its Kimigayo entry.
    RemoveEnd = @{ source = "var values: Array<i32> = [1]`nlet n = values.remove(^0)`n"; stdout = ''; exit = 1; stderr = (Get-KimiLibraryLocation 'Array.kimi' 'self.removeAt(') + $bounds }
}
Invoke-MilestoneVariants $variants $expected
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    ShapeEquality = @{ source = (Edit-KimiSource $original '    let middle = readings[inner]' ('    let middle = readings[inner]' + "`n" + '    let same = (0..3) == (0..=2)')); diagnostic = 'TypeMismatch_Kd' }
    FromEndIteration = @{ source = (Edit-KimiSource $original '    var bytes = 0' ('    for i in inner => ()' + "`n" + '    var bytes = 0')); diagnostic = 'UnsatisfiedConstraint_Kd' }
    FromEndArithmetic = @{ source = (Edit-KimiSource $original 'let last = ^1 // FromEnd<i32>' 'let last = ^1 + 1'); diagnostic = 'TypeMismatch_Kd' }
    FloatingKey = @{ source = (Edit-KimiSource $original 'let wide: u64 = 2' 'let wide = 2.5'); diagnostic = 'TypeMismatch_Kd' }
    PrivateConstruction = @{ source = (Edit-KimiSource $original 'let last = ^1 // FromEnd<i32>' 'let last = FromEnd<i32>.init(1)'); diagnostic = 'NoApplicableOverload_Kd' }
    BoundaryType = @{ source = (Edit-KimiSource $original 'let span = from..^1 //' 'let span: Range<i32, FromEnd<i32>> = from..^1 //'); diagnostic = 'TypeMismatch_Kd' }
    UserPosition = @{ source = (Edit-KimiSource $original 'public func main()' ('struct Mine' + "`n" + '    Self is Position' + "`n" + 'public func main()')); diagnostic = 'InvalidSelfClause_Kd' }
}
Complete-Milestone $invalid
