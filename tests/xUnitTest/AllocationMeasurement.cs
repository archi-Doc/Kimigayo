// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Runtime;

namespace XunitTest;

internal static class AllocationMeasurement
{
    // The standard window of every warm compiler-allocation regression, both on the measuring thread: warm-up operations,
    // then measured ones. After p operations on the measured state and b operations of this helper (b = 4 for the standard
    // window), a window of b + 8 operations adds entries k(p + b) + 1 to k(p + 2b + 8) of a leak of k entries per operation;
    // for p <= 8 that range doubles, so it contains a growth of any doubling storage whose first capacity is below
    // k(p + 2b + 8), k(p + 16) for the standard window. Call sites pass no counts and run no warm-up of their own.
    internal const int WarmupOperations = 4;
    internal const int MeasuredOperations = 12;

    // ArrayPool<T>.Shared empties every thread's pooled buffers when it trims under High memory pressure after a full
    // collection, or after any collection of a pool first used in the process; the next Rent then allocates. A window with
    // pooled-buffer events that all have such a cause, or a full pool, is invalid, and another warm-up and window follow; any
    // other pooled allocation, such as storage that first grows after the warm-up, is measured.
    // The first tier-0 run of a comparison creates EqualityComparer<T>.Default or Comparer<T>.Default (88 bytes on .NET 10)
    // once per process, charged to the running thread; optimized code needs none. A comparison that OSR or inlined code ran
    // first therefore creates it whenever tiering first runs it at tier 0, for example in a callee first called on its own once
    // its caller's tier-1 code stops inlining it, and the thread compiles that tier-0 method first. A window that allocated
    // while the measuring thread compiled methods is invalid in the same way; a window that allocated nothing is valid, and an
    // allocation that recurs is measured in the next window. A comparer of a reference type T runs shared code that may need no
    // compilation, so its creation is not covered.
    private const int MaximumWindows = 3;

    private static readonly PoolEvents Pool = new();

    // Keep test-runner execution context and thread setup outside the measured interval. The result is the largest
    // allocation of MeasuredOperations consecutive operations of the valid window, the whole standard window.
    // Zero remains a strict assertion at every call site.
    internal static long Measure(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        long allocated = 0;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Pool.RecordCurrentThread();
                var run = 0;
                for (var window = 1; ; window++)
                {
                    var trims = Pool.Trims;
                    for (var i = 0; i < WarmupOperations; i++)
                    {
                        operation();
                    }

                    // The counter before and after each of the window's b + 8 operations (12, then 28 and 60).
                    run += WarmupOperations;
                    var readings = new long[run + 8 + 1];
                    var events = Pool.OnCurrentThread;

                    // A runtime pause that is not a collection can charge the unused tail of a partially used
                    // allocation context (up to ~8 KB) to this thread. Retire it so zero-allocation work holds none.
                    // A gen0 collection retires it; a full one would also trim shared pools (gen2 callbacks) under
                    // the measurements running on other threads.
                    GC.Collect(0, GCCollectionMode.Forced, blocking: true);
                    var compiled = JitInfo.GetCompiledMethodCount(currentThread: true);
                    readings[0] = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 1; i < readings.Length; i++)
                    {
                        operation();
                        readings[i] = GC.GetAllocatedBytesForCurrentThread();
                    }

                    compiled = JitInfo.GetCompiledMethodCount(currentThread: true) - compiled;
                    run += readings.Length - 1;
                    var largest = Largest(readings);
                    var trimmed = Pool.FollowTrims(events, trims);
                    if (!trimmed && (largest == 0 || compiled == 0))
                    {
                        allocated = largest;
                        return;
                    }

                    if (window == MaximumWindows)
                    {
                        throw new InvalidOperationException(trimmed
                            ? $"High-pressure ArrayPool trims or full pools disturbed all {MaximumWindows} allocation windows."
                            : $"Trims or methods compiled on the measuring thread disturbed all {MaximumWindows} allocation windows; the last allocated {largest} bytes while {compiled} methods were compiled.");
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        using (ExecutionContext.SuppressFlow())
        {
            thread.Start();
        }

        // The bound only stops a hung measurement. A parallel Debug run can take several times longer than an isolated one, so
        // it is generous, for each window; the zero-byte assertions at the call sites are unchanged.
        if (!thread.Join(MaximumWindows * 300_000))
        {
            throw new TimeoutException("Allocation measurement did not complete.");
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return allocated;
    }

    // The largest allocation of MeasuredOperations consecutive operations of a window's readings.
    private static long Largest(long[] readings)
    {
        long largest = 0;
        for (var i = MeasuredOperations; i < readings.Length; i++)
        {
            largest = Math.Max(largest, readings[i] - readings[i - MeasuredOperations]);
        }

        return largest;
    }

    // ArrayPoolEventSource reports a pool's allocation or full-pool drop of a buffer synchronously on the thread that rents
    // or returns it, and each trim's memory pressure, then its drops, on the finalizer thread. The instance only keeps the
    // source enabled.
    private sealed class PoolEvents : EventListener
    {
        // Utilities.MemoryPressure.High; Low and Medium pressure trims drop only buffers left unused for 15 s or more.
        private const int HighPressure = 2;

        // The trim count after a High-pressure trim last dropped a buffer of each pool and length.
        private static readonly ConcurrentDictionary<(int Pool, int Length), long> Trimmed = new();
        private static long trims;
        private static int pressure;

        [ThreadStatic]
        private static bool recorded;

        [ThreadStatic]
        private static List<(int Pool, int Length)>? allocated;

        [ThreadStatic]
        private static int dropped;

        internal long Trims => Volatile.Read(ref trims);

        internal (int Allocated, int Dropped) OnCurrentThread => (allocated?.Count ?? 0, dropped);

        internal void RecordCurrentThread() => recorded = true;

        // Whether this thread raised pool events since the mark, each with a cause outside the measured operation: a buffer of
        // the same pool and length that a High-pressure trim dropped after the trim count `since`, or a full pool.
        internal bool FollowTrims((int Allocated, int Dropped) mark, long since)
        {
            if (this.OnCurrentThread == mark)
            {
                return false;
            }

            if (allocated?.Count > mark.Allocated)
            {
                // A trim runs as a finalizer; once the pending finalizers are done, every trim that ran in the window has
                // recorded its drops.
                GC.WaitForPendingFinalizers();
                for (var i = mark.Allocated; i < allocated.Count; i++)
                {
                    if (!Trimmed.TryGetValue(allocated[i], out var at) || at <= since)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
            {
                this.EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventName)
            {
                case "BufferAllocated" when recorded:
                    (allocated ??= []).Add(Key(eventData));
                    break;
                case "BufferDropped" when recorded:
                    dropped++;
                    break;
                case "BufferTrimPoll":
                    pressure = (int)eventData.Payload![1]!;
                    break;
                case "BufferTrimmed" when pressure == HighPressure:
                    Trimmed[Key(eventData)] = Interlocked.Increment(ref trims);
                    break;
            }

            // BufferAllocated and BufferTrimmed begin with the buffer's identity, its length and the pool's identity.
            static (int Pool, int Length) Key(EventWrittenEventArgs eventData) => ((int)eventData.Payload![2]!, (int)eventData.Payload[1]!);
        }
    }
}
