// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi.Compiler;
using Verification;

namespace Benchmark;

// Timing workloads are opt-in. Allocation and retained-capacity assertions stay in xUnit.
internal static class VerificationMeasurements
{
    internal static void Run()
    {
        var results = new List<object>();
        foreach (var count in new[] { 8, 16 })
        {
            results.Add(Measure("live part loans", count, VerificationWorkloads.LivePartLoans(count), 5));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { compiler = Compilation.CompilerVersion, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Measure(string workload, int count, string source, int iterations)
    {
        var c = Compilation.CreateForTest();
        if (!c.Prepare("x86_64-pc-windows-msvc"))
        {
            throw new InvalidOperationException("Measurement target must be prepared.");
        }

        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        if (!c.Bind().IsComplete || !c.Binding.CheckStartup(OutputKind.Application).IsComplete)
        {
            throw new InvalidOperationException("Measurement input must bind.");
        }

        for (var i = 0; i < 8; i++)
        {
            Analyze();
        }

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            Analyze();
        }

        return new { workload, count, iterations, elapsedSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds };

        void Analyze()
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Measurement input must pass ownership verification.");
            }
        }
    }
}
