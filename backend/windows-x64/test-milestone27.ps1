[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 27 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "Start evaluated.`nEnd evaluated.`nNested views retain 20 and 30.`nEnd boundary is not an element.`nShort target rejected.`nLast text.`nLast text.`nSlice Origins preserved.`nPair second.`nPair replaced.`n"
# A user Type publishing its Places through the Indexable Contracts (SPEC 4.6.9), shared by the Place rejections.
$pair = @'
struct Pair<T>
    Self is UniqIndexable<isize>
    associate Element is T
    var first: T
    var second: T
    public init(first: T, second: T)
        self.first = first@move
        self.second = second@move
    public func index(self, key: ref/isize) -> place ref/T during self
        if key == 0 => return self.first
        return self.second
    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/T during self
        if key == 0 => return self.first
        return self.second

'@.Replace("`r`n", "`n")
# The source is immutable; all behavioral variants live in private harness directories.
$trySplit = 'let parts = match middle.trySplitAt(1)' + "`n" + '        .Some(let split) => split' + "`n" + '        .None => $abort("split")'
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    IndexInit = @{ source = (Edit-KimiSource $original 'let last: Index = ^1' 'let last: Index = Index.init(1, fromEnd: true)'); stdout = $expected }
    InclusiveKey = @{ source = (Edit-KimiSource $original 'exit to selection: row[resolved]' 'exit to selection: row[1..=2]'); stdout = $expected }
    ResolvedInit = @{ source = (Edit-KimiSource $original 'let resolved = bounds.resolve(grid[1].length)' 'let resolved = ResolvedRange.init(start: 1, end: 3)'); stdout = $expected }
    TrySplit = @{ source = (Edit-KimiSource $original 'let parts = middle.splitAt(1)' $trySplit); stdout = $expected }
    EmptyRange = @{ source = (Edit-KimiSource $original 'let empty = middle[^0..]' 'let empty = middle[2..2]'); stdout = $expected }
    SavedReapplied = @{ source = (Edit-KimiSource $original '    Console.writeLine("Nested views retain 20 and 30.")' ('    require bounds.resolve(grid[0].length).end == 3 and bounds == (1..^1) and bounds != (1..=2) else => $abort("Reapplied bounds failed")' + "`n" + '    Console.writeLine("Nested views retain 20 and 30.")')); stdout = $expected }
    IndexKeys = @{ source = (Edit-KimiSource $original 'middle[0] == 20 and middle[last] == 30' 'middle[Index.init(0)] == 20 and middle[^1] == 30'); stdout = $expected }
    TryGetPosition = @{ source = (Edit-KimiSource $original 'match middle.tryGet(^0)' 'match middle.tryGet(2)'); stdout = $expected }
    TrySlice = @{ source = (Edit-KimiSource $original '    require tail(middle)[0] == 30 else => $abort("Generic reslice failed")' ('    require tail(middle)[0] == 30 else => $abort("Generic reslice failed")' + "`n" + '    match middle.trySlice(^0..)' + "`n" + '        .Some(let none) => require none.isEmpty else => $abort("Empty view failed")' + "`n" + '        .None => $abort("From-end view refused")' + "`n" + '    match middle.trySlice(3..)' + "`n" + '        .Some(_) => $abort("Long view accepted")' + "`n" + '        .None => ()')); stdout = $expected }
    ReversedRange = @{ source = "let values: [3 of i32] = [1, 2, 3]`nlet bad = values[2..1]`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:11: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    ShortTarget = @{ source = "let bounds = ResolvedRange.init(start: 2, end: 5)`nlet values: [3 of i32] = [1, 2, 3]`nlet failed = values[bounds]`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:3:14: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
}
Invoke-MilestoneVariants $variants $expected
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    RangeIteration = @{ source = "for i in 0..3`n    ()`n"; diagnostic = 'UnsupportedBinding_Kd' }
    IntegerIndex = @{ source = "let values: [3 of i32] = [1, 2, 3]`nlet i: i32 = 1`nlet v = values[i]`n"; diagnostic = 'TypeMismatch_Kd' }
    NonCopyIndexedMove = @{ source = "let text: [2 of string] = [`"a`", `"b`"]`nlet words = text[..]`nlet taken = words[0]@move`n"; diagnostic = 'SharedPathAccess_Kd' }
    SliceElementWrite = @{ source = "var text: [2 of string] = [`"a`", `"b`"]`nlet words = text[..]`nwords[0] = `"c`"`n"; diagnostic = 'SharedPathAccess_Kd' }
    ConflictingMutation = @{ source = "var data: [2 of i32] = [1, 2]`nlet view = data[..]`ndata[0] = 5`nlet n = view.length`n"; diagnostic = 'ComparisonLoanConflict_Kd' }
    BarePlaceRead = @{ source = $pair + "var pair = Pair<string>.init(`"a`", `"b`")`nlet bare = pair[0]`n"; diagnostic = 'TransferRequired_Kd' }
    PlaceMove = @{ source = $pair + "var pair = Pair<string>.init(`"a`", `"b`")`nlet taken = pair[0]@move`n"; diagnostic = 'ExclusivePathTake_Kd' }
    LetUpdate = @{ source = $pair + "let pair = Pair<i32>.init(1, 2)`npair[0] = 3`n"; diagnostic = 'InvalidAssignment_Kd' }
    SharedPathUpdate = @{ source = $pair + "func f(pair: ref/Pair<i32>) => pair[0] = 3`n"; diagnostic = 'SharedPathAccess_Kd' }
    MismatchedElement = @{ source = "struct Wrong`n    Self is Indexable<isize>`n    associate Element is string`n    var value: i32`n    public init(value: i32) => self.value = value`n    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value`n"; diagnostic = 'IncompatibleContractImplementation_Kd' }
    MissingIndexUniq = @{ source = "struct Missing`n    Self is UniqIndexable<isize>`n    associate Element is i32`n    var value: i32`n    public init(value: i32) => self.value = value`n    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value`n"; diagnostic = 'MissingContractImplementation_Kd' }
}
Complete-Milestone $invalid
