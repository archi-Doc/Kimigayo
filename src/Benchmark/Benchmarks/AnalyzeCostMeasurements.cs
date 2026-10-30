// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kimi.Compiler;

namespace Benchmark;

// The check-time cost of the Generic shape (R5 K4b): a fresh check (Bind, CheckStartup and Analyze, the compiler pipeline's stages 2 to 4)
// and a warm Analyze, the same compilation rebound without an edit and analyzed again, as an LSP server repeats it. One configuration,
// the process's: every pipeline's Analyze builds, analyzes and collects the MIR and publishes its records (DECISIONS BU-48, R5-M156), and
// MIR code generation is the one code generator (R5-M160), so the former legacy, mir and sel rows measured one check; `compiler` names a
// non-default pipeline. Retained memory is the managed heap after full collections with the compilation alive: after its first check,
// and the growth over its warm Analyzes.
internal static partial class AnalyzeCostMeasurements
{
    private const int Warmups = 4;
    private const int Samples = 7;
    private const int Iterations = 4;

    internal static void Run()
    {
        var results = new List<object>();
        foreach (var size in new[] { 1, 32, 64, 128 })
        {
            results.Add(Result(size, CompilerPipelineBenchmark.GenericSource(size)));
        }

        var report = new
        {
            schemaVersion = 3,
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerIdentity,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency,
            warmups = Warmups,
            samples = Samples,
            iterationsPerSample = Iterations,
            results,
        };
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Console.WriteLine(JsonSerializer.Serialize(report, options));
    }

    // One size's measurements, in a frame of its own, so that no compilation of another stays reachable during them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Result(int size, string source)
    {
        for (var i = 0; i < Warmups; i++)
        {
            Check(Create(source));
        }

        var (freshTicks, freshBytes) = Measure(() => Create(source), static c => Check(c));
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var warm = Create(source);
        Check(warm);
        var retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
        var (warmTicks, warmBytes) = Measure(() => warm, static c => Check(c));
        var growth = GC.GetTotalMemory(forceFullCollection: true) - baseline - retained;
        var result = new
        {
            size,
            freshCheckMeanMilliseconds = Milliseconds(freshTicks),
            freshCheckMeanAllocatedBytes = (double)freshBytes / (Samples * Iterations),
            warmAnalyzeMeanMilliseconds = Milliseconds(warmTicks),
            warmAnalyzeMeanAllocatedBytes = (double)warmBytes / (Samples * Iterations),
            retainedBytes = retained,
            warmRetainedGrowthBytes = growth,
            collectedItems = warm.Instances?.Items.Length ?? 0,
        };
        GC.KeepAlive(warm);
        return result;
    }

    // A compilation of the source, prepared and parsed, outside measurement.
    private static Compilation Create(string source)
    {
        var c = Compilation.CreateForTest();
        Require(c.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        c.Kotonoha.AddSource(new SourceDocument("AnalyzeCost.kimi", source));
        return c;
    }

    // The check of one compilation: Binding, startup and the analysis, which must accept the program.
    private static void Check(Compilation c)
    {
        Require(c.Bind().IsComplete, "Binding failed.");
        Require(c.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        Require(c.Analyze().IsVerified, "The analysis rejected the program.");
    }

    // The summed time and current-thread allocation of the operation over every sample, each on a compilation the setup gives untimed.
    private static (long Ticks, long Bytes) Measure(Func<Compilation> setup, Action<Compilation> operation)
    {
        long ticks = 0;
        long bytes = 0;
        for (var i = 0; i < Samples * Iterations; i++)
        {
            var c = setup();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            operation(c);
            ticks += Stopwatch.GetTimestamp() - start;
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }

        return (ticks, bytes);
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency / (Samples * Iterations);

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }
}
