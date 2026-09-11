// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

internal static class AllocationMeasurement
{
    // Keep test-runner execution context and thread setup outside the measured interval.
    // Warm the measuring thread too; zero remains a strict assertion at every call site.
    internal static long Measure(Action operation, int iterations = 8)
    {
        long allocated = -1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    operation();
                }

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

        if (!thread.Join(30_000))
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
