// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    // any other allocation: it is late growth, not the effect of a pool's trim. A first measurement compiles the pool's and
    // the listener's code for it, so that the measured window compiles nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasuresAPooledAllocationThatNoTrimExplains()
    {
        AllocationMeasurement.Measure(static () => ArrayPool<PoolItem>.Shared.Return(ArrayPool<PoolItem>.Shared.Rent(16)));
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

    // The first run of a method in the first measured operation compiles it on the measuring thread and allocates there, as
    // the first tier-0 run of a comparison does when it creates the comparer: that window is invalid, and the next measures
    // the operation, (4 + 12) + (4 + 28) operations in all. An allocation that recurs is still measured there.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardsTheWindowThatAllocatedWhileCompiling(bool recurring)
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            if (++calls == AllocationMeasurement.WarmupOperations + 1)
            {
                _ = recurring ? FirstRun<RecurringMarker>() : FirstRun<OnceMarker>();
            }

            if (recurring)
            {
                GC.KeepAlive(new byte[128]);
            }
        });
        Assert.Equal(48, calls);
        if (recurring)
        {
            Assert.True(bytes >= 128 * AllocationMeasurement.MeasuredOperations, $"{bytes} bytes");
        }
        else
        {
            Assert.Equal(0, bytes);
        }
    }

    // An operation that compiles and allocates every time disturbs every window: the measurement fails instead of passing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void FailsWhenCompilationDisturbsEveryWindow()
    {
        Type[] types = [typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(char), typeof(bool), typeof(float), typeof(double), typeof(decimal), typeof(nint), typeof(nuint)];
        var method = typeof(AllocationMeasurementTest).GetMethod(nameof(FirstRunOfPair), BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = 0;
        var failure = Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() =>
        {
            method.MakeGenericMethod(types[calls % types.Length], types[calls / types.Length]).Invoke(null, null);
            calls++;
        }));
        Assert.Contains("compiled", failure.Message, StringComparison.Ordinal);
        Assert.Equal(112, calls);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }

    // Compiled at its first run for each value Type, which also creates the Type's default comparer.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool FirstRun<T>()
        where T : struct
        => EqualityComparer<T>.Default.Equals((new T[1])[0], default);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstRunOfPair<T1, T2>()
        where T1 : struct
        where T2 : struct
        => GC.KeepAlive(new (T1, T2)[1]);

    private readonly struct PoolItem;

    private readonly struct OnceMarker;

    private readonly struct RecurringMarker;
}
