// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace XunitTest;

public class AllocationMeasurementTest
{
    [Fact]
    public void NestedAllocationClassesBelongToOneIsolatedCollection()
    {
        var assembly = typeof(AllocationMeasurementTest).Assembly;
        var collections = new CollectionPerClassTestCollectionFactory(new XunitTestAssembly(assembly, configFilePath: null));
        var allocationClasses = assembly.GetTypes().Where(type => type.IsNested && type.Name == "AllocationTests").ToArray();
        Assert.NotEmpty(allocationClasses);
        var isolated = collections.Get(allocationClasses[0]);
        foreach (var type in allocationClasses)
        {
            var collection = collections.Get(type);
            Assert.True(collection.DisableParallelization, $"{type.FullName} is not isolated under collection parallelism.");
            Assert.Same(isolated, collection);
            Assert.False(collections.Get(type.DeclaringType!).DisableParallelization);
        }

        Assert.False(collections.Get(typeof(AllocationMeasurementTest)).DisableParallelization);
    }

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

    // MeasureOnce runs its operation exactly once, on the calling thread, and reports that run's allocation: none here.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasureOnceRunsOnceOnTheCallingThread()
    {
        var callingThread = Environment.CurrentManagedThreadId;
        var calls = 0;
        var onCaller = true;
        var bytes = AllocationMeasurement.MeasureOnce(() =>
        {
            onCaller &= Environment.CurrentManagedThreadId == callingThread;
            calls++;
        });
        Assert.True(onCaller);
        Assert.Equal(1, calls);
        Assert.Equal(0, bytes);
    }

    // A byte[128] is 152 bytes on a 64-bit runtime (header, method table and length, then the elements).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasureOnceDetectsAllocatingWork()
        => Assert.True(AllocationMeasurement.MeasureOnce(() => GC.KeepAlive(new byte[128])) >= 152);

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
    public Task DiscardsWindowsWhosePooledAllocationsFollowTrims()
        => FreshProcess.RunExplicit(typeof(AllocationMeasurementTest), nameof(AllocationMeasurementTest.TrimsUnderHighPressure), ("DOTNET_GCHighMemPercent", "0xA"));

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

    // Creating a value Type's default comparer in the first measured operation, as the first tier-0 run of a comparison does,
    // compiles its class constructor on the measuring thread: that window is invalid, and the next measures the operation,
    // (4 + 12) + (4 + 28) operations in all. An allocation that recurs is still measured there.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardsTheWindowThatCreatedADefaultComparer(bool recurring)
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            if (++calls == AllocationMeasurement.WarmupOperations + 1)
            {
                _ = recurring ? CreateComparer<RecurringMarker>() : CreateComparer<OnceMarker>();
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

    // Storage that first grows after the warm-up is measured even when its operation compiles other code there.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasuresALateAllocationThatCompilesOtherCode()
    {
        var calls = 0;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            if (++calls == AllocationMeasurement.WarmupOperations + 1)
            {
                Grow<LateMarker>();
            }
        });
        Assert.Equal(AllocationMeasurement.WarmupOperations + AllocationMeasurement.MeasuredOperations, calls);
        Assert.True(bytes >= 64, $"{bytes} bytes");
    }

    // An operation that creates a default comparer and allocates every time disturbs every window: the measurement fails,
    // naming the cause, instead of passing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void FailsWhenComparerCreationDisturbsEveryWindow()
    {
        Type[] types = [typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(char), typeof(bool), typeof(float), typeof(double), typeof(decimal), typeof(nint), typeof(nuint)];
        var method = typeof(AllocationMeasurementTest).GetMethod(nameof(CreateComparer), BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = 0;
        var failure = Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() =>
        {
            var type = typeof(ValueTuple<,,>).MakeGenericType(typeof(EveryMarker), types[calls % types.Length], types[calls / types.Length]);
            method.MakeGenericMethod(type).Invoke(null, null);
            calls++;
        }));
        Assert.Contains("window 3: a default comparer created on the measuring thread", failure.Message, StringComparison.Ordinal);
        Assert.Equal(112, calls);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PropagatesWorkerFailure()
    {
        var failure = new InvalidOperationException("test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.Measure(() => throw failure)));
    }

    // Compiled at its first call for each value Type, which creates the Type's default comparer there.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CreateComparer<T>()
        where T : struct
        => EqualityComparer<T>.Default.Equals(default, default);

    // Compiled at its first call for each value Type, allocating storage without creating a comparer.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Grow<T>()
        where T : struct
        => GC.KeepAlive(new T[64]);

    private readonly struct PoolItem;

    private readonly struct OnceMarker;

    private readonly struct RecurringMarker;

    private readonly struct LateMarker;

    private readonly struct EveryMarker;
}
