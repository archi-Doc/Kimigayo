// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Xunit;
using Xunit.v3;

namespace XunitTest;

public class AllocationMeasurementTest
{
    // The variable under which the test apphost's pools read High memory pressure: the GC's high-memory threshold at 10% of RAM.
    private static readonly (string Name, string Value) HighPressure = ("DOTNET_GCHighMemPercent", "0xA");

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
            isolated &= Environment.CurrentManagedThreadId != runnerThread && context.Value is null && CompilerThread.IsCurrent;
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

    // MeasureOnce runs its operation exactly once, on a compiler thread (DECISIONS R4-M178), and reports that run's allocation:
    // none here.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasureOnceRunsOnceOnACompilerThread()
    {
        var calls = 0;
        var onCompilerThread = true;
        var bytes = AllocationMeasurement.MeasureOnce(() =>
        {
            onCompilerThread &= CompilerThread.IsCurrent;
            calls++;
        });
        Assert.True(onCompilerThread);
        Assert.Equal(1, calls);
        Assert.Equal(0, bytes);
    }

    // A byte[128] is 152 bytes on a 64-bit runtime (header, method table and length, then the elements).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void MeasureOnceDetectsAllocatingWork()
        => Assert.True(AllocationMeasurement.MeasureOnce(() => GC.KeepAlive(new byte[128])) >= 152);

    // A cold series runs only in AllocationIsolationCollection (G650): this class runs beside other collections, so the series refuses
    // before it makes any state.
    [Fact]
    public void MeasureSeriesRefusesOutsideTheIsolatedCollection()
    {
        var made = 0;
        Assert.Throws<InvalidOperationException>(() => AllocationMeasurement.MeasureSeries(1, _ =>
        {
            made++;
            return static () => { };
        }));
        Assert.Equal(0, made);
    }

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
        => FreshProcess.RunExplicit(typeof(AllocationMeasurementTest), nameof(AllocationMeasurementTest.TrimsUnderHighPressure), HighPressure);

    // Under High memory pressure: a full collection before the first measured operation's Rent invalidates that window and
    // the next measures nothing; one before every operation's Rent invalidates all three windows, (4 + 12) + (4 + 28) +
    // (4 + 60) operations. Only the measuring thread uses the pool, so a trim leaves it no buffer to rent.
    [Trait("Purpose", "Allocation")]
    [Fact(Explicit = true)]
    public void TrimsUnderHighPressure()
    {
        var first = 0;
        var bytes = AllocationMeasurement.Measure(() => RentAfterTrim(++first == AllocationMeasurement.WarmupOperations + 1));
        SkipUnlessHighPressure(nameof(AllocationMeasurementTest.DiscardsWindowsWhosePooledAllocationsFollowTrims));
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
                Trim();
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

    // A full collection and its finalizers, the pools' trim among them: under High pressure it empties every thread's pooled buffers.
    private static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
    }

    // Read after a collection: an Explicit child of HighPressure's process measures only there.
    private static void SkipUnlessHighPressure(string parent)
    {
        var memory = GC.GetGCMemoryInfo();
        Assert.SkipUnless(memory.MemoryLoadBytes >= memory.HighMemoryLoadThresholdBytes * 0.9, $"Pools read High pressure only in the process that {parent} starts.");
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

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        // MeasureSeries makes a fresh state per index and one more of index 0, whose unmeasured run takes the process's one-time
        // initialization: OneTime's 4 KB table falls in no measured run, so every index, the first included, reads only its
        // run's byte[64 * size] (88 to 280 bytes). Without that run, index 0 would read the 4 KB.
        [Fact]
        public void MeasureSeriesMeasuresNoOneTimeInitialization()
        {
            var made = new List<int>();
            var allocated = AllocationMeasurement.MeasureSeries(3, i =>
            {
                made.Add(i);
                var size = 1 << i;
                return () => GC.KeepAlive(OneTime.Run(size));
            });
            Assert.All(allocated.Select(static (x, i) => (x, i)), static x => Assert.InRange(x.x, 64 << x.i, 1024));
            Assert.Equal([0, 0, 1, 2], made);
        }

        // G650 (B): a thread's pooled buffers are process state that a High-pressure trim, or whatever the thread ran before,
        // changes between two runs, so every run of a series, the unmeasured one too, has a new compiler thread, whose own pooled
        // buffers are empty. In a process whose pools read High pressure it runs ASeriesUnderHighPressure.
        [Fact]
        public Task EveryRunOfASeriesPaysThePooledBuffersItRents()
            => FreshProcess.RunExplicit(typeof(AllocationTests), nameof(AllocationTests.ASeriesUnderHighPressure), HighPressure);

        // Each run rents and returns one PoolItem[] of a length that nothing else in the process rents, 16 KB at the first size.
        // With a trim before run 1, and with none, every run pays its buffer once: on one thread for the whole series, run 0
        // found the unmeasured run's buffer and only the run after the trim paid (0 / 16,408 / 0 B). A rented length that grows
        // with the size shows in the bytes as it is.
        [Fact(Explicit = true)]
        public void ASeriesUnderHighPressure()
        {
            const int Length = 1 << 14;
            var same = AllocationMeasurement.MeasureSeries(3, i =>
            {
                if (i == 1)
                {
                    Trim();
                }

                return static () => ArrayPool<PoolItem>.Shared.Return(ArrayPool<PoolItem>.Shared.Rent(Length));
            });
            SkipUnlessHighPressure(nameof(AllocationTests.EveryRunOfASeriesPaysThePooledBuffersItRents));
            Assert.True(same.All(static x => x is >= Length and < 2 * Length), string.Join(" / ", same));
            var growing = AllocationMeasurement.MeasureSeries(3, i =>
            {
                var length = Length << i;
                return () => ArrayPool<PoolItem>.Shared.Return(ArrayPool<PoolItem>.Shared.Rent(length));
            });
            Assert.True(growing.Select(static (x, i) => (Bytes: x, Buffer: (long)Length << i)).All(static x => x.Bytes >= x.Buffer && x.Bytes < 2 * x.Buffer), string.Join(" / ", growing));
        }
    }

    private readonly struct PoolItem;

    private readonly struct OnceMarker;

    private readonly struct RecurringMarker;

    private readonly struct LateMarker;

    private readonly struct EveryMarker;

    // An operation with a one-time initialization: its first run in the process allocates a 4 KB table (only
    // MeasureSeriesMeasuresNoOneTimeInitialization runs it), then each run allocates a byte[64 * size]. A static readonly array
    // initializer would not do: the runtime may place it on its non-GC heap, which the thread's counter does not see.
    private static class OneTime
    {
        private static byte[]? table;

        internal static byte[] Run(int size) => new byte[(table ??= new byte[4096]).Length / 4096 * 64 * size];
    }
}
