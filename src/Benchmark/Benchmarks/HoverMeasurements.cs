// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Verification;

namespace Benchmark;

internal static class HoverMeasurements
{
    private const int Warmup = 32;
    private const int Samples = 7;

    internal static void Run()
    {
        var results = new List<object>();
        using var text = new TextDocument(HoverWorkloads.Text);
        foreach (var name in new[] { "short", "long-comment", "maximum-comment", "long-effects", "identity-dag", "wide-identity" })
        {
            var participants = HoverWorkloads.Create(name);
            HoverAnswer answer = default;
            var cold = Stopwatch.GetTimestamp();
            answer = new HoverState(participants, true).Find(text, new(0, 8));
            results.Add(new { name, operation = "beforeWarmup", milliseconds = Stopwatch.GetElapsedTime(cold).TotalMilliseconds, outputLength = answer.Body?.Length ?? 0, answer.Reason });
            results.Add(new { name, operation = "initialFourConfigurations", cost = Measure(() => answer = new HoverState(participants, true).Find(text, new(0, 8)), 64) });
            var cached = new HoverState(participants, true);
            answer = cached.Find(text, new(0, 8));
            results.Add(new { name, operation = "repeated", cost = Measure(() => answer = cached.Find(text, new(0, 8)), 10_000) });
            for (var edit = 0; edit < HoverLimits.Edits; edit++)
            {
                cached.Edited(0, 0, 0);
            }

            answer = cached.Find(text, new(0, 8));
            results.Add(new { name, operation = "previousMaximumHistory", cost = Measure(() => answer = cached.Find(text, new(0, 8)), 10_000) });
            if (name is "short" or "long-comment" or "maximum-comment" or "long-effects" or "wide-identity")
            {
                results.Add(new { name, operation = "queuedInitialHoverThenEdit", cost = Queued(participants, 1, false) });
                results.Add(new { name, operation = "queued100CachedHoversThenEdit", cost = Queued(participants, 100, true) });
            }

            GC.KeepAlive(answer);
        }

        foreach (var enabled in new[] { false, true })
        {
            results.Add(new
            {
                name = "compiler",
                operation = enabled ? "newCompilationWithHover" : "newCompilationWithoutHover",
                cost = Measure(
                () =>
                {
                    var compilation = Compilation.CreateForTest();
                    if (!compilation.Prepare("x86_64-pc-windows-msvc"))
                    {
                        throw new InvalidOperationException("The measured target must be prepared");
                    }

                    compilation.CollectHover = enabled;
                    compilation.CollectDocumentation = enabled;
                    compilation.Kotonoha.AddSource(new("hover.kimi", HoverWorkloads.Program));
                    if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Ownership.Analyze().IsVerified)
                    {
                        throw new InvalidOperationException("The measured compiler workload must verify");
                    }

                    if (enabled)
                    {
                        GC.KeepAlive(compilation.Binding.CreateHoverSnapshot());
                    }
                },
                16),
            });
        }

        var report = new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            warmup = Warmup,
            samples = Samples,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Measure(Action action, int iterations)
    {
        for (var i = 0; i < Warmup; i++)
        {
            action();
        }

        var milliseconds = new double[Samples];
        var bytes = new double[Samples];
        for (var sample = 0; sample < Samples; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            milliseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / iterations;
            bytes[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        }

        return new { iterations, milliseconds, bytes };
    }

    // Model an already queued FIFO burst on the real state owner. The interval ends after didChange is applied;
    // Response construction/enqueue is inside it. Parsing, transport and asynchronous serialization/drain are outside it.
    private static object Queued(HoverParticipant[] participants, int requests, bool cached)
    {
        var milliseconds = new double[Samples];
        var bytes = new long[Samples];
        for (var sample = -Warmup; sample < Samples; sample++)
        {
            using var workload = HoverWorkloads.Session(participants);
            if (cached)
            {
                workload.Session.Process(workload.Hover, 0);
                workload.Sender.FlushAsync().GetAwaiter().GetResult();
            }

            var before = GC.GetTotalAllocatedBytes(true);
            var start = Stopwatch.GetTimestamp();
            for (var request = 0; request < requests; request++)
            {
                workload.Session.Process(workload.Hover, 0);
            }

            workload.Session.Process(workload.Edit, 0);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            workload.Sender.FlushAsync().GetAwaiter().GetResult();
            if (sample >= 0)
            {
                milliseconds[sample] = elapsed;
                bytes[sample] = GC.GetTotalAllocatedBytes(true) - before;
            }
        }

        return new { requests, milliseconds, bytesIncludingSender = bytes };
    }
}
