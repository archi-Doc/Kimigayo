// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AllocationMeasurementTest
{
    [Fact]
    public void DetectsAllocatingWork()
        => Assert.True(AllocationMeasurement.Measure(() => GC.KeepAlive(new byte[128])) >= 128 * 8);

    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }
}
