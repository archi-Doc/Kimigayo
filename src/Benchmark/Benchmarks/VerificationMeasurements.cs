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
        foreach (var count in new[] { 4, 16, 64 })
        {
            results.Add(Measure("guard histories", count, false, VerificationWorkloads.GuardHistories(count), 64));
        }

        foreach (var (count, checking) in new[] { (8, false), (16, false), (8, true) })
        {
            results.Add(Measure("live part loans", count, checking, VerificationWorkloads.LivePartLoans(count, checking), 5));
        }

        Console.WriteLine(JsonSerializer.Serialize(new { compiler = Compilation.CompilerVersion, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Measure(string workload, int count, bool checking, string source, int iterations)
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

        var retained = c.Ownership.Bodies.Sum(x => x.CheckingSeeds.Count + x.CheckingReplays.Count + x.CheckingRegions.Count);
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            Analyze();
        }

        return new { workload, count, checking, retainedRecords = retained, iterations, elapsedSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds };

        void Analyze()
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Measurement input must pass ownership verification.");
            }
        }
    }
}
