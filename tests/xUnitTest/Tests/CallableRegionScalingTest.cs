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
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        var storage = c.Ownership.Bodies.Sum(x => x.LocalRegionStorageBytes);
        var indexes = c.Ownership.Bodies.Sum(x => x.LocalRegionIndexCapacity);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.Equal(storage, c.Ownership.Bodies.Sum(x => x.LocalRegionStorageBytes));
            Assert.Equal(indexes, c.Ownership.Bodies.Sum(x => x.LocalRegionIndexCapacity));
        }

        foreach (var body in c.Ownership.Bodies)
        {
            Assert.True(body.LocalLoanFlowCapacity <= Math.Max(16L, 2L * body.PeakLocalLoanCells));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
