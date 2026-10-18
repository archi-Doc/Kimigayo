// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kimi.Compiler;

namespace Benchmark;

// The check-time cost of the Generic shape under each analysis configuration (R5 K4b): a fresh check (Bind, CheckStartup and Analyze, the
// compiler pipeline's stages 2 to 4) and a warm Analyze, the same compilation rebound without an edit and analyzed again, as an LSP
// server repeats it. MIR code generation ("sel") adds the instance collector to Analyze.
internal static class AnalyzeCostMeasurements
{
    private const int Warmups = 4;
    private const int Samples = 7;
    private const int Iterations = 4;

    private static readonly (string Name, CompilerPipeline Pipeline, CompilerCodegen Codegen)[] Configurations =
    [
        ("legacy", CompilerPipeline.Legacy, CompilerCodegen.Legacy),
        ("mir", CompilerPipeline.Mir, CompilerCodegen.Legacy),
        ("sel", CompilerPipeline.Mir, CompilerCodegen.Mir),
    ];

    internal static void Run()
    {
        var results = new List<object>();
        foreach (var size in new[] { 1, 32, 64, 128 })
        {
            var source = CompilerPipelineBenchmark.GenericSource(size);
            foreach (var (name, pipeline, codegen) in Configurations)
            {
                for (var i = 0; i < Warmups; i++)
                {
                    Check(Create(source, pipeline, codegen));
                }

                var (freshTicks, freshBytes) = Measure(() => Create(source, pipeline, codegen), static c => Check(c));
                var warm = Create(source, pipeline, codegen);
                Check(warm);
                var (warmTicks, warmBytes) = Measure(() => warm, static c => Check(c));
                results.Add(new
                {
                    size,
                    configuration = name,
                    freshCheckMeanMilliseconds = Milliseconds(freshTicks),
                    freshCheckMeanAllocatedBytes = (double)freshBytes / (Samples * Iterations),
                    warmAnalyzeMeanMilliseconds = Milliseconds(warmTicks),
                    warmAnalyzeMeanAllocatedBytes = (double)warmBytes / (Samples * Iterations),
                    collectedItems = warm.Instances?.Items.Length ?? 0,
                });
            }
        }

        var report = new
        {
            schemaVersion = 1,
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerVersion,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency,
            warmups = Warmups,
            samples = Samples,
            iterationsPerSample = Iterations,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    // A compilation of the source under a configuration, prepared and parsed, outside measurement.
    private static Compilation Create(string source, CompilerPipeline pipeline, CompilerCodegen codegen)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = pipeline;
        c.Codegen = codegen;
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
