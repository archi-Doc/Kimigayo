// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class InheritedPlanReuseTest
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 1)]
    [InlineData(32, 1)]
    [InlineData(4, 8)]
    [InlineData(4, 32)]
    public void DeepAndWideStorageRetainsFieldIdentity(int depth, int width)
        => ScalarEmissionTest.EmitFixture($"InheritedPlans{depth}x{width}", VerificationWorkloads.InheritedPlans(depth, width), string.Empty);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(32, 1)]
    [InlineData(4, 32)]
    public void WarmFieldAndBasePlansAllocateNothing(int depth, int width)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.InheritedPlans(depth, width));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    [Theory]
    [InlineData("func read(self: ref/Self) -> i32", "public func read(self: ref/Self) -> i32 => 1", "x.read()")]
    [InlineData("property item: i32 has get", "public computed item: i32\n        get(self: ref/Self) -> i32 => 1", "x.item")]
    public void InheritedRequirementDispatchCannotEmitAnUnimplementedProjection(string requirement, string implementation, string use)
    {
        var source = "contract C\n    " + requirement + "\nopen struct Base\n    " + implementation + "\nstruct Leaf: Base\n    Self is C\n    public init() => ()\nfunc read<T>(x: ref/T) -> i32\n    T is C\n    return " + use + "\nlet x = Leaf.init()\n_ = read(x@ref)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out var failure));
        Assert.NotNull(failure);
        Assert.Empty(output.ToString());
    }
}
