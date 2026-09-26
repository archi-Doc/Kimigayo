[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 37 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "No orders to count.`nLarge orders: 3 overall, 2 in the tail.`nFirst order over 40 is 3.`nRunning total is 155.`nCategory 10 totals 75.`nCategory 20 totals 20.`nCategory 30 totals 60.`nStopped at order 4.`nRemoved order 1.`nOrder 1 destroyed.`nProcessing finished.`nOrder 5 destroyed.`nOrder 4 destroyed.`nOrder 3 destroyed.`nOrder 2 destroyed.`n"
# The source is immutable; all behavioral variants live in private harness directories.
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    IndexTail = @{ source = (Edit-KimiSource $original 'let tail = orders[2..]' 'let tail = orders[^3..]'); stdout = $expected }
    ResolvedTail = @{ source = (Edit-KimiSource $original 'let tail = orders[2..]' 'let tail = orders[(2..).resolve(orders.length)]'); stdout = $expected }
}
Invoke-MilestoneVariants $variants $expected
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    BareReturn = @{ source = (Edit-KimiSource $original '    return sums@move' '    return sums'); diagnostic = 'TransferRequired_Kd' }
    ViewThenMutation = @{ source = (Edit-KimiSource $original '        let tail = orders[2..] // A borrowed view of orders 3, 4 and 5.' ('        let tail = orders[2..]' + "`n" + '        orders.append(Order.init(6, 10, 1))')); diagnostic = 'CallActivationConflict_Kd' }
    MovedOrders = @{ source = (Edit-KimiSource $original ('    for order in orders' + "`n" + '        seen += 1') ('    for order in orders@move' + "`n" + '        seen += 1')); diagnostic = 'MovedPlace_Kd' }
}
Complete-Milestone $invalid
