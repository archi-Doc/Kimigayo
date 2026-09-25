[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 29 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "No tasks.`nMany tasks.`nRemoved the last task.`nTask 1 destroyed.`nPopped a task.`nTask 3 destroyed.`nTwo tasks.`nTask 6 destroyed.`nCleared the spare tasks.`nIterating task 5.`nTask 5 destroyed.`nTask 2 destroyed.`nArray run finished.`nTask 4 destroyed.`n"
# The source is immutable; all behavioral variants live in private harness directories.
$complete = Edit-KimiSource $original 'require task.id == 5 else' 'require task.id == 5 or task.id == 2 else' 'Console.writeLine("Iterating task 5.")' 'Console.writeLine("Iterating.")' '        exit // task is destroyed first; the iterator then destroys the unyielded Task 2.' ''
$completeOutput = Edit-KimiSource $expected "Iterating task 5.`nTask 5 destroyed.`nTask 2 destroyed." "Iterating.`nTask 5 destroyed.`nIterating.`nTask 2 destroyed."
$capacityChecks = '    let spareCapacity = spare.capacity' + "`n" + '    spare.reserve(0)' + "`n" + '    spare.clear()' + "`n" + '    match spare.pop()' + "`n" + '        .None => ()' + "`n" + '        .Some(_) => $abort("Empty pop")' + "`n" + '    require spare.length == 0 and spare.capacity == spareCapacity else => $abort("No-op capacity")' + "`n" + '    spare@uniq.shrinkToFit()' + "`n" + '    require spare.length == 0 and spare.capacity <= spareCapacity else => $abort("Shrink")' + "`n" + '    Console.writeLine("Cleared the spare tasks.")'
$variants = [ordered]@{
    SharedStringIteration = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let names: Array<string> = ["name"]' + "`n" + '    for name in names => ()' + "`n" + '    Console.writeLine("Array run finished.")')); stdout = $expected }
    Renamed = @{ source = $original; stdout = $expected }
    Names = @{ source = (Edit-KimiSource $original 'Task' 'Job' 'tasks' 'jobs' 'describe' 'inspect'); stdout = (Edit-KimiSource $expected 'Task' 'Job' 'tasks' 'jobs') }
    Wide = @{ source = (Edit-KimiSource $original 'id: i32' 'id: i64'); stdout = $expected }
    Growth = @{ source = (Edit-KimiSource $original 'reserve(additional: 4)' 'reserve(additional: 0)' 'tasks.length == 0 and tasks.capacity >= 4' 'tasks.length == 0 and tasks.capacity == 0'); stdout = $expected }
    IndexValues = @{ source = (Edit-KimiSource $original 'insert(^0,' 'insert(Index.init(0, fromEnd: true),' 'remove(^1)' 'remove(Index.init(1, fromEnd: true))'); stdout = $expected }
    CompleteIteration = @{ source = $complete; stdout = $completeOutput }
    Capacity = @{ source = (Edit-KimiSource $original '    Console.writeLine("Cleared the spare tasks.")' $capacityChecks); stdout = $expected }
    SharedReads = @{ source = (Edit-KimiSource $original '    match tasks.pop()' ('    let item: ref/Task = tasks[0]' + "`n" + '    let explicit = tasks[0]@ref' + "`n" + '    require item.id == 5 and explicit.id == 5 else => $abort("Shared read")' + "`n" + '    match tasks@uniq.pop()')); stdout = $expected }
    SharedSliceIteration = @{ source = (Edit-KimiSource $original '    var spare: Array<Task>' ('    let view = tasks[..]' + "`n" + '    require view.length == 2 and view[0].id == 5 else => $abort("Slice")' + "`n" + '    var total = 0' + "`n" + '    for item in tasks => total += item.id' + "`n" + '    require total == 7 else => $abort("Shared iteration")' + "`n" + '    var spare: Array<Task>')); stdout = $expected }
    ResultIteration = @{ source = (Edit-KimiSource $original 'for task in tasks@move' 'for task in (if true => tasks@move else => tasks@move)'); stdout = $expected }
    ClearAbort = @{ source = "struct Task`n    public let id: i32`n    public init(id: i32) => self.id = id`n    deinit`n        if self.id == 2 => `$abort(`"stop`")`n        Console.writeLine(`"drop`")`nvar values: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]`nvalues@uniq.clear()`nConsole.writeLine(`"after`")`n"; stdout = "drop`n"; exit = 1; stderr = '{name}.kimi:5:28: abort KIMI_E_ABORT: stop' + "`n" }
    AbandonedArgument = @{ source = "struct Task`n    public let id: i32`n    public init(id: i32) => self.id = id`n    deinit => Console.writeLine(`"drop`")`nvar values: Array<Task> = []`nlet completed = work: do`n    values@uniq.insert(value: Task.init(1), index: (index: do`n        exit to work: false`n        exit to index: 0@isize`n    ))`n    exit to work: true`nrequire not completed and values.length == 0 else => `$abort(`"abandon`")`nConsole.writeLine(`"done`")`n"; stdout = "drop`ndone`n" }
    EmptyRemove = @{ source = "var values: Array<i32> = []`nlet n = values.remove(^1)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    EndRemove = @{ source = "var values: Array<i32> = [1]`nlet n = values.remove(^0)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    InvalidInsert = @{ source = "var values: Array<i32> = []`nvalues.insert(^1, 7)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds' + "`n" }
    NegativeReserve = @{ source = "var values: Array<i32> = []`nvalues.reserve(-1)`n"; stdout = ''; exit = 1; stderr = '{name}.kimi:2:1: abort KIMI_E_ARGUMENT: Invalid argument value' + "`n" }
}
Invoke-MilestoneVariants $variants $expected
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    MovedArray = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let invalid = tasks.length' + "`n" + '    Console.writeLine("Array run finished.")')); diagnostic = 'MovedPlace_Kd' }
    ImplicitTransfer = @{ source = (Edit-KimiSource $original '    let last = tasks.remove(^1)' ('    let invalid = tasks' + "`n" + '    let last = tasks.remove(^1)')); diagnostic = 'TransferRequired_Kd' }
    MissingExclusive = @{ source = (Edit-KimiSource $original '    let last = tasks.remove(^1)' ('    var other: Array<Task> = []' + "`n" + '    Kimi.Intrinsics.swap(tasks, other@uniq)' + "`n" + '    let last = tasks.remove(^1)')); diagnostic = 'ExclusiveBorrowRequired_Kd' }
    SharedMutation = @{ source = (Edit-KimiSource $original 'tasks.append(Task.init(1))' 'tasks@ref.append(Task.init(1))'); diagnostic = 'NoApplicableOverload_Kd' }
    ElementMove = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let invalid = tasks[0]@move' + "`n" + '    tasks[0] = Task.init(5)')); diagnostic = 'UnsupportedOwnership_Kd' }
    LiveBorrow = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks@ref' + "`n" + '    tasks[0] = Task.init(5)' + "`n" + '    let invalid = view.length')); diagnostic = 'ComparisonLoanConflict_Kd' }
    LiveElement = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks[0]@ref' + "`n" + '    tasks[0] = Task.init(5)' + "`n" + '    let invalid = view.id')); diagnostic = 'ComparisonLoanConflict_Kd' }
    # Valid forms outside the verified generation boundary: rejected by ownership analysis, never at generation.
    ZeroSizedElement = @{ source = (Edit-KimiSource $original '    Console.writeLine("Array run finished.")' ('    let units: Array<()> = [()]' + "`n" + '    Console.writeLine("Array run finished.")')); diagnostic = 'UnsupportedOwnership_Kd' }
    BareElementRead = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let invalid = tasks[0]' + "`n" + '    tasks[0] = Task.init(5)')); diagnostic = 'TransferRequired_Kd' }
    LiveEmptySlice = @{ source = (Edit-KimiSource $original '    tasks[0] = Task.init(5)' ('    let view = tasks[0..0]' + "`n" + '    tasks.reserve(0)' + "`n" + '    let invalid = view.length' + "`n" + '    tasks[0] = Task.init(5)')); diagnostic = 'CallActivationConflict_Kd' }
}
Complete-Milestone $invalid
