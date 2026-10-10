// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Verification;
using Xunit;

namespace XunitTest;

public class BorrowSnapshotTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void ManyLivePartLoansRetainIndependentInitialization(int count)
    {
        var source = VerificationWorkloads.LivePartLoans(count);
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
        ScalarEmissionTest.EmitFixture($"BorrowSnapshot{count}False", source, string.Empty);
    }
}
