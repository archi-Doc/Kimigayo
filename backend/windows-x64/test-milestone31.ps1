[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 31 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

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
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = (Edit-KimiSource $original 'entries' 'mapping' 'position' 'ordinal'); stdout = $expected }
    Capacity = @{ source = (Edit-KimiSource $original 'additional: 3' 'additional: 7'); stdout = $expected }
    Empty = @{ source = @'
var entries: Dictionary<(), ()> = [:]
require entries.length == 0 and entries.capacity == 0 else => $abort("empty")
entries.reserve(0)
entries.clear()
entries.shrinkToFit()
for pair in entries@move => $abort("iteration")
'@; stdout = '' }
    FullIteration = @{ source = @'
var entries: Dictionary<i32, i32> = [:]
_ = entries.tryInsert(1, 10)
_ = entries.tryInsert(2, 20)
_ = entries.remove(1)
_ = entries.tryInsert(3, 30)
var order = 0
for (key, value) in entries@move
    require value == key * 10 else => $abort("value")
    order = order * 10 + key
    continue
require order == 23 else => $abort("order")
'@; stdout = '' }
    NaNAndZero = @{ source = @'
let nan: f64 = 0.0 / 0.0
var entries: Dictionary<f64, i32> = [:]
_ = entries.tryInsert(nan, 1)
_ = entries.tryInsert(-nan, 2)
_ = entries.tryInsert(0.0, 3)
_ = entries.tryInsert(-0.0, 4)
require entries.length == 2 else => $abort("equality")
require entries[nan] == 1 and entries[0.0] == 3 else => $abort("values")
require not (nan == nan) else => $abort("IEEE")
'@; stdout = '' }
    MissingKey = @{ source = 'var entries: Dictionary<i32, i32> = [:]' + [char]10 + 'entries[1] = 2' + [char]10; stdout = ''; exit = 1; stderr = '{name}.kimi:2:1: abort KIMI_E_MISSING_KEY: Dictionary key was not found' + [char]10 }
}
Invoke-MilestoneVariants $variants $expected

$setup = 'var entries: Dictionary<i32, i32> = [:]' + [char]10 + '_ = entries.tryInsert(1, 1)' + [char]10
$invalid = [ordered]@{
    DuplicateIntegerLiteral = @{ source = 'let entries: Dictionary<i32, i32> = [1_000: 1, (+1000): 2]'; diagnostic = 'DuplicateDictionaryKey_Kd' }
    DuplicateBooleanLiteral = @{ source = 'let entries: Dictionary<bool, i32> = [true: 1, (true): 2]'; diagnostic = 'DuplicateDictionaryKey_Kd' }
    DuplicateCharacterLiteral = @{ source = "let entries: Dictionary<char, i32> = ['a': 1, '\u(61)': 2]"; diagnostic = 'DuplicateDictionaryKey_Kd' }
    DuplicateStringLiteral = @{ source = 'let entries: Dictionary<string, i32> = ["a": 1, "\u(61)": 2]'; diagnostic = 'DuplicateDictionaryKey_Kd' }
    DuplicateUnitLiteral = @{ source = 'let entries: Dictionary<(), i32> = [(): 1, (()): 2]'; diagnostic = 'DuplicateDictionaryKey_Kd' }
    DuplicateAcrossRuntimeKey = @{ source = 'func unused(key: i32)' + [char]10 + '    if false' + [char]10 + '        let entries: Dictionary<i32, i32> = [1: 1, key: 2, (1): 3]'; diagnostic = 'DuplicateDictionaryKey_Kd' }
    MissingEquality = @{ source = 'struct Key' + [char]10 + '    public init() => ()' + [char]10 + 'var entries: Dictionary<Key, i32> = [:]'; diagnostic = 'UnsatisfiedConstraint_Kd' }
    MovedDictionary = @{ source = $setup + '_ = entries@move' + [char]10 + '_ = entries.length'; diagnostic = 'MovedPlace_Kd' }
    ImplicitTransfer = @{ source = $setup + 'let other = entries'; diagnostic = 'TransferRequired_Kd' }
    KeyLoanAtReplacement = @{ source = $setup + 'entries[entries[1]] = 2'; diagnostic = 'ComparisonLoanConflict_Kd' }
    MutationDuringIteration = @{ source = $setup + 'for (key, value) in entries' + [char]10 + '    entries.clear()' + [char]10 + '    require key + value == 2 else => $abort("borrow")'; diagnostic = 'CallActivationConflict_Kd' }
    ReadOnlyKey = @{ source = $setup + 'for (key, value) in entries' + [char]10 + '    key = value'; diagnostic = 'InvalidAssignment_Kd' }
    MovedInput = @{ source = 'var entries: Dictionary<i32, string> = [:]' + [char]10 + 'let text = "value"' + [char]10 + '_ = entries.tryInsert(1, text@move)' + [char]10 + '_ = entries.tryInsert(2, text@move)'; diagnostic = 'MovedPlace_Kd' }
}
Complete-Milestone $invalid
