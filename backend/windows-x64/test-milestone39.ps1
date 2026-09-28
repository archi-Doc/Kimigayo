[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('All', 'Rejections')] [string] $Cases = 'All'
)
. (Join-Path $PSScriptRoot 'milestone-harness.ps1') -Milestone 39 -ToolchainRoot $ToolchainRoot -Configuration $Configuration -Cases $Cases

$expected = "Owned element updated to 5.`nBorrowed elements total 30.`nExclusive referent updated to 33.`nTotals are 6, 30 and 73.`nSemantics-generic follow finished.`n"
# The source is immutable; all behavioral variants live in private harness directories.
$variants = [ordered]@{
    Renamed = @{ source = $original; stdout = $expected }
    # SPEC 10.2: a bare pair Place expected as ref/T is shared-borrowed through the layer, like the explicit follow.
    ImplicitView = @{ source = (Edit-KimiSource $original '    return c[i]@follow@ref' '    return c[i]'); stdout = $expected }
    # SPEC 7.3: the implicit shared receiver and the explicit p@follow receiver select the same Place.
    ExplicitReceiver = @{ source = (Edit-KimiSource $original '        sum += c[i].load()' '        sum += c[i]@follow.load()'); stdout = $expected }
}
Invoke-MilestoneVariants $variants $expected
# Rejections must name the actual diagnostic and must leave neither IR nor an executable.
$invalid = [ordered]@{
    MissingAdmission = @{ source = (Edit-KimiSource $original ('ref/T during c' + "`n" + '    s is value or valueborrow' + "`n" + '    return c[i]@follow@ref') ('ref/T during c' + "`n" + '    return c[i]@follow@ref')); diagnostic = 'UnprovenConstraint_Kd' }
    SharedExclusive = @{ source = (Edit-KimiSource $original '    s is owner or uniq' '    s is value or valueborrow'); diagnostic = 'SharedPathAccess_Kd' }
    FollowTake = @{ source = (Edit-KimiSource $original '    return c[i]@follow@uniq' ('    let taken = c[i]@follow@move' + "`n" + '    return c[i]@follow@uniq')); diagnostic = 'ExclusivePathTake_Kd' }
    RefsExclusive = @{ source = (Edit-KimiSource $original '    let target = viewUniq(uniqs@uniq, 0)' ('    let target = viewUniq(uniqs@uniq, 0)' + "`n" + '    var shared = Collection<ref/Node>.init([a@ref])' + "`n" + '    let wrong = viewUniq(shared@uniq, 0)')); diagnostic = 'NoApplicableOverload_Kd' }
}
Complete-Milestone $invalid
