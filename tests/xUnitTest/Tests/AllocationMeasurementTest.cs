// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AllocationMeasurementTest
{
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RunsTheStandardWindowAndIsolatesTheMeasuringThread()
    {
        var context = new AsyncLocal<string?> { Value = "runner" };
        var runnerThread = Environment.CurrentManagedThreadId;
        var calls = 0;
        var isolated = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            isolated &= Environment.CurrentManagedThreadId != runnerThread && context.Value is null;
            calls++;
        });
        Assert.True(isolated);
        Assert.Equal(AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations, calls);
        Assert.Equal(0, bytes);
        Assert.Equal("runner", context.Value);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DetectsAllocatingWork()
        => Assert.True(AllocationMeasurement.Measure(() => GC.KeepAlive(new byte[128])) >= 128 * AllocationMeasurement.MeasuredOperations);

    // Allocating in exactly the warm-up operations measures nothing, and allocating in the first measured operation
    // is measured: together they fix the split of the window.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuresExactlyTheOperationsAfterTheWarmup(bool firstMeasured)
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            calls++;
            if (firstMeasured ? calls == AllocationMeasurement.WarmupOperations + 1 : calls <= AllocationMeasurement.WarmupOperations)
            {
                GC.KeepAlive(new byte[128]);
            }
        });
        Assert.Equal(AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations, calls);
        if (firstMeasured)
        {
            Assert.True(bytes >= 128);
        }
        else
        {
            Assert.Equal(0, bytes);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }
}
