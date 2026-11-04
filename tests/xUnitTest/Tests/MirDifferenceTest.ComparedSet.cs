// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The ledger's compared set (DECISIONS R4-M4, R4-M17, R4-M67): its analyses, its MIR column, the split of a compilation's records and
// the matching of the two sides.
public sealed partial class MirDifferenceTest
{
    // R4-M17: the sides match as multisets on code and subject node: a node's second old record of one code stays old, and another code
    // at a matched node is new. R4-M67: a matched MIR problem that names another requirement writes the pair's as old>mir; a MIR record
    // that names none (-) agrees.
    [Fact]
    public void RecordsMatchOnCodeAndSubjectNode()
    {
        var c = CompilationTestHelper.Parse("func f(a: i32) -> i32 => a\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var (a, b) = (nodes[^1], nodes[^2]);
        var (moved, uninitialized, bound) = (DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd, DiagnosticCode.UnsatisfiedEffectBound_Kd);
        MirDifferenceLedger.Entry[] old =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(moved, "Ownership.PossiblyMovedUse", "A1", a),
            new(uninitialized, "Ownership.UninitializedUse", "A1", b), new(bound, "Binding.VirtualEffectBound", "A4", b),
            new(DiagnosticCode.Unsupported_Kd, "Ownership.Unsupported", "U", b),
        ];
        MirDifferenceLedger.Entry[] mir =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(uninitialized, "Ownership.UninitializedUse", "A1", a),
            new(bound, "Ownership.VirtualEffectBound", "A4", b), new(DiagnosticCode.Unsupported_Kd, "-", "B", b),
        ];
        var rows = MirDifferenceLedger.Match([.. old], [.. mir]);
        var renamed = old[3] with { Requirement = "Binding.VirtualEffectBound>Ownership.VirtualEffectBound" };
        Assert.Equal([("both", old[0]), ("old", old[1]), ("old", old[2]), ("both", renamed), ("both", old[4]), ("new", mir[1])], rows);
    }

    // R4-M4: the compared set names its owning analysis; pass-through records have none.
    [Theory]
    [InlineData("Ownership", (int)OwnershipRequirement.PossiblyMovedUse, DiagnosticCode.MovedPlace_Kd, "A1")]
    [InlineData("Binding", (int)BindingFailure.TransferRequired, DiagnosticCode.TransferRequired_Kd, null)]
    [InlineData("Ownership", (int)OwnershipRequirement.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    [InlineData("ControlFlow", 0, DiagnosticCode.RequireFallthrough_Kd, "A3")]
    [InlineData("Binding", (int)BindingFailure.None, DiagnosticCode.FunctionFallthrough_Kd, null)]
    [InlineData("Binding", (int)BindingFailure.OriginRelation, DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5")]
    public void TheComparedSetNamesItsAnalysis(string partition, int kind, DiagnosticCode code, string? analysis)
        => Assert.Equal(analysis, MirDifferenceLedger.AnalysisOf(new(Enum.Parse<DiagnosticPartition>(partition), (ushort)kind), code));

    // R4-M4: every code an ownership failure reports is compared, except Internal's (TransferRequired is Binding's since H1, BU-84).
    // R4-M67: each MIR pair of the
    // compared set names its row's analysis, and PLAN10 §7's consistency holds: an Ownership requirement with a code the old analysis
    // gives that failure (OwnershipIssue.Code), or ControlFlow with the require fallthrough; a pair outside it, such as MirReport's
    // disagreement record, has none.
    [Fact]
    public void TheComparedSetCoversEachOwnershipCode()
    {
        var images = new HashSet<(DiagnosticRequirement, DiagnosticCode)>();
        foreach (var (failure, code) in PipelineSwitchTest.OwnershipCodes())
        {
            var passes = failure is OwnershipRequirement.Internal;
            Assert.True(passes == (MirDifferenceLedger.AnalysisOf(DiagnosticRequirement.Ownership(failure), code) is null), $"{failure} {code}");
            images.Add((DiagnosticRequirement.Ownership(failure), code));
        }

        images.Add((DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd));
        foreach (var (_, code, analysis, mir, _) in MirDifferenceLedger.Compared)
        {
            if (mir is { } requirement)
            {
                Assert.True(images.Contains((requirement, code)), $"{requirement.Name} {code}");
                Assert.Equal(analysis, MirDifferenceLedger.MirAnalysisOf(requirement, code));
            }
        }

        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Binding(BindingFailure.TransferRequired), DiagnosticCode.TransferRequired_Kd));
        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Ownership(OwnershipRequirement.Internal), DiagnosticCode.InternalInvariant_Kd));
    }
}
