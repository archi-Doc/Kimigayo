// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Verification;
using Xunit;

namespace XunitTest;

public class BorrowSnapshotTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(8, false)]
    [InlineData(16, false)]
    [InlineData(8, true)]
    public void ManyLivePartLoansRetainIndependentInitialization(int count, bool checking)
    {
        var source = VerificationWorkloads.LivePartLoans(count, checking);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        void Analyze()
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Live part Loans failed.");
            }
        }

        for (var i = 0; i < 3; i++)
        {
            Analyze();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze, 2));
        ScalarEmissionTest.EmitFixture($"BorrowSnapshot{count}{checking}", source, string.Empty);
    }
}
