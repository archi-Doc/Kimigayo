[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 28 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "Owned iterator acquired.`nContinue after item 1.`nItem 1 destroyed.`nExit after item 2.`nItem 2 destroyed.`nItem 4 destroyed.`nItem 3 destroyed.`nOwned iteration finished.`nBorrowed iterator acquired.`nExternal first is 10; remaining total is 50.`nBorrowed iterator acquired.`nGeneral iteration finished.`n"
$prefix = $original.Substring(0, $original.IndexOf('public func main()'))
$batch = '    let batch = Batch<Item>.init(Item.init(1), Item.init(2), Item.init(3), Item.init(4))'
$returnSource = $prefix + 'func first(batch: Batch<Item>) -> Item' + "`n" + '    for item in batch@move => return item@move' + "`n" + '    $abort("Empty")' + "`npublic func main()`n$batch`n" + '    let value = first(batch@move)' + "`n" + '    require value.id == 1 else => $abort("First")' + "`n" + '    Console.writeLine("Done.")' + "`n"
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    Exhaustion = @{
        source = $prefix + "public func main()`n$batch`n    for item in batch@move`n        Console.writeLine(`"Body.`")`n    Console.writeLine(`"Done.`")`n"
        stdout = "Owned iterator acquired.`nBody.`nItem 1 destroyed.`nBody.`nItem 2 destroyed.`nBody.`nItem 3 destroyed.`nBody.`nItem 4 destroyed.`nDone.`n"
    }
    Empty = @{
        source = Edit-KimiSource $original 'require self.position < 4 else' 'require self.position < 0 else'
        stdout = "Owned iterator acquired.`nItem 4 destroyed.`nItem 3 destroyed.`nItem 2 destroyed.`nItem 1 destroyed.`nOwned iteration finished.`nBorrowed iterator acquired.`nExternal first is 10; remaining total is 50.`nBorrowed iterator acquired.`nGeneral iteration finished.`n"
    }
    Return = @{
        source = $returnSource
        stdout = "Owned iterator acquired.`nItem 4 destroyed.`nItem 3 destroyed.`nItem 2 destroyed.`nDone.`nItem 1 destroyed.`n"
    }
    Unnamed = @{
        source = $prefix + "public func main()`n$batch`n    for _ in batch@move => exit`n    Console.writeLine(`"Done.`")`n"
        stdout = "Owned iterator acquired.`nItem 1 destroyed.`nItem 4 destroyed.`nItem 3 destroyed.`nItem 2 destroyed.`nDone.`n"
    }
}
Invoke-MilestoneVariants $variants $expected
$invalid = [ordered]@{
    ReusedSubject = @{ source = Edit-KimiSource $original '    Console.writeLine("Owned iteration finished.")' ('    let again = batch@move' + "`n" + '    Console.writeLine("Owned iteration finished.")'); diagnostic = 'MovedPlace_Kd' }
    SharedMode = @{ source = Edit-KimiSource $original 'for item in batch@move' 'for item in batch'; diagnostic = 'UnresolvedBinding_Kd' }
    WrongArity = @{ source = $prefix + "public func main()`n$batch`n    for (a, b) in batch@move => ()`n"; diagnostic = 'TypeMismatch_Kd' }
    WrongIterator = @{ source = Edit-KimiSource $original 'associate IntoIterable.IteratorType is Drain<T>' 'associate IntoIterable.IteratorType is i32'; diagnostic = 'IncompatibleContractImplementation_Kd' }
    RetainedSource = @{
        source = Edit-KimiSource (Edit-KimiSource $original '    let values: [3 of i32]' '    var values: [3 of i32]') '    require first == 10 and total == 50' ('    values[0] = 11' + "`n" + '    require first == 10 and total == 50')
        diagnostic = 'ComparisonLoanConflict_Kd'
    }
}
Complete-Milestone $invalid
