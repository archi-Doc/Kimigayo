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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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

    // G216 (a) U3a (C-38 B3; DECISIONS BU-104): premise discovery reads the evidence names once per solve, so its reads grow linearly
    // in a literal's elements beside the evidence that names the callee's Origins (`mk([.None x N], q, r)`), not with every leaf.
    [Fact]
    public void PremiseDiscoveryReadsGrowLinearlyInALiteralsElements()
    {
        var work = new[] { 200, 400, 800 }.Select(Work).ToArray();
        Assert.True(MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        static long Work(int count)
        {
            var c = MinimalEmissionTest.Analyze(
                "func mk(p: Array<Option<ref/(ref/i32 during a) during b>>, x: ref/i32 during a, y: ref/i32 during b) -> i32 => 0\n" +
                "func k(q: ref/i32, r: ref/i32) -> i32\n    return mk([" + string.Join(", ", Enumerable.Repeat(".None", count)) + "], q, r)\n");
            return c.Binding.EvidenceNameWork;
        }
    }

    // G216 (a) U3a (D) (DECISIONS BU-104 Rule E): Π keeps each premise once per entry, so Rule E's reads grow linearly in a literal's
    // elements when the callee's Origins are free (`mk([.None x N])`: nothing solves `a` or `b`).
    [Fact]
    public void FreeOriginPremiseReadsGrowLinearlyInALiteralsElements()
    {
        var work = new[] { 200, 400, 800 }.Select(Work).ToArray();
        Assert.True(work[0] > 0 && MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        static long Work(int count)
        {
            var c = MinimalEmissionTest.Analyze(
                "func mk(p: Array<Option<ref/(ref/i32 during a) during b>>) -> i32 => 0\n" +
                "func k() -> i32\n    return mk([" + string.Join(", ", Enumerable.Repeat(".None", count)) + "])\n");
            return c.Binding.FreePremiseWork;
        }
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmLargeSlotSetsReuseTheirTailStorage()
    {
        var c = MinimalEmissionTest.Analyze(Wide(130, true));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
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
