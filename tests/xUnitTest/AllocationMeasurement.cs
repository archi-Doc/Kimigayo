// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

internal static class AllocationMeasurement
{
    // The standard window of every warm compiler-allocation regression, both on the measuring thread: warm-up operations,
    // then measured ones. After p operations on the measured state, the measured operations add entries k(p + 4) + 1 to
    // k(p + 16) of a leak of k entries per operation; for p <= 8 that range doubles, so it contains a growth of any
    // doubling storage whose first capacity is below k(p + 16). Call sites pass no counts and run no warm-up of their own.
    internal const int WarmupOperations = 4;
    internal const int MeasuredOperations = 12;

    // Keep test-runner execution context and thread setup outside the measured interval.
    // Zero remains a strict assertion at every call site.
    internal static long Measure(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        long allocated = -1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < WarmupOperations; i++)
                {
                    operation();
                }

                // A runtime pause that is not a collection can charge the unused tail of a partially used
                // allocation context (up to ~8 KB) to this thread. Retire it so zero-allocation work holds none.
                // A gen0 collection retires it; a full one would also trim shared pools (gen2 callbacks) under
                // the measurements running on other threads.
                GC.Collect(0, GCCollectionMode.Forced, blocking: true);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < MeasuredOperations; i++)
                {
                    operation();
                }

                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
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
        // it is generous; the zero-byte assertions at the call sites are unchanged.
        if (!thread.Join(300_000))
        {
            throw new TimeoutException("Allocation measurement did not complete.");
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return allocated;
    }
}
