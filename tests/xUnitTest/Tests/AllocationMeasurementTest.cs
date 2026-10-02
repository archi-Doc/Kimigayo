// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AllocationMeasurementTest
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PreservesFixedCountsAndIsolatesTheMeasuringThread()
    {
        var context = new AsyncLocal<string?> { Value = "runner" };
        var runnerThread = Environment.CurrentManagedThreadId;
        var calls = 0;
        var isolated = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                isolated &= Environment.CurrentManagedThreadId != runnerThread && context.Value is null;
                calls++;
            },
            iterations: 128,
            warmupIterations: 100);
        Assert.True(isolated);
        Assert.Equal(228, calls);
        Assert.Equal(0, bytes);
        Assert.Equal("runner", context.Value);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void InvalidCountsNeverRunTheWork(int iterations, int warmupIterations)
    {
        var calls = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => AllocationMeasurement.Measure(() => calls++, iterations, warmupIterations));
        Assert.Equal(0, calls);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(8, 8)]
    [InlineData(128, 100)]
    public void DetectsAllocatingWork(int iterations, int warmupIterations)
        => Assert.True(AllocationMeasurement.Measure(() => GC.KeepAlive(new byte[128]), iterations, warmupIterations) >= 128 * iterations);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmupAllocationsStayOutsideTheFixedMeasurement()
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                if (++calls <= 100)
                {
                    GC.KeepAlive(new byte[128]);
                }
            },
            iterations: 128,
            warmupIterations: 100);
        Assert.Equal(228, calls);
        Assert.Equal(0, bytes);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }
}
