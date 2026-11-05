// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class InspectionLoanTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(32)]
    [InlineData(128)]
    [Trait("Purpose", "Allocation")]
    public void AWarmAnalysisOfStoredInspectionsAllocatesNothing(int count)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InspectionLoans(count, true));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    [Trait("Purpose", "Allocation")]
    public void AWarmAnalysisOfInspectionsAllocatesNothing(int count)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InspectionLoans(count));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analyze().IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Analyze()));
    }

    // R6-check H8c D8 (H8a OQ2): on the inspection extent's N locals and temporaries at N = 32, 64, 128, A5's work grows within A3 6.3's
    // dense terms (factor 5), its Loan-set bytes and the cold analysis's allocation linearly (CallableRegionScalingTest's bounds).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Purpose", "Allocation")]
    public void InspectionLoansScaleWithinTheirBound(bool stored)
    {
        string[] sources = [.. new[] { 32, 64, 128 }.Select(n => VerificationWorkloads.InspectionLoans(n, stored))];
        CallableRegionScalingTest.WorkScales(output, sources, 5);
        CallableRegionScalingTest.ColdAnalysisScales(output, sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionAndStoredLoansExecute(bool stored)
    {
        var source = VerificationWorkloads.InspectionLoans(4, stored);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("BorrowDependencyStorage" + stored, source, string.Empty);
    }

    [Theory]
    [InlineData("()", true)]
    [InlineData("value = \"changed\"", false)]
    public void InspectionExtentStillProtectsLaterArguments(string effect, bool accepted)
    {
        var source = "func inspect(text: ref/string, effect: ()) -> bool => text == \"x\"\nvar value = \"x\"\nlet result = inspect(value, " + effect + ")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(accepted, c.Analysis.IsVerified);
        Assert.Equal(accepted, c.Generate(TextWriter.Null));
        if (!accepted)
        {
            Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        }
    }
}
