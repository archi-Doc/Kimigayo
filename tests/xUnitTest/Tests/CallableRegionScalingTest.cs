// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class CallableRegionScalingTest
{
    public static TheoryData<string, int> Workloads => new()
    {
        { "candidates", 4 }, { "candidates", 8 }, { "candidates", 16 },
        { "results", 4 }, { "results", 8 }, { "results", 16 },
        { "regions", 4 }, { "regions", 8 }, { "regions", 16 },
        { "fixed-calls", 4 }, { "fixed-calls", 8 }, { "fixed-calls", 16 },
        { "fixed-roots", 4 }, { "fixed-roots", 8 }, { "fixed-roots", 16 },
        { "fixed-joins", 4 }, { "fixed-joins", 8 }, { "fixed-joins", 16 },
    };

    [Theory]
    [MemberData(nameof(Workloads))]
    public void GrowingPlansPreserveBehavior(string axis, int count)
        => ScalarEmissionTest.EmitFixture($"CallableRegionScaling{axis}{count}", VerificationWorkloads.CallableRegions(axis, count), string.Empty);

    [Theory]
    [MemberData(nameof(Workloads))]
    [Trait("Purpose", "Allocation")]
    public void GrowingPlansReuseBoundedStorage(string axis, int count)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.CallableRegions(axis, count));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        var storage = c.Ownership.Bodies.Sum(x => x.LocalRegionStorageBytes);
        var indexes = c.Ownership.Bodies.Sum(x => x.LocalRegionIndexCapacity);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Analyze().IsVerified);
            Assert.Equal(storage, c.Ownership.Bodies.Sum(x => x.LocalRegionStorageBytes));
            Assert.Equal(indexes, c.Ownership.Bodies.Sum(x => x.LocalRegionIndexCapacity));
        }

        foreach (var body in c.Ownership.Bodies)
        {
            Assert.True(body.LocalLoanFlowCapacity <= Math.Max(16L, 2L * body.PeakLocalLoanCells));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}
