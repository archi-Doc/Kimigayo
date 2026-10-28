// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Backend;

namespace Benchmark;

// The instance collector's cost inside one compilation (PLAN10-P3's collection share; R5 plan §5.3 item 10, §5.4 item 6, §5.5 item 7;
// DECISIONS R5-M148), without a compiler hook. For each compiler pipeline input, a compilation under the mir pipeline without MIR code
// generation is bound and its MIR built, validated and analyzed outside timing: BuildMir does there what sel's BuildMir does before it
// collects. Three fresh collectors then run over that program, each timed with its construction: cold, with the compilation's Layout, as
// sel's BuildMir runs it (the collection-and-layout share, and the check-time cost of collection); freshLayout, a new Layout with the Type
// context's memos warm; warm, the compilation's Layout again. Layout's first computations are freshLayout - warm and the Type context's
// first queries cold - freshLayout, both per operation inside one compilation. `--collection-cost-check` requires the three collections
// of each input to agree and the cold one to equal a sel compilation's checked set, and times nothing.
internal static class CollectionCostMeasurements
{
    private const int Warmups = 4;
    private const int Samples = 7;
    private const int Iterations = 4;

    private static readonly string[] Variants = ["cold", "freshLayout", "warm"];

    internal static void Run(bool check)
    {
        var results = new List<object>();
        foreach (var shape in Enum.GetValues<PipelineShape>())
        {
            foreach (var size in new[] { 1, 32, 64, 128 })
            {
                var input = new CompilerPipelineBenchmark { Shape = shape, Size = size };
                input.Setup();
                if (check)
                {
                    Check(input);
                }
                else
                {
                    results.Add(Result(input));
                }
            }
        }

        if (check)
        {
            return;
        }

        var report = new
        {
            schemaVersion = 1,
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerVersion,
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(CollectionCostMeasurements).Module.ModuleVersionId,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            affinity = OperatingSystem.IsWindows() ? (long)Process.GetCurrentProcess().ProcessorAffinity : -1,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            stopwatchFrequency = Stopwatch.Frequency,
            warmups = Warmups,
            samples = Samples,
            iterationsPerSample = Iterations,
            variants = Variants,
            results,
        };
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Console.WriteLine(JsonSerializer.Serialize(report, options));
    }

    // One input's measurements, in a frame of its own, so that no compilation of another input stays reachable during them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Result(CompilerPipelineBenchmark input)
    {
        for (var i = 0; i < Warmups; i++)
        {
            Operation(input.Source, null, null, 0);
        }

        var ticks = Variants.Select(_ => new long[Samples * Iterations]).ToArray();
        var bytes = Variants.Select(_ => new long[Samples * Iterations]).ToArray();
        var counts = (Bodies: 0, Items: 0, Records: 0);
        for (var i = 0; i < Samples * Iterations; i++)
        {
            counts = Operation(input.Source, ticks, bytes, i);
        }

        var variants = Enumerable.Range(0, Variants.Length).Select(v => new
        {
            name = Variants[v],
            medianMilliseconds = Milliseconds(Median(ticks[v])),
            meanMilliseconds = Milliseconds(ticks[v].Sum()) / ticks[v].Length,
            minMilliseconds = Milliseconds(ticks[v].Min()),
            meanAllocatedBytes = (double)bytes[v].Sum() / bytes[v].Length,
            ticks = ticks[v],
        }).ToArray();
        return new
        {
            shape = input.Shape.ToString(),
            size = input.Size,
            mirBodies = counts.Bodies,
            collectedItems = counts.Items,
            collectorRecords = counts.Records,
            layoutMedianMilliseconds = Milliseconds(Median(Difference(ticks[1], ticks[2]))),
            firstQueriesMedianMilliseconds = Milliseconds(Median(Difference(ticks[0], ticks[1]))),
            variants,
        };
    }

    // One operation: a checked mir compilation outside timing, then the three collections over its program, whose ticks and bytes are
    // recorded at the operation's index when the arrays are given; returns the program's bodies and the collections' items and records.
    private static (int Bodies, int Items, int Records) Operation(string source, long[][]? ticks, long[][]? bytes, int index)
    {
        var c = Checked(source, CompilerCodegen.Legacy);
        var program = c.BuildMir();
        Require(program.Records.Count == 0 && program.Findings.Count == 0, "MIR construction or validation failed.");
        Layout[] layouts = [c.Layout, new Layout(), c.Layout];
        var (items, records) = (-1, -1);
        for (var v = 0; v < layouts.Length; v++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var set = new InstanceCollector(c.Types, layouts[v]).Run(program, c.SourceModules.Length);
            var end = Stopwatch.GetTimestamp();
            if (ticks is not null && bytes is not null)
            {
                ticks[v][index] = end - start;
                bytes[v][index] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }

            Require(v == 0 || (set.Items.Length == items && set.Records.Count == records), "The three collections differ.");
            (items, records) = (set.Items.Length, set.Records.Count);
        }

        return (program.Bodies.Length, items, records);
    }

    // The check: the three collections agree (Operation) and equal a sel compilation's checked set, which its Analyze collects.
    private static void Check(CompilerPipelineBenchmark input)
    {
        var (bodies, items, records) = Operation(input.Source, null, null, 0);
        var sel = Checked(input.Source, CompilerCodegen.Mir);
        Require(sel.Analyze().IsVerified, "The sel analysis rejected the program.");
        var set = sel.Instances ?? throw new InvalidOperationException("The sel compilation has no checked set.");
        Require(set.Items.Length == items && set.Records.Count == records, $"items {items}/{set.Items.Length}, records {records}/{set.Records.Count}");
        Console.WriteLine($"PASS collection cost: shape={input.Shape}, size={input.Size}, MIR bodies={bodies}, items={items}, records={records}");
    }

    // A compilation of the source under the mir pipeline and the given code generator, prepared, parsed, bound and started.
    private static Compilation Checked(string source, CompilerCodegen codegen)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = codegen;
        Require(c.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        c.Kotonoha.AddSource(new SourceDocument("CollectionCost.kimi", source));
        Require(c.Bind().IsComplete, "Binding failed.");
        Require(c.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        return c;
    }

    private static long[] Difference(long[] minuend, long[] subtrahend) => minuend.Zip(subtrahend, (x, y) => x - y).ToArray();

    private static long Median(long[] values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }
}
