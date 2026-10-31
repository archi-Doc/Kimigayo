// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Reflection.Emit;
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
    // first therefore creates it whenever tiering first runs it at tier 0, such as in a callee first called on its own once its
    // caller's tier-1 code stops inlining it. A window that allocated while the measuring thread compiled such a comparer's
    // class constructor, as the runtime's JIT events report in the thread's order between the window's two markers, is invalid
    // in the same way; any other compilation excuses nothing. The whole window is invalid, so the operation's own one-time
    // allocation in it is not measured either, as with a trim. Not covered: a comparer whose class constructor is precompiled
    // (ReadyToRun) or shared (a reference type T) or was compiled by another thread before this one ran it. Cost: every
    // compilation in the process sends the listener an event, each window compiles a start marker, and a window that allocated
    // while compiling compiles an end marker and waits for it (at most MarkerWait; nothing is excused without it).
    private const int MaximumWindows = 3;

    private static readonly TimeSpan MarkerWait = TimeSpan.FromSeconds(10);

    private static readonly PoolEvents Pool = new();

    private static readonly CompileEvents Compiles = new();

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
                List<string>? causes = null;
                var run = 0;
                for (var window = 1; window <= MaximumWindows; window++)
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
                    var marker = CompileEvents.Start();
                    RetireAllocationContext();
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
                    var cause = Pool.FollowTrims(events, trims) ? "High-pressure ArrayPool trims or full pools"
                        : largest > 0 && compiled > 0 && Compiles.CreatedComparer(marker, MarkerWait) ? $"a default comparer created on the measuring thread, {largest} bytes"
                        : null;
                    if (cause is null)
                    {
                        allocated = largest;
                        return;
                    }

                    (causes ??= []).Add($"window {window}: {cause}");
                }

                throw new InvalidOperationException($"All {MaximumWindows} allocation windows were invalid ({string.Join("; ", causes!)}).");
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

    // The allocation of one run of an operation that cannot repeat on its state: a cold first build or analysis, or a designed
    // second pass. Measure is the rule otherwise. It runs on the calling thread, after the step that retires the thread's
    // allocation context; no warm-up, no repeated or retried window and no excused cause.
    internal static long MeasureOnce(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RetireAllocationContext();
        var before = GC.GetAllocatedBytesForCurrentThread();
        operation();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // A runtime pause that is not a collection can charge the unused tail of a partially used allocation context (up to ~8 KB)
    // to this thread. Retire it so zero-allocation work holds none. A gen0 collection retires it; a full one would also trim
    // shared pools (gen2 callbacks) under the measurements running on other threads.
    private static void RetireAllocationContext() => GC.Collect(0, GCCollectionMode.Forced, blocking: true);

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

    // The runtime reports each method it compiles, with the compiling thread, on its JIT keyword (0x10); the events reach the
    // listener on one thread, in each compiling thread's order. A window is bounded by two marker methods that the measuring
    // thread compiles, named after the window; a class constructor of EqualityComparer<T> or Comparer<T>, which creates the
    // Type's default comparer, that the same thread compiled between them is in the window.
    private sealed class CompileEvents : EventListener
    {
        private const EventKeywords Jit = (EventKeywords)0x10;
        private const string StartName = "AllocationWindowStart:";
        private const string EndName = "AllocationWindowEnd:";

        // The window each thread has open, by marker, and whether the thread created a default comparer in it; then the
        // closed windows' results until the measuring thread reads them. Only the listener's thread writes Open.
        private static readonly ConcurrentDictionary<long, (long Marker, bool Comparer)> Open = new();
        private static readonly ConcurrentDictionary<long, bool> Closed = new();
        private static long markers;

        // Opens a window on the current thread: compiles its start marker and returns the marker.
        internal static long Start()
        {
            var marker = Interlocked.Increment(ref markers);
            Compile(StartName + marker);
            return marker;
        }

        // Whether the current thread created a default comparer in the window: compiles its end marker and waits at most the
        // bound for the listener to close the window; without that, nothing is excused.
        internal bool CreatedComparer(long marker, TimeSpan wait)
        {
            Compile(EndName + marker);
            var deadline = DateTime.UtcNow + wait;
            bool created;
            while (!Closed.TryRemove(marker, out created))
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                Thread.Sleep(10);
            }

            return created;
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                this.EnableEvents(eventSource, EventLevel.Verbose, Jit);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName?.StartsWith("MethodLoadVerbose", StringComparison.Ordinal) != true)
            {
                return;
            }

            var names = eventData.PayloadNames!;
            var name = eventData.Payload![names.IndexOf("MethodName")] as string ?? string.Empty;
            var thread = eventData.OSThreadId;
            if (name.StartsWith(StartName, StringComparison.Ordinal))
            {
                Open[thread] = (long.Parse(name.AsSpan(StartName.Length), CultureInfo.InvariantCulture), false);
            }
            else if (Open.TryGetValue(thread, out var open))
            {
                if (name.StartsWith(EndName, StringComparison.Ordinal))
                {
                    Open.TryRemove(thread, out _);
                    if (long.Parse(name.AsSpan(EndName.Length), CultureInfo.InvariantCulture) == open.Marker)
                    {
                        Closed[open.Marker] = open.Comparer;
                    }
                }
                else if (name == ".cctor" && eventData.Payload[names.IndexOf("MethodNamespace")] is string type &&
                    (type.StartsWith("System.Collections.Generic.EqualityComparer`1[", StringComparison.Ordinal) ||
                    type.StartsWith("System.Collections.Generic.Comparer`1[", StringComparison.Ordinal)))
                {
                    Open[thread] = (open.Marker, true);
                }
            }
        }

        // Compiles and runs a fresh empty method with the name on the current thread.
        private static void Compile(string name)
        {
            var method = new DynamicMethod(name, null, null, typeof(CompileEvents).Module, skipVisibility: true);
            method.GetILGenerator().Emit(OpCodes.Ret);
            method.CreateDelegate<Action>()();
        }
    }
}
