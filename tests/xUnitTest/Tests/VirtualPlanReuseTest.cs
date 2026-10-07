// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public sealed class VirtualPlanReuseTest
{
    [Theory]
    [InlineData(1, 1, 0, "obj", false)]
    [InlineData(8, 1, 0, "obj", false)]
    [InlineData(4, 8, 0, "rc", true)]
    [InlineData(4, 32, 0, "arc", false)]
    [InlineData(2, 1, 32, "obj", false)]
    [InlineData(2, 1, 128, "obj", false)]
    public void ScalingWorkloadsExecuteTheSameSelectedContracts(int depth, int slots, int aliases, string mode, bool factory)
        => ScalarEmissionTest.EmitFixture($"VirtualPlanD{depth}S{slots}A{aliases}{mode}{factory}", VerificationWorkloads.VirtualPlans(depth, slots, aliases, mode, factory), string.Empty);

    [Theory]
    [InlineData(8, 1, 0)]
    [InlineData(4, 32, 0)]
    [InlineData(2, 1, 128)]
    [Trait("Purpose", "Allocation")]
    public void FixedScalingWorkloadsReuseAllCompilerPhases(int depth, int slots, int aliases)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.VirtualPlans(depth, slots, aliases));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        var before = c.Ownership.Bodies.Sum(static body => body.BorrowStorageBytes);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
        Assert.Equal(before, c.Ownership.Bodies.Sum(static body => body.BorrowStorageBytes));
    }
}
