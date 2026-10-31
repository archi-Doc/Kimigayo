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
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }
}
