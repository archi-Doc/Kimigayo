// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class BorrowDependencyStorageTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    [Trait("Purpose", "Allocation")]
    public void InspectionOnlyBodiesNeedNoLocalDependencyTable(int count)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InspectionLoans(count));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = Assert.Single(c.Ownership.Bodies, static x => x.Function.Name == "check");
        Assert.True(body.Places.Count >= count);
        Assert.Equal(0, body.BorrowDependencyCapacity);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionAndStoredLoansExecute(bool stored)
    {
        var source = VerificationWorkloads.InspectionLoans(4, stored);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = Assert.Single(c.Ownership.Bodies, static x => x.Function.Name == "check");
        Assert.Equal(stored, body.BorrowDependencyCapacity > 0);
        ScalarEmissionTest.EmitFixture("BorrowDependencyStorage" + stored, source, string.Empty);
    }

    [Theory]
    [InlineData("()", true)]
    [InlineData("value = \"changed\"", false)]
    public void InspectionExtentStillProtectsLaterArguments(string effect, bool accepted)
    {
        var source = "func inspect(text: ref/string, effect: ()) -> bool => text == \"x\"\nvar value = \"x\"\nlet result = inspect(value, " + effect + ")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(accepted, c.Ownership.Result.IsVerified);
        Assert.Equal(accepted, c.Emission.Validate(out _));
        if (!accepted)
        {
            Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
    }
}
