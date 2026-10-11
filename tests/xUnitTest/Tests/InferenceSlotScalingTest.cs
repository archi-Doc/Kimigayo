// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InferenceSlotScalingTest
{
    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    [InlineData(false, 130)]
    [InlineData(true, 130)]
    public void StructuralSlotsHaveNoWordSizeLimit(bool constructor, int count)
    {
        var c = MinimalEmissionTest.Analyze(Wide(count, constructor));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    [InlineData(false, 130)]
    [InlineData(true, 130)]
    public void AnUnboundHighSlotRemainsRankableAndNamesItsActualSlot(bool constructor, int count)
    {
        var c = MinimalEmissionTest.Analyze(Wide(count, constructor, true));
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnboundTypeArgument_Kd", record.Code);
        Assert.Contains($"T{count - 1}", record.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AHighWaitingArgumentRetainsItsUnboundSignature()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 65).Select(i => $"p{i}: i32"));
        var values = string.Join(", ", Enumerable.Repeat("0", 65));
        var c = MinimalEmissionTest.Analyze($"func accept<T>({parameters}, action: (T) -> i32) => ()\naccept({values}, func (x) => 0)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnboundTypeArgument);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmLargeSlotSetsReuseTheirTailStorage()
    {
        var c = MinimalEmissionTest.Analyze(Wide(130, true));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
    }

    private static string Wide(int count, bool constructor, bool unbound = false)
    {
        var slots = string.Join(", ", Enumerable.Range(0, count).Select(i => $"T{i}"));
        var parameters = string.Join(", ", Enumerable.Range(0, count - (unbound ? 1 : 0)).Select(i => $"p{i}: T{i}"));
        var values = string.Join(", ", Enumerable.Repeat("n", count - (unbound ? 1 : 0)));
        return (constructor ? $"struct Wide<{slots}>\n    public init({parameters}) => ()\n" : $"func wide<{slots}>({parameters}) => ()\n") +
            "let n: i32 = 42\n" + (constructor ? "let result = Wide.init" : "wide") + $"({values})";
    }
}
