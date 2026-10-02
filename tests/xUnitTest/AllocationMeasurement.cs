// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

internal static class AllocationMeasurement
{
    // Keep test-runner execution context and thread setup outside the measured interval.
    // Warm the measuring thread too; zero remains a strict assertion at every call site.
    internal static long Measure(Action operation, int iterations = 8, int? warmupIterations = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupIterations ?? iterations);
        long allocated = -1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < (warmupIterations ?? iterations); i++)
                {
                    operation();
                }

                // A runtime pause that is not a collection can charge the unused tail of a partially used
                // allocation context (up to ~8 KB) to this thread. Retire it so zero-allocation work holds none.
                GC.Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < iterations; i++)
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
