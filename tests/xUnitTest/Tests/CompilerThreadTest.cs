// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The compile stack (DECISIONS R4-M178). Isolated, so that no other test's hop takes a worker between two hops of one row.
[Collection<AllocationIsolationCollection>]
public class CompilerThreadTest
{
    private static readonly AsyncLocal<string?> Flowing = new();

    [Fact]
    public void ASiteOnACompilerThreadRunsInline()
    {
        Assert.False(CompilerThread.IsCurrent);
        var (worker, site) = CompilerThread.Run(0, static depth => (Environment.CurrentManagedThreadId, Site(depth)));
        Assert.Equal(worker, site);
        Assert.NotEqual(Environment.CurrentManagedThreadId, worker);
        Assert.IsType<InvalidOperationException>(CompilerThread.Run(0, static _ => Record(() => CompilerThread.Run(0, static x => x))));

        static int Site(int depth) => CompilerThread.IsCurrent ? Environment.CurrentManagedThreadId : CompilerThread.Run(depth, static d => Site(d));
    }

    // The caller gets the work's own exception instance, of its type, with the frame that threw it.
    [Fact]
    public void TheWorksExceptionReachesTheCallerWithItsStack()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => CompilerThread.Run(3, static n => Throw(n)));
        Assert.Equal("n", thrown.ParamName);
        Assert.Contains(nameof(Throw), thrown.StackTrace);
        var same = new InvalidDataException("same");
        Assert.Same(same, Assert.Throws<InvalidDataException>(() => CompilerThread.Run(same, static e => Throw(e))));
    }

    // A job runs under its caller's ExecutionContext: its culture and AsyncLocals, as the inline call would.
    [Fact]
    public void TheCallersCultureAndAsyncLocalsFlow()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Flowing.Value = "caller";
            var (culture, value) = CompilerThread.Run(0, static _ => (CultureInfo.CurrentCulture.Name, Flowing.Value));
            Assert.Equal("tr-TR", culture);
            Assert.Equal("caller", value);
        }
        finally
        {
            Flowing.Value = null;
            CultureInfo.CurrentCulture = previous;
        }
    }

    // Nothing stays on a worker: a thread Create makes keeps no creator context, a job's own writes end with it, whether it ran
    // under its caller's context or, having captured none (flow suppressed), under the worker's start context, and a later job
    // that captured nothing sees that empty context on the same worker.
    [Fact]
    public void AWorkerKeepsNoContext()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Flowing.Value = "creator";
            (string? Culture, string? Value) created = default;
            CompilerThread.Create(() => created = (CultureInfo.CurrentCulture.Name, Flowing.Value)).Join();
            Assert.NotEqual("tr-TR", created.Culture);
            Assert.Null(created.Value);

            var flowing = CompilerThread.Run("flowing", Write);
            int suppressed;
            (int Worker, string Culture, string? Value) later;
            using (ExecutionContext.SuppressFlow())
            {
                suppressed = CompilerThread.Run("suppressed", Write);
                later = CompilerThread.Run(0, static _ => (Environment.CurrentManagedThreadId, CultureInfo.CurrentCulture.Name, Flowing.Value));
            }

            Assert.Equal(flowing, suppressed);
            Assert.Equal(flowing, later.Worker);
            Assert.Equal(created.Culture, later.Culture);
            Assert.Null(later.Value);
            Assert.Equal("creator", Flowing.Value);
            Assert.Equal("tr-TR", CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            Flowing.Value = null;
            CultureInfo.CurrentCulture = previous;
        }

        static int Write(string value)
        {
            Flowing.Value = value;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            return Environment.CurrentManagedThreadId;
        }
    }

    // A worker pools itself before it signals, so sequential hops reuse one worker; concurrent callers get a worker each, and
    // workers are background compiler threads.
    [Fact]
    public void SequentialHopsReuseAWorkerAndConcurrentCallersGetOneEach()
    {
        var first = CompilerThread.Run(0, static _ => Environment.CurrentManagedThreadId);
        Assert.Equal(first, CompilerThread.Run(0, static _ => Environment.CurrentManagedThreadId));
        Assert.True(CompilerThread.Run(0, static _ => Thread.CurrentThread.IsBackground && Thread.CurrentThread.Name == "Kimi compiler"));

        using var both = new Barrier(2);
        var workers = new int[2];
        var met = new bool[2];
        var callers = Enumerable.Range(0, 2).Select(i => new Thread(() =>
        {
            (workers[i], met[i]) = CompilerThread.Run(
                both,
                static barrier => (Environment.CurrentManagedThreadId, barrier.SignalAndWait(TimeSpan.FromSeconds(60))));
        })).ToArray();
        foreach (var caller in callers)
        {
            caller.Start();
        }

        foreach (var caller in callers)
        {
            caller.Join();
        }

        Assert.True(met[0] && met[1]);
        Assert.NotEqual(workers[0], workers[1]);
    }

    // About 8 MB of recursion, which ends a 1.5 MB thread (the default) with a stack overflow, completes on a compiler thread.
    [Fact]
    public void ACompilerThreadHoldsEightMegabytesOfRecursion()
    {
        var sum = -1;
        CompilerThread.Create(() => sum = Recurse(8 * 1024)).Join();
        Assert.Equal(0, sum);
    }

    // A warm hop allocates nothing on its caller or on its worker, also under a culture and an AsyncLocal with a change handler.
    // AllocationMeasurement.MeasureHops measures from the test's thread, which is not a compiler thread.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmHopAllocatesNothing()
    {
        Assert.Equal((0, 0), AllocationMeasurement.MeasureHops());
        var previous = CultureInfo.CurrentCulture;
        var noticed = new AsyncLocal<string?>(static _ => { });
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            noticed.Value = "caller";
            Assert.Equal((0, 0), AllocationMeasurement.MeasureHops());
        }
        finally
        {
            noticed.Value = null;
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Throw(int n) => throw new ArgumentOutOfRangeException(nameof(n), n, "thrown on the worker");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Throw(Exception exception) => throw exception;

    // About 1 KB of stack per level.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Recurse(int depth)
    {
        Span<byte> frame = stackalloc byte[1024];
        frame[depth & 1023] = (byte)depth;
        var below = depth == 0 ? 0 : Recurse(depth - 1);
        return below + frame[depth & 1023] - (byte)depth;
    }
}
