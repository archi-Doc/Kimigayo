// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
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

    // A pooled allocation that the operation makes once after the warm-up, of a length that no trim drops, is measured like
    // any other allocation: it is late growth, not the effect of a pool's trim.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasuresAPooledAllocationThatNoTrimExplains()
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            if (++calls == AllocationMeasurement.WarmupOperations + 1)
            {
                GC.KeepAlive(ArrayPool<PoolItem>.Shared.Rent(64));
            }
        });
        Assert.True(bytes >= 64, $"{bytes} bytes");
        Assert.Equal(AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations, calls);
    }

    // The test apphost doubles as a process whose pools read High memory pressure (the GC's high-memory threshold at 10% of
    // RAM), where a full collection empties every thread's pooled buffers; it runs TrimsUnderHighPressure there.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public async Task DiscardsWindowsWhosePooledAllocationsFollowTrims()
    {
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(AllocationMeasurementTest).Assembly.Location, ".exe"), ["-method", "XunitTest.AllocationMeasurementTest.TrimsUnderHighPressure", "-explicit", "only"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        start.Environment["DOTNET_GCHighMemPercent"] = "0xA";
        using var probe = Process.Start(start)!;
        var report = probe.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        if (!probe.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            probe.Kill(entireProcessTree: true);
            probe.WaitForExit();
        }

        var text = await report;
        Assert.True(probe.ExitCode == 0 && text.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0", StringComparison.Ordinal), text);
    }

    // Under High memory pressure: a full collection before the first measured operation's Rent invalidates that window and
    // the next measures nothing; one before every operation's Rent invalidates all three windows, (4 + 12) + (4 + 28) +
    // (4 + 60) operations. Only the measuring thread uses the pool, so a trim leaves it no buffer to rent.
    [Trait("Purpose", "Allocation")]
    [Fact(Explicit = true)]
    public void TrimsUnderHighPressure()
    {
        var first = 0;
        var bytes = AllocationMeasurement.Measure(() => RentAfterTrim(++first == AllocationMeasurement.WarmupOperations + 1));
        var memory = GC.GetGCMemoryInfo();
        Assert.SkipUnless(memory.MemoryLoadBytes >= memory.HighMemoryLoadThresholdBytes * 0.9, "Pools read High pressure only in the process that DiscardsWindowsWhosePooledAllocationsFollowTrims starts.");
        Assert.Equal(0, bytes);
        Assert.True(first > AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations, $"{first} operations");
        var every = 0;
        Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() =>
        {
            every++;
            RentAfterTrim(true);
        }));
        Assert.Equal(112, every);

        static void RentAfterTrim(bool trim)
        {
            if (trim)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
            }

            ArrayPool<PoolItem>.Shared.Return(ArrayPool<PoolItem>.Shared.Rent(16));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }

    private readonly struct PoolItem;
}
