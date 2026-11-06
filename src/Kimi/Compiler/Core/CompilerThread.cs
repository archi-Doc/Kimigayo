// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the compile stack S, 64 MB: the reserve of every compiler thread, the host stack of IMPL A.4 (DECISIONS R4-M178). Runs a
// site's work on a pooled compiler thread while its caller waits, and makes every compiler thread (Create: the pool's,
// AllocationMeasurement's, the tests'). Input: a state, a static work and the caller's ExecutionContext. Output: the work's result,
// or its exception rethrown on the caller with its original stack. Invariants: a site on a compiler thread runs inline; a warm hop
// allocates nothing; the pool grows to the peak number of concurrent callers and never shrinks, and sequential hops reuse one
// worker; workers are background threads started without a context, and a job runs under its caller's captured context, else
// under the worker's own start context, so nothing a job sets stays on its worker; a job's references are cleared before its worker
// is pooled; across the hop, the caller's `when` filters run after the work's `finally` blocks (inline, before them), and no filter
// reads such state. Must not probe the stack, time out, cancel or retry; no second runner or thread maker; the transfer catch
// handles nothing and rethrows every exception (ARCH "Catch boundaries"); no site off R4-M178's list. A site begins
// `if (!CompilerThread.IsCurrent) { return CompilerThread.Run((this, a), static s => s.Item1.Entry(s.Item2)); }`.

using System.Runtime.ExceptionServices;

namespace Kimi.Compiler;

/// <summary>The compile stack: compiler threads with a 64 MB reserve, and the hop that runs a site's work on one.</summary>
internal static class CompilerThread
{
    /// <summary>S, the stack reserve of a compiler thread.</summary>
    internal const int StackSize = 64 << 20;

    private static readonly Lock Gate = new();

    private static readonly Stack<Worker> Idle = new();

    [ThreadStatic]
    private static bool current;

    /// <summary>Gets a value indicating whether the current thread is a compiler thread, on which a site runs inline.</summary>
    internal static bool IsCurrent => current;

    /// <summary>Starts a compiler thread: a background thread with the reserve S, started without its creator's context.</summary>
    /// <param name="start">What the thread runs.</param>
    /// <returns>The started thread.</returns>
    internal static Thread Create(ThreadStart start)
    {
        var thread = new Thread(
            static start =>
            {
                current = true;
                ((ThreadStart)start!)();
            },
            StackSize) { IsBackground = true, Name = "Kimi compiler" };
        thread.UnsafeStart(start);
        return thread;
    }

    /// <summary>Runs a site's work on a pooled compiler thread under the caller's context and waits for it.</summary>
    /// <typeparam name="TState">The site's arguments.</typeparam>
    /// <typeparam name="TResult">The site's result, <see cref="ValueTuple"/> for a site without one.</typeparam>
    /// <param name="state">The arguments.</param>
    /// <param name="work">The static work, the site's entry.</param>
    /// <returns>The work's result.</returns>
    internal static TResult Run<TState, TResult>(TState state, Func<TState, TResult> work)
    {
        if (current)
        {
            throw new InvalidOperationException("A site on a compiler thread runs inline.");
        }

        var job = Job<TState, TResult>.Current;
        job.Post(state, work);
        Worker? worker;
        lock (Gate)
        {
            Idle.TryPop(out worker);
        }

        (worker ?? new Worker()).Start(job);
        return job.Take();
    }

    private abstract class Job
    {
        private readonly ManualResetEventSlim done = new();

        // Runs the work on the worker under the caller's context, else the worker's start context, and clears the job's references.
        internal abstract void Execute(ExecutionContext start);

        internal void Signal() => this.done.Set();

        protected void Wait()
        {
            this.done.Wait();
            this.done.Reset();
        }
    }

    // One job per caller thread and instantiation, reused by every hop of that thread.
    private sealed class Job<TState, TResult> : Job
    {
        [ThreadStatic]
        private static Job<TState, TResult>? cached;

        private TState state = default!;
        private Func<TState, TResult>? work;
        private ExecutionContext? context;
        private TResult result = default!;
        private ExceptionDispatchInfo? failure;

        internal static Job<TState, TResult> Current => cached ??= new();

        internal void Post(TState state, Func<TState, TResult> work)
        {
            this.state = state;
            this.work = work;
            this.context = ExecutionContext.Capture();
        }

        internal override void Execute(ExecutionContext start)
        {
            try
            {
                ExecutionContext.Run(
                    this.context ?? start,
                    static job =>
                    {
                        var self = (Job<TState, TResult>)job!;
                        self.result = self.work!(self.state);
                    },
                    this);
            }
            catch (Exception exception)
            {
                // The transfer: it handles nothing; Take rethrows the same exception on the caller (ARCH "Catch boundaries").
                this.failure = ExceptionDispatchInfo.Capture(exception);
            }

            this.state = default!;
            this.work = null;
            this.context = null;
        }

        internal TResult Take()
        {
            this.Wait();
            var result = this.result;
            var failure = this.failure;
            this.result = default!;
            this.failure = null;
            failure?.Throw();
            return result;
        }
    }

    // A pooled compiler thread: it waits for a job, runs it, pools itself and only then signals its caller, so the caller's next
    // hop finds it in the pool.
    private sealed class Worker
    {
        private readonly ManualResetEventSlim posted = new();
        private Job? job;

        internal Worker() => Create(this.Loop);

        internal void Start(Job job)
        {
            this.job = job;
            this.posted.Set();
        }

        private void Loop()
        {
            var start = ExecutionContext.Capture()!;
            while (true)
            {
                this.posted.Wait();
                this.posted.Reset();
                var job = this.job!;
                this.job = null;
                job.Execute(start);
                lock (Gate)
                {
                    Idle.Push(this);
                }

                job.Signal();
            }
        }
    }
}
